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
            settings.Changed += ApplyFrameRate;

            GameLog.Verbose("Bootstrap", "Core services initialized");
        }

        private static void ApplyFrameRate(GameSettings s)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = s.FrameRateCap > 0 ? s.FrameRateCap : -1;
        }
    }
}
