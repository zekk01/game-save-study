using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace SaveStudy;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const int DefaultChunkSize = 4096;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            return args[0] switch
            {
                "run" => Run(args[1..]),
                "worker" => Worker(args[1..]),
                _ => throw new ArgumentException($"Unknown command: {args[0]}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void PrintHelp() => Console.WriteLine("""
        SaveStudy: controlled process-interruption experiments for game-like save data.

        Usage:
          SaveStudy run [--fault-trials N] [--timing-iterations N] [--item-sizes N,N,...]
                        [--chunk-size N] [--output-dir PATH]

        The run command executes protocol v1's controlled process-interruption matrix
        and writes per-trial CSV output. Use a new output directory for each run.
        """);

    private static int Run(string[] args)
    {
        int trials = GetIntOption(args, "--fault-trials", 20);
        int timingIterations = GetIntOption(args, "--timing-iterations", 100);
        int[] itemSizes = GetIntListOption(args, "--item-sizes", [25, 250, 1000]);
        int chunkSize = GetIntOption(args, "--chunk-size", DefaultChunkSize);
        string outputDirectory = GetStringOption(args, "--output-dir", Path.GetFullPath("results"));
        if (trials < 1 || timingIterations < 1 || itemSizes.Length == 0 || itemSizes.Any(n => n < 1) || chunkSize < 64)
            throw new ArgumentOutOfRangeException(nameof(args), "Trial counts and item sizes must be positive; chunk size must be at least 64 bytes.");

        string[] strategies = ["direct", "replace", "dual"];
        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);
        string faultPath = Path.Combine(fullOutputDirectory, "fault-trials.csv");
        string timingPath = Path.Combine(fullOutputDirectory, "save-timings.csv");
        using var faultWriter = new StreamWriter(faultPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using var timingWriter = new StreamWriter(timingPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        faultWriter.WriteLine("strategy,item_count,stage,trial,outcome,recovery_ms,save_directory_bytes,payload_bytes,chunk_size,old_sha256,new_sha256");
        timingWriter.WriteLine("strategy,item_count,iteration,order_position,save_ms,save_directory_bytes,payload_bytes,chunk_size");

        // Keep the measured save files on the same volume as the result directory;
        // system temp may silently be on a different drive/filesystem.
        string root = Path.Combine(fullOutputDirectory, ".work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (int itemCount in itemSizes)
            {
                GameSave oldSave = CreateSave(seed: 7, itemCount: itemCount, generation: 1);
                GameSave newSave = CreateSave(seed: 19, itemCount: itemCount, generation: 2);
                byte[] oldBytes = JsonSerializer.SerializeToUtf8Bytes(oldSave, JsonOptions);
                byte[] newBytes = JsonSerializer.SerializeToUtf8Bytes(newSave, JsonOptions);
                string oldHash = Sha256(oldBytes);
                string newHash = Sha256(newBytes);
                string oldPayloadPath = Path.Combine(root, $"old-{itemCount}.json");
                string newPayloadPath = Path.Combine(root, $"new-{itemCount}.json");
                File.WriteAllBytes(oldPayloadPath, oldBytes);
                File.WriteAllBytes(newPayloadPath, newBytes);

                foreach (string strategy in strategies)
                {
                    int writtenBytes = strategy == "dual"
                        ? MakeEnvelopeBytes(2, newBytes).Length
                        : newBytes.Length;
                    string[] stages = GetStages(strategy, writtenBytes, chunkSize);
                    foreach (string stage in stages)
                    {
                        for (int trial = 1; trial <= trials; trial++)
                        {
                            string caseRoot = Path.Combine(root, $"{itemCount}-{strategy}-{Sanitize(stage)}-{trial}");
                            string saveDirectory = Path.Combine(caseRoot, "saves");
                            Directory.CreateDirectory(saveDirectory);
                            SeedBaseline(strategy, saveDirectory, oldBytes);

                            RunWorkerUntilCheckpoint(strategy, saveDirectory, newPayloadPath, stage, chunkSize);
                            var stopwatch = Stopwatch.StartNew();
                            string outcome = RecoverSafely(strategy, saveDirectory, oldHash, newHash);
                            stopwatch.Stop();
                            long directoryBytes = Directory.EnumerateFiles(saveDirectory).Sum(path => new FileInfo(path).Length);

                            faultWriter.WriteLine(string.Join(',', strategy, itemCount, stage, trial, outcome, stopwatch.Elapsed.TotalMilliseconds.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), directoryBytes, newBytes.Length, chunkSize, oldHash, newHash));
                            faultWriter.Flush();
                            Console.WriteLine($"fault {itemCount,5} {strategy,-7} {stage,-32} trial {trial}/{trials}: {outcome}");
                        }
                    }
                }

                RunTimingTrials(strategies, oldBytes, newBytes, itemCount, timingIterations, chunkSize, root, timingWriter);
            }

            var metadata = new
            {
                protocol = "protocol-v1-draft",
                createdUtc = DateTimeOffset.UtcNow,
                os = RuntimeInformation.OSDescription,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                framework = RuntimeInformation.FrameworkDescription,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                processorCount = Environment.ProcessorCount,
                processorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
                volumeRoot = Path.GetPathRoot(fullOutputDirectory),
                fileSystem = new DriveInfo(Path.GetPathRoot(fullOutputDirectory)!).DriveFormat,
                chunkSizeBytes = chunkSize,
                faultTrialsPerStage = trials,
                timingIterationsPerStrategy = timingIterations,
                itemCounts = itemSizes,
                oldAndNewSeeds = new[] { 7, 19 },
                timerHighResolution = Stopwatch.IsHighResolution,
                build =
#if DEBUG
                    "Debug"
#else
                    "Release"
#endif
            };
            File.WriteAllText(Path.Combine(fullOutputDirectory, "run-metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* preserve the CSV if cleanup fails */ }
        }

        Console.WriteLine($"Wrote {faultPath}");
        Console.WriteLine($"Wrote {timingPath}");
        Console.WriteLine($"Wrote {Path.Combine(fullOutputDirectory, "run-metadata.json")}");
        return 0;
    }

    private static int Worker(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("worker requires: STRATEGY SAVE_DIRECTORY NEW_PAYLOAD_PATH CHUNK_SIZE");
        string strategy = args[0];
        string saveDirectory = args[1];
        string payloadPath = args[2];
        int chunkSize = int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
        // args[4] is reserved for protocol versioning so old result runners cannot
        // silently invoke a changed worker implementation.
        if (args[4] != "protocol-v1") throw new ArgumentException("Unsupported worker protocol.");
        byte[] payload = File.ReadAllBytes(payloadPath);

        switch (strategy)
        {
            case "direct":
            {
                string path = Path.Combine(saveDirectory, "save.json");
                Checkpoint("before-truncate");
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize, FileOptions.None))
                {
                    Checkpoint("after-truncate");
                    WriteChunks(stream, payload, chunkSize);
                }
                Checkpoint("after-close");
                break;
            }
            case "replace":
            {
                string target = Path.Combine(saveDirectory, "save.json");
                string temp = Path.Combine(saveDirectory, "save.json.tmp");
                Checkpoint("before-temp-create");
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize, FileOptions.None))
                {
                    Checkpoint("after-temp-create");
                    WriteChunks(stream, payload, chunkSize);
                }
                Checkpoint("after-temp-close");
                Checkpoint("before-replace");
                File.Replace(temp, target, destinationBackupFileName: null);
                Checkpoint("after-replace");
                break;
            }
            case "dual":
            {
                string slot0 = Path.Combine(saveDirectory, "slot0.json");
                string slot1 = Path.Combine(saveDirectory, "slot1.json");
                int newGeneration = 2;
                byte[] envelope = JsonSerializer.SerializeToUtf8Bytes(new SaveEnvelope(newGeneration, Encoding.UTF8.GetString(payload), Sha256(payload)), JsonOptions);
                Checkpoint("before-inactive-slot-create");
                using (var stream = new FileStream(slot1, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize, FileOptions.None))
                {
                    Checkpoint("after-inactive-slot-truncate");
                    WriteChunks(stream, envelope, chunkSize);
                }
                Checkpoint("after-inactive-slot-close");
                _ = slot0; // slot0 is retained as the prior valid generation.
                break;
            }
            default:
                throw new ArgumentException($"Unknown strategy: {strategy}");
        }
        return 0;
    }

    private static void WriteChunks(FileStream stream, byte[] bytes, int chunkSize)
    {
        int chunk = 0;
        for (int offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            int count = Math.Min(chunkSize, bytes.Length - offset);
            stream.Write(bytes, offset, count);
            stream.Flush();
            chunk++;
            Checkpoint($"after-chunk-{chunk}");
        }
    }

    private static void Checkpoint(string stage)
    {
        Console.Out.WriteLine("CHECKPOINT|" + stage);
        Console.Out.Flush();
        // Parent acknowledges every checkpoint. When the selected checkpoint is
        // reached it kills this process, so the worker cannot run ahead.
        string? ack = Console.In.ReadLine();
        if (ack != "continue") Environment.FailFast("Fault injection stop at " + stage);
    }

    private static string[] GetStages(string strategy, int payloadLength, int chunkSize)
    {
        int chunks = (payloadLength + chunkSize - 1) / chunkSize;
        string[] chunkStages = Enumerable.Range(1, chunks).Select(i => $"after-chunk-{i}").ToArray();
        return strategy switch
        {
            "direct" => ["before-truncate", "after-truncate", .. chunkStages, "after-close"],
            "replace" => ["before-temp-create", "after-temp-create", .. chunkStages, "after-temp-close", "before-replace", "after-replace"],
            "dual" => ["before-inactive-slot-create", "after-inactive-slot-truncate", .. chunkStages, "after-inactive-slot-close"],
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };
    }

    private static void RunWorkerUntilCheckpoint(string strategy, string saveDirectory, string payloadPath, string targetStage, int chunkSize)
    {
        string processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate current executable.");
        var start = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("worker");
        start.ArgumentList.Add(strategy);
        start.ArgumentList.Add(saveDirectory);
        start.ArgumentList.Add(payloadPath);
        start.ArgumentList.Add(chunkSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("protocol-v1");

        using Process child = Process.Start(start) ?? throw new InvalidOperationException("Could not start fault-injection worker.");
        while (true)
        {
            string? line = child.StandardOutput.ReadLine();
            if (line is null)
            {
                string stderr = child.StandardError.ReadToEnd();
                child.WaitForExit();
                throw new InvalidOperationException($"Worker exited before checkpoint '{targetStage}' (exit {child.ExitCode}). {stderr}");
            }
            if (!line.StartsWith("CHECKPOINT|", StringComparison.Ordinal)) continue;
            string stage = line[11..];
            if (stage == targetStage)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                return;
            }
            child.StandardInput.WriteLine("continue");
            child.StandardInput.Flush();
        }
    }

    private static void SeedBaseline(string strategy, string directory, byte[] oldPayload)
    {
        if (strategy is "direct" or "replace")
            File.WriteAllBytes(Path.Combine(directory, "save.json"), oldPayload);
        else
        {
            byte[] envelope = MakeEnvelopeBytes(1, oldPayload);
            File.WriteAllBytes(Path.Combine(directory, "slot0.json"), envelope);
        }
    }

    private static void RunTimingTrials(string[] strategies, byte[] oldBytes, byte[] payload, int itemCount, int iterations, int chunkSize, string root, StreamWriter writer)
    {
        string timingRoot = Path.Combine(root, $"timing-{itemCount}");
        var directories = strategies.ToDictionary(s => s, s => Path.Combine(timingRoot, s));
        foreach (string strategy in strategies)
        {
            Directory.CreateDirectory(directories[strategy]);
            SeedBaseline(strategy, directories[strategy], oldBytes);
            for (int warmup = 0; warmup < 10; warmup++)
                SaveNormally(strategy, directories[strategy], payload, chunkSize, generation: warmup + 2);
        }

        var random = new Random(20261102 + itemCount);
        for (int iteration = 1; iteration <= iterations; iteration++)
        {
            string[] order = strategies.ToArray();
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            for (int position = 0; position < order.Length; position++)
            {
                string strategy = order[position];
                int generation = 11 + iteration;
                var stopwatch = Stopwatch.StartNew();
                SaveNormally(strategy, directories[strategy], payload, chunkSize, generation);
                stopwatch.Stop();
                long bytes = Directory.EnumerateFiles(directories[strategy]).Sum(path => new FileInfo(path).Length);
                writer.WriteLine(string.Join(',', strategy, itemCount, iteration, position + 1, stopwatch.Elapsed.TotalMilliseconds.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), bytes, payload.Length, chunkSize));
            }
            writer.Flush();
        }
    }

    private static void SaveNormally(string strategy, string directory, byte[] payload, int chunkSize, int generation)
    {
        switch (strategy)
        {
            case "direct":
                WriteFile(Path.Combine(directory, "save.json"), payload, chunkSize);
                break;
            case "replace":
            {
                string target = Path.Combine(directory, "save.json");
                string temp = Path.Combine(directory, "save.json.tmp");
                WriteFile(temp, payload, chunkSize);
                File.Replace(temp, target, destinationBackupFileName: null);
                break;
            }
            case "dual":
            {
                string slotPath = Path.Combine(directory, generation % 2 == 0 ? "slot0.json" : "slot1.json");
                WriteFile(slotPath, MakeEnvelopeBytes(generation, payload), chunkSize);
                break;
            }
            default:
                throw new ArgumentException($"Unknown strategy: {strategy}");
        }
    }

    private static void WriteFile(string path, byte[] bytes, int chunkSize)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize, FileOptions.None);
        for (int offset = 0; offset < bytes.Length; offset += chunkSize)
        {
            stream.Write(bytes, offset, Math.Min(chunkSize, bytes.Length - offset));
            stream.Flush();
        }
    }

    private static byte[] MakeEnvelopeBytes(int generation, byte[] payload) =>
        JsonSerializer.SerializeToUtf8Bytes(new SaveEnvelope(generation, Encoding.UTF8.GetString(payload), Sha256(payload)), JsonOptions);

    private static string RecoverSafely(string strategy, string directory, string oldHash, string newHash)
    {
        try { return Recover(strategy, directory, oldHash, newHash); }
        catch { return "recovery-error"; }
    }

    private static string Recover(string strategy, string directory, string oldHash, string newHash)
    {
        if (strategy is "direct" or "replace")
        {
            string path = Path.Combine(directory, "save.json");
            if (!File.Exists(path)) return "no-valid-save";
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                _ = JsonSerializer.Deserialize<GameSave>(bytes, JsonOptions) ?? throw new InvalidDataException("Null save");
                string hash = Sha256(bytes);
                return hash == oldHash ? "old-valid" : hash == newHash ? "new-valid" : "unexpected-state";
            }
            catch (JsonException) { return "no-valid-save"; }
            catch (IOException) { return "no-valid-save"; }
        }

        var valid = new List<(int Generation, string Hash)>();
        foreach (string path in Directory.EnumerateFiles(directory, "slot*.json"))
        {
            try
            {
                SaveEnvelope envelope = JsonSerializer.Deserialize<SaveEnvelope>(File.ReadAllBytes(path), JsonOptions) ?? throw new InvalidDataException("Null envelope");
                byte[] payload = Encoding.UTF8.GetBytes(envelope.PayloadJson);
                if (Sha256(payload) != envelope.Sha256) continue;
                _ = JsonSerializer.Deserialize<GameSave>(payload, JsonOptions) ?? throw new InvalidDataException("Null payload");
                valid.Add((envelope.Generation, Sha256(payload)));
            }
            catch { /* invalid slot is ignored; recovery attempts the other slot */ }
        }
        if (valid.Count == 0) return "no-valid-save";
        var selected = valid.OrderByDescending(x => x.Generation).First();
        return selected.Hash == oldHash ? "old-valid" : selected.Hash == newHash ? "new-valid" : "unexpected-state";
    }

    private static GameSave CreateSave(int seed, int itemCount, int generation)
    {
        var random = new Random(seed);
        var inventory = Enumerable.Range(0, itemCount).Select(i => new InventoryItem($"item-{i:D6}", random.Next(1, 100), random.Next(0, 1000))).ToArray();
        var quests = Enumerable.Range(0, Math.Max(1, itemCount / 5)).Select(i => new QuestState($"quest-{i:D5}", random.Next(0, 4), random.Next(0, 10))).ToArray();
        var locations = Enumerable.Range(0, itemCount).Select(i => new DiscoveredLocation(i, random.Next(-5000, 5000), random.Next(-5000, 5000), random.Next(0, 3) == 0)).ToArray();
        return new GameSave(generation, random.Next(1, 99), random.Next(10, 100), random.Next(0, 250), inventory, quests, locations);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Sanitize(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'));

    private static int GetIntOption(string[] args, string name, int fallback)
    {
        int index = Array.IndexOf(args, name);
        return index < 0 ? fallback : int.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetStringOption(string[] args, string name, string fallback)
    {
        int index = Array.IndexOf(args, name);
        return index < 0 ? fallback : args[index + 1];
    }

    private static int[] GetIntListOption(string[] args, string name, int[] fallback)
    {
        int index = Array.IndexOf(args, name);
        return index < 0 ? fallback : args[index + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    }

    private sealed record GameSave(int Generation, int PlayerLevel, int Health, int Coins, InventoryItem[] Inventory, QuestState[] Quests, DiscoveredLocation[] DiscoveredLocations);
    private sealed record InventoryItem(string ItemId, int Quantity, int Durability);
    private sealed record QuestState(string QuestId, int Status, int Progress);
    private sealed record DiscoveredLocation(int Id, int X, int Y, bool HasBeenVisited);
    private sealed record SaveEnvelope(int Generation, string PayloadJson, string Sha256);
}
