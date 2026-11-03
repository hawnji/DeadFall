using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Deadfall.EditorTools
{
    /// <summary>
    /// Player builds (M13), from the menu (Deadfall › Build) or the command line:
    /// <code>
    /// Unity -batchmode -quit -projectPath . -buildTarget Win64 \
    ///   -executeMethod Deadfall.EditorTools.BuildScript.BuildFromCommandLine \
    ///   -deadfallTarget windows -deadfallVersion 0.13.0 [-deadfallDev] [-deadfallOut Builds/Windows]
    /// </code>
    /// Builds every enabled scene in Build Settings into <c>Builds/&lt;Target&gt;[-Dev]/</c>, stamps the version,
    /// and writes <c>build_info.txt</c> next to the player. Release builds have developer tools off (BuildInfo).
    /// </summary>
    public static class BuildScript
    {
        public const string DefaultVersion = "0.13.0";

        [MenuItem("Deadfall/Build/Windows (Release)", false, 10)]
        public static void WindowsRelease() => Build(BuildTarget.StandaloneWindows64, false, null, null);

        [MenuItem("Deadfall/Build/Windows (Development)", false, 11)]
        public static void WindowsDev() => Build(BuildTarget.StandaloneWindows64, true, null, null);

        [MenuItem("Deadfall/Build/Linux (Release)", false, 30)]
        public static void LinuxRelease() => Build(BuildTarget.StandaloneLinux64, false, null, null);

        [MenuItem("Deadfall/Build/macOS (Release)", false, 31)]
        public static void MacRelease() => Build(BuildTarget.StandaloneOSX, false, null, null);

        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            BuildTarget target = ParseTarget(Arg(args, "-deadfallTarget") ?? "windows");
            bool dev = Array.IndexOf(args, "-deadfallDev") >= 0;
            BuildReport report = Build(target, dev, Arg(args, "-deadfallVersion"), Arg(args, "-deadfallOut"));
            bool ok = report != null && report.summary.result == BuildResult.Succeeded;
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static BuildReport Build(BuildTarget target, bool development, string version, string outDir)
        {
            string[] scenes = EnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError("[Build] No enabled scenes in Build Settings.");
                return null;
            }
            if (!string.IsNullOrEmpty(version)) PlayerSettings.bundleVersion = version;
            else if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "1.0") PlayerSettings.bundleVersion = DefaultVersion;

            string folder = outDir ?? Path.Combine("Builds", TargetFolder(target) + (development ? "-Dev" : string.Empty));
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, ExecutableName(target));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exe,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.StrictMode
                    : BuildOptions.CompressWithLz4HC | BuildOptions.StrictMode,
            };
            Debug.Log($"[Build] {target} {(development ? "development" : "release")} v{PlayerSettings.bundleVersion} -> {exe}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report == null) return null;
            BuildSummary s = report.summary;
            Debug.Log($"[Build] {s.result}: {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalSize / (1024 * 1024)} MB in {s.totalTime.TotalSeconds:0} s");
            if (s.result == BuildResult.Succeeded)
            {
                File.WriteAllText(Path.Combine(folder, "build_info.txt"),
                    $"Deadfall {PlayerSettings.bundleVersion}{(development ? " (development)" : string.Empty)}\n" +
                    $"Target: {target}\nBuilt: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\nUnity: {Application.unityVersion}\n" +
                    "Run with -devtools to enable developer keys in a release build.\n");
            }
            return report;
        }

        private static string[] EnabledScenes()
        {
            var list = new List<string>();
            foreach (EditorBuildSettingsScene sc in EditorBuildSettings.scenes)
                if (sc != null && sc.enabled && !string.IsNullOrEmpty(sc.path)) list.Add(sc.path);
            return list.ToArray();
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static BuildTarget ParseTarget(string s) => s.ToLowerInvariant() switch
        {
            "linux" => BuildTarget.StandaloneLinux64,
            "mac" or "macos" or "osx" => BuildTarget.StandaloneOSX,
            _ => BuildTarget.StandaloneWindows64,
        };

        private static string TargetFolder(BuildTarget t) => t switch
        {
            BuildTarget.StandaloneLinux64 => "Linux",
            BuildTarget.StandaloneOSX => "macOS",
            _ => "Windows",
        };

        private static string ExecutableName(BuildTarget t) => t switch
        {
            BuildTarget.StandaloneLinux64 => "Deadfall.x86_64",
            BuildTarget.StandaloneOSX => "Deadfall.app",
            _ => "Deadfall.exe",
        };
    }
}
