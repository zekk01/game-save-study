using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SaveStudyUnity
{
    public static class SaveStudyBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartRunner()
        {
            var runnerObject = new GameObject("SaveStudyRunner");
            UnityEngine.Object.DontDestroyOnLoad(runnerObject);
            runnerObject.AddComponent<SaveStudyRunner>();
        }
    }

    public sealed class SaveStudyRunner : MonoBehaviour
    {
        private const int DefaultChunkSize = 4096;
        private string[] args;

        private void Awake()
        {
            args = Environment.GetCommandLineArgs();
            try
            {
                string mode = GetString("--save-study-mode", "");
                switch (mode)
                {
                    case "catalog": RunCatalog(); break;
                    case "fault": RunFault(); break;
                    case "recover": RunRecovery(); break;
                    case "timing": RunTiming(); break;
                    default: throw new ArgumentException("Missing/unknown --save-study-mode");
                }
            }
            catch (Exception ex)
            {
                WriteError(ex);
                Application.Quit(2);
            }
        }

        private void RunCatalog()
        {
            int itemCount = GetInt("--item-count", 25);
            int chunkSize = GetInt("--chunk-size", DefaultChunkSize);
            string strategy = GetString("--strategy", "direct");
            byte[] oldBytes = Serialize(CreateSave(7, itemCount, 1));
            byte[] newBytes = Serialize(CreateSave(19, itemCount, 2));
            byte[] writeBytes = strategy == "dual" ? SerializeEnvelope(2, newBytes) : newBytes;
            string[] stages = GetStageNames(strategy, writeBytes.Length, chunkSize);
            WriteJson(GetString("--result-path", ""), new CatalogResult
            {
                persistentDataPath = Application.persistentDataPath,
                strategy = strategy,
                itemCount = itemCount,
                chunkSize = chunkSize,
                payloadBytes = newBytes.Length,
                encodedWriteBytes = writeBytes.Length,
                oldSha256 = Sha256(oldBytes),
                newSha256 = Sha256(newBytes),
                stages = stages
            });
            Application.Quit();
        }

        private void RunFault()
        {
            string strategy = GetString("--strategy", "direct");
            string caseDirectory = GetString("--case-dir", "");
            if (string.IsNullOrWhiteSpace(caseDirectory)) throw new ArgumentException("--case-dir is required");
            int itemCount = GetInt("--item-count", 25);
            int chunkSize = GetInt("--chunk-size", DefaultChunkSize);
            int targetCheckpoint = GetInt("--checkpoint-index", -1);
            Directory.CreateDirectory(caseDirectory);
            if (Directory.EnumerateFileSystemEntries(caseDirectory).Any())
                throw new IOException("A fault case directory must be new and empty");
            string saveDirectory = Path.Combine(caseDirectory, "saves");
            Directory.CreateDirectory(saveDirectory);

            byte[] oldBytes = Serialize(CreateSave(7, itemCount, 1));
            byte[] newBytes = Serialize(CreateSave(19, itemCount, 2));
            string oldHash = Sha256(oldBytes);
            string newHash = Sha256(newBytes);
            SeedBaseline(strategy, saveDirectory, oldBytes);
            WriteJson(Path.Combine(caseDirectory, "expected.json"), new ExpectedResult
            {
                oldSha256 = oldHash,
                newSha256 = newHash,
                payloadBytes = newBytes.Length,
                chunkSize = chunkSize
            });

            var checkpoints = new CheckpointController(caseDirectory, targetCheckpoint);
            switch (strategy)
            {
                case "direct": SaveDirect(saveDirectory, newBytes, chunkSize, checkpoints); break;
                case "replace": SaveReplace(saveDirectory, newBytes, chunkSize, checkpoints); break;
                case "dual": SaveDual(saveDirectory, newBytes, chunkSize, checkpoints); break;
                default: throw new ArgumentException("Unknown strategy: " + strategy);
            }
            WriteJson(Path.Combine(caseDirectory, "writer-complete.json"), new CompletionResult { complete = true });
            Application.Quit();
        }

        private void RunRecovery()
        {
            string caseDirectory = GetString("--case-dir", "");
            string resultPath = GetString("--result-path", "");
            string strategy = GetString("--strategy", "direct");
            ExpectedResult expected = ReadJson<ExpectedResult>(Path.Combine(caseDirectory, "expected.json"));
            var watch = Stopwatch.StartNew();
            string saveDirectory = Path.Combine(caseDirectory, "saves");
            string outcome = Recover(strategy, saveDirectory, expected.oldSha256, expected.newSha256);
            watch.Stop();
            long bytes = Directory.Exists(saveDirectory)
                ? Directory.EnumerateFiles(saveDirectory).Sum(path => new FileInfo(path).Length)
                : 0;
            WriteJson(resultPath, new RecoveryResult
            {
                outcome = outcome,
                recoveryMilliseconds = watch.Elapsed.TotalMilliseconds,
                directoryBytes = bytes
            });
            Application.Quit();
        }

        private void RunTiming()
        {
            int itemCount = GetInt("--item-count", 25);
            int chunkSize = GetInt("--chunk-size", DefaultChunkSize);
            int iterations = GetInt("--iterations", 100);
            string root = GetString("--case-root", Path.Combine(Application.persistentDataPath, "SaveStudyUnity", "timing"));
            string resultPath = GetString("--result-path", "");
            byte[] oldBytes = Serialize(CreateSave(7, itemCount, 1));
            byte[] newBytes = Serialize(CreateSave(19, itemCount, 2));
            string[] strategies = { "direct", "replace", "dual" };
            var rows = new List<TimingResult>();
            var order = new List<(string strategy, int iteration)>();
            foreach (string strategy in strategies)
            {
                for (int warmup = 1; warmup <= 10; warmup++)
                {
                    string warmupDirectory = Path.Combine(root, "warmup", itemCount.ToString(), strategy, warmup.ToString("D2"));
                    Directory.CreateDirectory(warmupDirectory);
                    SeedBaseline(strategy, warmupDirectory, oldBytes);
                    switch (strategy)
                    {
                        case "direct": SaveDirect(warmupDirectory, newBytes, chunkSize, null); break;
                        case "replace": SaveReplace(warmupDirectory, newBytes, chunkSize, null); break;
                        case "dual": SaveDual(warmupDirectory, newBytes, chunkSize, null); break;
                    }
                }
            }
            for (int iteration = 1; iteration <= iterations; iteration++)
                foreach (string strategy in strategies) order.Add((strategy, iteration));
            Shuffle(order, 4100 + itemCount);

            int orderPosition = 0;
            foreach (var pair in order)
            {
                orderPosition++;
                string directory = Path.Combine(root, itemCount.ToString(), pair.strategy, pair.iteration.ToString("D4"));
                Directory.CreateDirectory(directory);
                SeedBaseline(pair.strategy, directory, oldBytes);
                byte[] encoded = pair.strategy == "dual" ? SerializeEnvelope(2, newBytes) : newBytes;
                var watch = Stopwatch.StartNew();
                switch (pair.strategy)
                {
                    case "direct": SaveDirect(directory, newBytes, chunkSize, null); break;
                    case "replace": SaveReplace(directory, newBytes, chunkSize, null); break;
                    case "dual": SaveDual(directory, newBytes, chunkSize, null); break;
                }
                watch.Stop();
                rows.Add(new TimingResult
                {
                    strategy = pair.strategy,
                    itemCount = itemCount,
                    iteration = pair.iteration,
                    orderPosition = orderPosition,
                    saveMilliseconds = watch.Elapsed.TotalMilliseconds,
                    directoryBytes = Directory.EnumerateFiles(directory).Sum(p => new FileInfo(p).Length),
                    payloadBytes = newBytes.Length,
                    encodedWriteBytes = encoded.Length
                });
            }
            WriteJsonArray(resultPath, rows);
            Application.Quit();
        }

        private static void SeedBaseline(string strategy, string directory, byte[] oldBytes)
        {
            if (strategy == "dual")
                File.WriteAllBytes(Path.Combine(directory, "slot0.json"), SerializeEnvelope(1, oldBytes));
            else
                File.WriteAllBytes(Path.Combine(directory, "save.json"), oldBytes);
        }

        private static void SaveDirect(string directory, byte[] bytes, int chunkSize, CheckpointController checkpoints)
        {
            checkpoints?.Hit("before-truncate");
            using (var stream = new FileStream(Path.Combine(directory, "save.json"), FileMode.Create, FileAccess.Write, FileShare.None))
            {
                checkpoints?.Hit("after-truncate");
                WriteChunks(stream, bytes, chunkSize, checkpoints);
            }
            checkpoints?.Hit("after-close");
        }

        private static void SaveReplace(string directory, byte[] bytes, int chunkSize, CheckpointController checkpoints)
        {
            checkpoints?.Hit("before-temp");
            string temporary = Path.Combine(directory, "save.tmp");
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                WriteChunks(stream, bytes, chunkSize, checkpoints);
            checkpoints?.Hit("after-temp-close");
            checkpoints?.Hit("before-replace");
            File.Replace(temporary, Path.Combine(directory, "save.json"), null);
            checkpoints?.Hit("after-replace");
        }

        private static void SaveDual(string directory, byte[] payload, int chunkSize, CheckpointController checkpoints)
        {
            byte[] envelope = SerializeEnvelope(2, payload);
            string slot = Path.Combine(directory, "slot1.json");
            checkpoints?.Hit("before-slot-write");
            using (var stream = new FileStream(slot, FileMode.Create, FileAccess.Write, FileShare.None))
                WriteChunks(stream, envelope, chunkSize, checkpoints);
            checkpoints?.Hit("after-slot-close");
        }

        private static void WriteChunks(Stream stream, byte[] bytes, int chunkSize, CheckpointController checkpoints)
        {
            int chunk = 0;
            for (int offset = 0; offset < bytes.Length; offset += chunkSize)
            {
                int count = Math.Min(chunkSize, bytes.Length - offset);
                stream.Write(bytes, offset, count);
                stream.Flush();
                checkpoints?.Hit("after-chunk-" + chunk);
                chunk++;
            }
        }

        private static string[] GetStageNames(string strategy, int encodedBytes, int chunkSize)
        {
            var names = new List<string>();
            int chunks = (encodedBytes + chunkSize - 1) / chunkSize;
            if (strategy == "direct")
            {
                names.Add("before-truncate");
                names.Add("after-truncate");
                for (int i = 0; i < chunks; i++) names.Add("after-chunk-" + i);
                names.Add("after-close");
            }
            else if (strategy == "replace")
            {
                names.Add("before-temp");
                for (int i = 0; i < chunks; i++) names.Add("after-chunk-" + i);
                names.Add("after-temp-close");
                names.Add("before-replace");
                names.Add("after-replace");
            }
            else if (strategy == "dual")
            {
                names.Add("before-slot-write");
                for (int i = 0; i < chunks; i++) names.Add("after-chunk-" + i);
                names.Add("after-slot-close");
            }
            else throw new ArgumentException("Unknown strategy: " + strategy);
            return names.ToArray();
        }

        private static string Recover(string strategy, string directory, string oldHash, string newHash)
        {
            if (strategy != "dual")
            {
                string path = Path.Combine(directory, "save.json");
                if (!File.Exists(path)) return "no-valid-save";
                byte[] bytes;
                try { bytes = File.ReadAllBytes(path); }
                catch { return "recovery-error"; }
                GameSave state = TryParseSave(bytes);
                if (state == null) return "no-valid-save";
                string hash = Sha256(bytes);
                return hash == oldHash ? "old-valid" : hash == newHash ? "new-valid" : "unexpected-state";
            }

            var valid = new List<Tuple<int, string>>();
            foreach (string path in Directory.GetFiles(directory, "slot*.json"))
            {
                try
                {
                    Envelope envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(path, Encoding.UTF8));
                    if (envelope == null || string.IsNullOrEmpty(envelope.payloadJson)) continue;
                    byte[] payload = Encoding.UTF8.GetBytes(envelope.payloadJson);
                    if (Sha256(payload) != envelope.sha256 || TryParseSave(payload) == null) continue;
                    valid.Add(Tuple.Create(envelope.generation, Sha256(payload)));
                }
                catch { }
            }
            if (valid.Count == 0) return "no-valid-save";
            Tuple<int, string> selected = valid.OrderByDescending(x => x.Item1).First();
            return selected.Item2 == oldHash ? "old-valid" : selected.Item2 == newHash ? "new-valid" : "unexpected-state";
        }

        private static GameSave TryParseSave(byte[] bytes)
        {
            try
            {
                GameSave state = JsonUtility.FromJson<GameSave>(Encoding.UTF8.GetString(bytes));
                return state != null && state.inventory != null && state.quests != null && state.discoveredLocations != null ? state : null;
            }
            catch { return null; }
        }

        private static GameSave CreateSave(int seed, int itemCount, int generation)
        {
            var random = new System.Random(seed);
            var inventory = new InventoryItem[itemCount];
            var locations = new DiscoveredLocation[itemCount];
            int questCount = Math.Max(1, itemCount / 5);
            var quests = new QuestState[questCount];
            for (int i = 0; i < itemCount; i++)
            {
                inventory[i] = new InventoryItem { itemId = "item-" + i.ToString("D6"), quantity = random.Next(1, 100), durability = random.Next(0, 1000) };
                locations[i] = new DiscoveredLocation { id = i, x = random.Next(-5000, 5000), y = random.Next(-5000, 5000), hasBeenVisited = random.Next(0, 3) == 0 };
            }
            for (int i = 0; i < questCount; i++)
                quests[i] = new QuestState { questId = "quest-" + i.ToString("D5"), status = random.Next(0, 4), progress = random.Next(0, 10) };
            return new GameSave
            {
                generation = generation,
                playerLevel = random.Next(1, 99),
                health = random.Next(10, 100),
                coins = random.Next(0, 250),
                inventory = inventory,
                quests = quests,
                discoveredLocations = locations
            };
        }

        private static byte[] Serialize(GameSave save) => Encoding.UTF8.GetBytes(JsonUtility.ToJson(save));

        private static byte[] SerializeEnvelope(int generation, byte[] payload)
        {
            var envelope = new Envelope
            {
                generation = generation,
                payloadJson = Encoding.UTF8.GetString(payload),
                sha256 = Sha256(payload)
            };
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(envelope));
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static void Shuffle<T>(IList<T> list, int seed)
        {
            var random = new System.Random(seed);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T value = list[i]; list[i] = list[j]; list[j] = value;
            }
        }

        private static void WriteJson(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("--result-path is required");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, JsonUtility.ToJson(value), new UTF8Encoding(false));
        }

        private static T ReadJson<T>(string path)
        {
            return JsonUtility.FromJson<T>(File.ReadAllText(path, Encoding.UTF8));
        }

        private static void WriteJsonArray(string path, IEnumerable<TimingResult> values)
        {
            WriteJson(path, new TimingArray { rows = values.ToArray() });
        }

        private void WriteError(Exception ex)
        {
            UnityEngine.Debug.LogException(ex);
            string path = GetString("--result-path", "");
            if (!string.IsNullOrEmpty(path))
            {
                try { WriteJson(path, new ErrorResult { error = ex.ToString() }); }
                catch { }
            }
        }

        private int GetInt(string name, int fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : fallback;
        }

        private string GetString(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        [Serializable] private sealed class InventoryItem { public string itemId; public int quantity; public int durability; }
        [Serializable] private sealed class QuestState { public string questId; public int status; public int progress; }
        [Serializable] private sealed class DiscoveredLocation { public int id; public int x; public int y; public bool hasBeenVisited; }
        [Serializable] private sealed class GameSave { public int generation; public int playerLevel; public int health; public int coins; public InventoryItem[] inventory; public QuestState[] quests; public DiscoveredLocation[] discoveredLocations; }
        [Serializable] private sealed class Envelope { public int generation; public string payloadJson; public string sha256; }
        [Serializable] private sealed class ExpectedResult { public string oldSha256; public string newSha256; public int payloadBytes; public int chunkSize; }
        [Serializable] private sealed class CompletionResult { public bool complete; }
        [Serializable] private sealed class RecoveryResult { public string outcome; public double recoveryMilliseconds; public long directoryBytes; }
        [Serializable] private sealed class CatalogResult { public string persistentDataPath; public string strategy; public int itemCount; public int chunkSize; public int payloadBytes; public int encodedWriteBytes; public string oldSha256; public string newSha256; public string[] stages; }
        [Serializable] private sealed class TimingResult { public string strategy; public int itemCount; public int iteration; public int orderPosition; public double saveMilliseconds; public long directoryBytes; public int payloadBytes; public int encodedWriteBytes; }
        [Serializable] private sealed class TimingArray { public TimingResult[] rows; }
        [Serializable] private sealed class ErrorResult { public string error; }

        private sealed class CheckpointController
        {
            private readonly string caseDirectory;
            private readonly int target;
            private int index;
            public CheckpointController(string directory, int targetIndex) { caseDirectory = directory; target = targetIndex; }
            public void Hit(string name)
            {
                int current = index++;
                if (current != target) return;
                File.WriteAllText(Path.Combine(caseDirectory, "checkpoint.ready"), current + "," + name, new UTF8Encoding(false));
                while (true) Thread.Sleep(25);
            }
        }
    }
}
