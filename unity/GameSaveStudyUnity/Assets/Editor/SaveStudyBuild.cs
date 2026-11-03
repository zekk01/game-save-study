using System.IO;
using UnityEditor;

namespace SaveStudyUnity.Editor
{
    public static class SaveStudyBuild
    {
        public static void BuildWindowsPlayer()
        {
            string project = Directory.GetCurrentDirectory();
            string output = Path.GetFullPath(Path.Combine(project, "build", "GameSaveStudyUnity.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.Exception("Unity Player build failed: " + report.summary.result);
            UnityEngine.Debug.Log("GameSaveStudyUnity build succeeded: " + output);
        }
    }
}
