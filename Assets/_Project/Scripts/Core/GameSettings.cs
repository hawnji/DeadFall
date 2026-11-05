using System;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>Player-facing options. Serialized with JsonUtility into PlayerPrefs until the save system lands (M10).</summary>
    [Serializable]
    public sealed class GameSettings
    {
        public const int CurrentVersion = 4;

        public int Version = CurrentVersion;
        /// <summary>Degrees of rotation per mouse count at sensitivity 1.</summary>
        public float MouseSensitivity = 1f;
        public float AimSensitivityMultiplier = 0.8f;
        public bool InvertY;
        public float FieldOfView = 90f;
        public bool HeadBob;
        /// <summary>Sync to the monitor's refresh rate. When on, <see cref="FrameRateCap"/> is ignored.</summary>
        public bool VSync = true;
        /// <summary>0 = unlimited. Used only with VSync off.</summary>
        public int FrameRateCap;
        public float MasterVolume = 1f;
        /// <summary>Gunfire, enemies, explosions (M13).</summary>
        public float EffectsVolume = 1f;
        /// <summary>Music (title theme, level tracks) and any looping background bed. Shown as "Music" in Settings.</summary>
        public float AmbienceVolume = DefaultMusicVolume;
        public const float DefaultMusicVolume = 0.1f;
        /// <summary>The v2 default, moved to <see cref="DefaultMusicVolume"/> when an untouched v2 file loads.</summary>
        private const float OldDefaultMusicVolume = 0.7f;
        /// <summary>The v3 default, moved to <see cref="DefaultMusicVolume"/> when an untouched v3 file loads.</summary>
        private const float V3DefaultMusicVolume = 0.25f;
        /// <summary>Menu clicks and HUD cues.</summary>
        public float InterfaceVolume = 0.8f;
        /// <summary>Index into the project's quality levels; -1 keeps the project default.</summary>
        public int QualityLevel = -1;
        public bool Windowed;
        /// <summary>Small frame-rate counter in the corner (release builds; dev builds always show the dev readout).</summary>
        public bool ShowFps;
        /// <summary>Floating damage numbers on hits.</summary>
        public bool DamageNumbers = true;
        /// <summary>Rebound keys as "ActionId=path" entries; actions not listed use their default (see <see cref="KeyBindings"/>).</summary>
        public string[] KeyBindings = new string[0];

        public void Sanitize()
        {
            MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.05f, 10f);
            AimSensitivityMultiplier = Mathf.Clamp(AimSensitivityMultiplier, 0.1f, 2f);
            FieldOfView = Mathf.Clamp(FieldOfView, 70f, 110f);
            FrameRateCap = Mathf.Clamp(FrameRateCap, 0, 1000);
            MasterVolume = Mathf.Clamp01(MasterVolume);
            EffectsVolume = Mathf.Clamp01(EffectsVolume);
            AmbienceVolume = Mathf.Clamp01(AmbienceVolume);
            InterfaceVolume = Mathf.Clamp01(InterfaceVolume);
            // v1 files have no per-bus volumes or display fields; JsonUtility leaves the defaults, which is right.
            // v2 defaulted Music to 70% and v3 to 25%; files still on their old default move to the new 10% default.
            if (Version < 3 && Mathf.Abs(AmbienceVolume - OldDefaultMusicVolume) < 0.001f) AmbienceVolume = DefaultMusicVolume;
            if (Version < 4 && Mathf.Abs(AmbienceVolume - V3DefaultMusicVolume) < 0.001f) AmbienceVolume = DefaultMusicVolume;
            QualityLevel = Mathf.Clamp(QualityLevel, -1, 32);
            if (KeyBindings == null) KeyBindings = new string[0];
            Version = CurrentVersion;
        }
    }

    public interface ISettingsService
    {
        GameSettings Current { get; }
        event Action<GameSettings> Changed;
        void Save();
        void NotifyChanged();
    }

    public sealed class PlayerPrefsSettingsService : ISettingsService
    {
        private const string Key = "deadfall.settings";

        public PlayerPrefsSettingsService()
        {
            Current = Load();
        }

        public GameSettings Current { get; }
        public event Action<GameSettings> Changed;

        public void Save()
        {
            Current.Sanitize();
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Current));
            PlayerPrefs.Save();
        }

        public void NotifyChanged()
        {
            Current.Sanitize();
            Changed?.Invoke(Current);
        }

        private static GameSettings Load()
        {
            var settings = new GameSettings();
            string json = PlayerPrefs.GetString(Key, null);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(json, settings);
                }
                catch (Exception e)
                {
                    GameLog.Warn("Settings", $"Discarding unreadable settings: {e.Message}");
                    settings = new GameSettings();
                }
            }
            settings.Sanitize();
            return settings;
        }
    }
}
