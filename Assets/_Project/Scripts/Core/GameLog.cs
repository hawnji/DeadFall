using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Deadfall.Core
{
    /// <summary>Category-tagged logging. Verbose logs compile out of release builds.</summary>
    public static class GameLog
    {
        public static void Info(string category, string message) => Debug.Log($"[{category}] {message}");
        public static void Warn(string category, string message) => Debug.LogWarning($"[{category}] {message}");
        public static void Error(string category, string message) => Debug.LogError($"[{category}] {message}");

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Verbose(string category, string message) => Debug.Log($"[{category}] {message}");
    }
}
