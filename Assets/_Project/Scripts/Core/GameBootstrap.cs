using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Creates core services before any scene loads, so every scene (including sandboxes opened
    /// directly in the editor) has them. Gameplay assemblies register their own services on top.
    /// </summary>
    public static class GameBootstrap
    {
        public static bool IsInitialized { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => IsInitialized = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (IsInitialized) return;
            IsInitialized = true;

            var settings = new PlayerPrefsSettingsService();
            ServiceRegistry.Register<ISettingsService>(settings);
            ApplyFrameRate(settings.Current);
            ApplyDisplay(settings.Current);
            settings.Changed += ApplyFrameRate;
            settings.Changed += ApplyDisplay;

            GameLog.Verbose("Bootstrap", "Core services initialized");
        }

        private static void ApplyDisplay(GameSettings s)
        {
            if (s.QualityLevel >= 0 && s.QualityLevel < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != s.QualityLevel)
                QualitySettings.SetQualityLevel(s.QualityLevel, true);
            if (Application.isEditor) return;
            FullScreenMode mode = s.Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            if (Screen.fullScreenMode != mode) Screen.fullScreenMode = mode;
        }

        private static void ApplyFrameRate(GameSettings s)
        {
            QualitySettings.vSyncCount = s.VSync ? 1 : 0;
            Application.targetFrameRate = !s.VSync && s.FrameRateCap > 0 ? s.FrameRateCap : -1;
        }
    }
}
