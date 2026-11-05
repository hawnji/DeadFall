using System;
using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Music while on a generated map: the level tracks play in turn ("Endless Infestation", then part 2, then
    /// back), crossfading near the end of each. Stops when leaving the map; never plays over the title screen.
    /// Follows the Music slider like the title theme.
    /// </summary>
    public sealed class LevelMusic : MonoBehaviour
    {
        public static readonly string[] Tracks = { "Music/EndlessInfestation", "Music/EndlessInfestation2" };
        private const float Crossfade = 3f;
        private const float StartFade = 2.5f;
        private const float StopFade = 2f;

        /// <summary>True while something else owns the music (the title screen). Set by the bootstrap.</summary>
        public static Func<bool> Suppressed;

        private readonly AudioClip[] _clips = new AudioClip[Tracks.Length];
        private bool _playing;
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
            if (!OnMap)
            {
                if (_playing)
                {
                    _playing = false;
                    GameAudio.StopMusic(StopFade);
                }
                return;
            }
            float now = Time.unscaledTime;
            if (!_playing)
            {
                _playing = true;
                PlayNext(StartFade, now);
                return;
            }
            if (now >= _switchAt) PlayNext(Crossfade, now);
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
