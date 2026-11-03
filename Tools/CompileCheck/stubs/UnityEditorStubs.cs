// Minimal UnityEditor surface used by Deadfall.Editor, so the compile check can build editor scripts.
// Signatures follow Unity 6; only what the project calls is here.
using System;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
        public MenuItem(string itemName, bool isValidateFunction) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
    }

    public enum BuildTarget { StandaloneOSX = 2, StandaloneWindows = 5, StandaloneWindows64 = 19, StandaloneLinux64 = 24 }
    public enum BuildTargetGroup { Unknown = 0, Standalone = 1 }

    [Flags]
    public enum BuildOptions { None = 0, Development = 1, AllowDebugging = 512, CompressWithLz4 = 0x40000, CompressWithLz4HC = 0x80000, StrictMode = 0x200000, DetailedBuildReport = 0x8000000 }

    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public BuildTarget target { get; set; }
        public BuildTargetGroup targetGroup { get; set; }
        public BuildOptions options { get; set; }
        public string[] extraScriptingDefines { get; set; }
    }

    public sealed class EditorBuildSettingsScene
    {
        public bool enabled { get; set; }
        public string path { get; set; }
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
    }

    public static class EditorUserBuildSettings
    {
        public static BuildTarget activeBuildTarget => BuildTarget.StandaloneWindows64;
    }

    public static class PlayerSettings
    {
        public static string bundleVersion { get; set; }
        public static string productName { get; set; }
        public static string companyName { get; set; }
    }

    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => null;
        public static BuildTargetGroup GetBuildTargetGroup(BuildTarget target) => BuildTargetGroup.Standalone;
    }

    public static class EditorApplication
    {
        public static void Exit(int returnValue) { }
    }

    public static class EditorUtility
    {
        public static void RevealInFinder(string path) { }
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown = 0, Succeeded = 1, Failed = 2, Cancelled = 3 }

    public struct BuildSummary
    {
        public BuildResult result => BuildResult.Unknown;
        public ulong totalSize => 0;
        public TimeSpan totalTime => TimeSpan.Zero;
        public int totalErrors => 0;
        public int totalWarnings => 0;
        public string outputPath => null;
    }

    public sealed class BuildReport
    {
        public BuildSummary summary => default;
    }
}
