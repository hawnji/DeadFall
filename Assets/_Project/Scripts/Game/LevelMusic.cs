using System;
using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Music outside the title screen. On a generated map the level tracks play in turn ("Endless Infestation",
    /// then part 2, then back), crossfading near the end of each. In the hub (FOB Halcyon and the range) "Bunker
    /// Pulse" loops. Switching between the two crossfades; never plays over the title screen. Follows the Music slider.
    /// </summary>
    public sealed class LevelMusic : MonoBehaviour
    {
        public static readonly string[] Tracks = { "Music/EndlessInfestation", "Music/EndlessInfestation2" };
        public const string HubTrack = "Music/BunkerPulse";
        private const float Crossfade = 3f;
        private const float StartFade = 2.5f;
        private const float StopFade = 2f;

        /// <summary>True while something else owns the music (the title screen). Set by the bootstrap.</summary>
        public static Func<bool> Suppressed;

        private readonly AudioClip[] _clips = new AudioClip[Tracks.Length];
        private AudioClip _hubClip;
        private bool _playing;
        /// <summary>Which set is playing: true for the map tracks, false for the hub loop.</summary>
        private bool _mapMode;
        private int _track = -1;
        private float _switchAt;

        private bool OnMap => MapSession.Current != null && MapSession.Current.Map != null;

        private void Update()
        {
            if (Suppressed != null && Suppressed())
            {
                _playing = false; // the title owns the music; start fresh when it closes
                return;
            }
            float now = Time.unscaledTime;
            bool onMap = OnMap;
            if (!_playing || onMap != _mapMode)
            {
                bool switching = _playing;
                _playing = true;
                _mapMode = onMap;
                if (onMap) PlayNext(switching ? Crossfade : StartFade, now);
                else PlayHub(switching ? Crossfade : StartFade, now);
                return;
            }
            if (now >= _switchAt)
            {
                if (_mapMode) PlayNext(Crossfade, now);
                else PlayHub(Crossfade, now);
            }
        }

        /// <summary>The hub loop: restarts with a crossfade near the end.</summary>
        private void PlayHub(float fade, float now)
        {
            if (_hubClip == null) _hubClip = Resources.Load<AudioClip>(HubTrack);
            if (_hubClip == null)
            {
                GameAudio.StopMusic(StopFade);
                GameLog.Warn("Music", $"Hub music: {HubTrack} not found in Resources.");
                _switchAt = now + 30f;
                return;
            }
            GameAudio.PlayMusic(_hubClip, fade);
            GameLog.Info("Music", $"Hub music: {HubTrack} ({_hubClip.length:0} s)");
            _switchAt = now + Mathf.Max(10f, _hubClip.length - Crossfade);
        }

        private void PlayNext(float fade, float now)
        {
            for (int tries = 0; tries < Tracks.Length; tries++)
            {
                _track = (_track + 1) % Tracks.Length;
                AudioClip clip = Clip(_track);
                if (clip == null) continue;
                GameAudio.PlayMusic(clip, fade);
                GameLog.Info("Music", $"Level music: {Tracks[_track]} ({clip.length:0} s)");
                _switchAt = now + Mathf.Max(10f, clip.length - Crossfade);
                return;
            }
            GameLog.Warn("Music", "Level music: no tracks found in Resources/Music (EndlessInfestation, EndlessInfestation2).");
            _switchAt = now + 30f; // no tracks found; try again later
        }

        private AudioClip Clip(int i)
        {
            if (_clips[i] == null) _clips[i] = Resources.Load<AudioClip>(Tracks[i]);
            return _clips[i];
        }

        private void OnDisable()
        {
            if (_playing) GameAudio.StopMusic(StopFade);
            _playing = false;
        }

        private void OnDestroy()
        {
            Suppressed = null;
        }
    }
}
