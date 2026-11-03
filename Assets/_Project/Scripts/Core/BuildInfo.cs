using System;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// What kind of build this is (M13). Developer tools (enemy spawn keys, god mode, the dev readout, the N run
    /// key) are on in the editor and in development builds, and off in release builds unless the game is started
    /// with <c>-devtools</c>.
    /// </summary>
    public static class BuildInfo
    {
        public const string GameName = "Deadfall";

        private static int _devTools = -1;

        /// <summary>The player version (Project Settings › Player › Version), stamped by the build script.</summary>
        public static string Version => Application.version;

        public static string Label => $"{GameName} v{Version}{(Debug.isDebugBuild ? " dev" : string.Empty)}";

        public static bool DevTools
        {
            get
            {
                if (_devTools < 0)
                {
                    bool flag = false;
                    try { flag = Array.IndexOf(Environment.GetCommandLineArgs(), "-devtools") >= 0; }
                    catch (Exception) { /* some platforms have no command line */ }
                    _devTools = Application.isEditor || Debug.isDebugBuild || flag ? 1 : 0;
                }
                return _devTools == 1;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => _devTools = -1;
    }
}
