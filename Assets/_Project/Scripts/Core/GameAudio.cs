using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>What plays the sounds. Deadfall.Audio registers the real one; until then calls do nothing.</summary>
    public interface IAudioSink
    {
        void Play(SfxId id, Vector3 position, float volume, float pitch);
        void Play2D(SfxId id, float volume, float pitch);
        void SetAmbience(SfxId id);
        /// <summary>Crossfades to a looping music track (null fades music out).</summary>
        void PlayMusic(AudioClip clip, float fadeSeconds);
    }

    /// <summary>
    /// The one place gameplay code asks for a sound (M13). Lives in Core so every assembly can call it without
    /// referencing Deadfall.Audio; the audio service registers itself as the <see cref="Sink"/> at startup.
    /// </summary>
    public static class GameAudio
    {
        public static IAudioSink Sink { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Sink = null;

        /// <summary>A sound in the world. Ids that are not spatial (<see cref="SfxBank.Info"/>) play flat.</summary>
        public static void Play(SfxId id, Vector3 position, float volume = 1f, float pitch = 1f) => Sink?.Play(id, position, volume, pitch);

        /// <summary>A sound in the player's ears: interface, markers, the player's own body.</summary>
        public static void Play2D(SfxId id, float volume = 1f, float pitch = 1f) => Sink?.Play2D(id, volume, pitch);

        /// <summary>Switches the looping background bed (<see cref="SfxId.None"/> fades it out).</summary>
        public static void SetAmbience(SfxId id) => Sink?.SetAmbience(id);

        /// <summary>Loops a track from Resources (e.g. "Music/TitleTheme"), following the Music slider.</summary>
        public static void PlayMusic(string resourcePath, float fadeSeconds = 1.5f)
        {
            if (Sink == null) return;
            AudioClip clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null) { Debug.LogWarning($"[Audio] Music '{resourcePath}' not found in Resources."); return; }
            Sink.PlayMusic(clip, fadeSeconds);
        }

        public static void StopMusic(float fadeSeconds = 1.5f) => Sink?.PlayMusic(null, fadeSeconds);
    }
}
