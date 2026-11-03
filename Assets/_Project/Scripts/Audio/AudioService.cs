using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Rules.Common;
using UnityEngine;

namespace Deadfall.Audio
{
    /// <summary>
    /// Plays every sound (M13). A recorded clip in Resources/Audio named after the id wins; otherwise clips are
    /// synthesised from <see cref="SfxBank"/> recipes the first time they are needed (and warmed over the first few frames), then played from a fixed pool of AudioSources. Each id has a
    /// voice limit so fifty Husks do not mean fifty growls; the oldest voice of that id is reused. Volumes follow the
    /// Master, Effects, Ambience and Interface sliders in <see cref="GameSettings"/>.
    /// </summary>
    public sealed class AudioService : MonoBehaviour, IAudioSink
    {
        private const int PoolSize = 32;
        private const float AmbienceFade = 1.5f;

        private sealed class Voice
        {
            public AudioSource Source;
            public SfxId Id;
            public float EndsAt;
            public float BaseVolume;
            public SfxBus Bus;
        }

        private readonly Dictionary<SfxId, AudioClip[]> _clips = new Dictionary<SfxId, AudioClip[]>();
        private readonly Voice[] _voices = new Voice[PoolSize];
        private readonly Dictionary<SfxId, SfxInfo> _info = new Dictionary<SfxId, SfxInfo>();
        private int _variantCounter;
        private ISettingsService _settings;
        private AudioListener _listener;
        private float _nextListenerLookup;

        private AudioSource _ambA, _ambB;
        private SfxId _ambience = SfxId.None;
        private float _ambBlend = 1f; // 1 = A is the live bed
        private bool _ambUsingA = true;
        private int _warmIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
            if (GameAudio.Sink is AudioService) return;
            GameBootstrap.Initialize();
            var go = new GameObject("[AudioService]");
            DontDestroyOnLoad(go);
            go.AddComponent<AudioService>();
        }

        private void Awake()
        {
            GameAudio.Sink = this;
            // Each voice has its own child object so it can move to the sound's position.
            for (int i = 0; i < PoolSize; i++)
            {
                var child = new GameObject($"Voice{i}");
                child.transform.SetParent(transform, false);
                AudioSource src = child.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.dopplerLevel = 0f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 3f;
                _voices[i] = new Voice { Source = src };
            }
            _ambA = MakeAmbienceSource("AmbienceA");
            _ambB = MakeAmbienceSource("AmbienceB");
            if (ServiceRegistry.TryGet(out _settings))
            {
                _settings.Changed += OnSettingsChanged;
                OnSettingsChanged(_settings.Current);
            }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(GameAudio.Sink, this)) GameAudio.Sink = null;
            if (_settings != null) _settings.Changed -= OnSettingsChanged;
        }

        private AudioSource MakeAmbienceSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }

        private void OnSettingsChanged(GameSettings s) => AudioListener.volume = s.MasterVolume;

        private float BusVolume(SfxBus bus)
        {
            GameSettings s = _settings?.Current;
            if (s == null) return 1f;
            return bus switch
            {
                SfxBus.Interface => s.InterfaceVolume,
                SfxBus.Ambience => s.AmbienceVolume,
                _ => s.EffectsVolume,
            };
        }

        private SfxInfo InfoOf(SfxId id)
        {
            if (!_info.TryGetValue(id, out SfxInfo info)) _info[id] = info = SfxBank.Info(id);
            return info;
        }

        private AudioClip[] ClipsOf(SfxId id)
        {
            if (_clips.TryGetValue(id, out AudioClip[] clips)) return clips;
            AudioClip[] recorded = LoadRecorded(id);
            if (recorded.Length > 0) return _clips[id] = recorded;
            SfxInfo info = InfoOf(id);
            SfxLayer[] recipe = SfxBank.Recipe(id);
            if (recipe.Length == 0) return _clips[id] = System.Array.Empty<AudioClip>();
            int variants = Mathf.Max(1, info.Variants);
            clips = new AudioClip[variants];
            for (int v = 0; v < variants; v++)
            {
                ulong seed = ((ulong)id + 1UL) * 0x9E3779B97F4A7C15UL + (ulong)v * 7919UL;
                float[] data = info.LoopSeconds > 0f ? SfxSynth.RenderLoop(recipe, seed, info.LoopSeconds) : SfxSynth.Render(recipe, seed);
                AudioClip clip = AudioClip.Create($"sfx_{id}_{v}", data.Length, 1, SfxSynth.SampleRate, false);
                clip.SetData(data, 0);
                clips[v] = clip;
            }
            return _clips[id] = clips;
        }

        /// <summary>
        /// Recorded clips override the synthesised sound (M13): <c>Resources/Audio/&lt;SfxId&gt;.wav</c> or numbered
        /// variants <c>&lt;SfxId&gt;_1</c>, <c>_2</c>... (wav or ogg). Each play picks the next variant.
        /// </summary>
        private static AudioClip[] LoadRecorded(SfxId id)
        {
            var list = new List<AudioClip>(4);
            AudioClip single = Resources.Load<AudioClip>("Audio/" + id);
            if (single != null) list.Add(single);
            for (int n = 1; n <= 12; n++)
            {
                AudioClip c = Resources.Load<AudioClip>($"Audio/{id}_{n}");
                if (c == null) break;
                list.Add(c);
            }
            return list.ToArray();
        }

        private Vector3 ListenerPosition()
        {
            if ((_listener == null || !_listener.isActiveAndEnabled) && Time.unscaledTime >= _nextListenerLookup)
            {
                _nextListenerLookup = Time.unscaledTime + 1f;
                _listener = null;
                foreach (AudioListener l in Resources.FindObjectsOfTypeAll<AudioListener>())
                    if (l != null && l.isActiveAndEnabled && l.gameObject.scene.IsValid()) { _listener = l; break; }
            }
            return _listener != null ? _listener.transform.position : Vector3.zero;
        }

        // ---------------------------------------------------------------- IAudioSink

        public void Play(SfxId id, Vector3 position, float volume, float pitch) => PlayInternal(id, position, true, volume, pitch);

        public void Play2D(SfxId id, float volume, float pitch) => PlayInternal(id, Vector3.zero, false, volume, pitch);

        private void PlayInternal(SfxId id, Vector3 position, bool world, float volume, float pitch)
        {
            if (id == SfxId.None) return;
            SfxInfo info = InfoOf(id);
            if (info.LoopSeconds > 0f) { SetAmbience(id); return; }
            bool spatial = world && info.Spatial;
            if (spatial && _listener != null && (position - ListenerPosition()).sqrMagnitude > info.MaxDistance * info.MaxDistance) return;
            AudioClip[] clips = ClipsOf(id);
            if (clips.Length == 0) return;

            Voice v = PickVoice(id, info.MaxVoices);
            if (v == null) return;
            AudioClip clip = clips[(_variantCounter++) % clips.Length];
            float p = pitch * (1f + Random.Range(-info.PitchJitter, info.PitchJitter));
            v.Id = id;
            v.Bus = info.Bus;
            v.BaseVolume = info.Volume * Mathf.Clamp(volume, 0f, 2f);
            v.EndsAt = Time.unscaledTime + clip.length / Mathf.Max(0.1f, p);
            AudioSource s = v.Source;
            s.Stop();
            s.clip = clip;
            s.pitch = p;
            s.volume = v.BaseVolume * BusVolume(info.Bus);
            s.spatialBlend = spatial ? 1f : 0f;
            s.maxDistance = Mathf.Max(5f, info.MaxDistance);
            s.transform.position = spatial ? position : Vector3.zero;
            s.Play();
        }

        /// <summary>A free voice; else the oldest voice of this id once it has its limit; else the voice ending soonest.</summary>
        private Voice PickVoice(SfxId id, int maxVoices)
        {
            float now = Time.unscaledTime;
            int same = 0;
            Voice oldestSame = null, free = null, soonest = null;
            foreach (Voice v in _voices)
            {
                bool busy = v.Source.isPlaying && v.EndsAt > now;
                if (!busy) { if (free == null) free = v; continue; }
                if (v.Id == id)
                {
                    same++;
                    if (oldestSame == null || v.EndsAt < oldestSame.EndsAt) oldestSame = v;
                }
                if (v.Bus != SfxBus.Interface && (soonest == null || v.EndsAt < soonest.EndsAt)) soonest = v;
            }
            if (same >= Mathf.Max(1, maxVoices)) return oldestSame;
            return free ?? soonest;
        }

        public void SetAmbience(SfxId id)
        {
            if (id == _ambience) return;
            _ambience = id;
            AudioSource next = _ambUsingA ? _ambB : _ambA;
            _ambUsingA = !_ambUsingA;
            _ambBlend = 0f;
            if (id == SfxId.None) { next.Stop(); next.clip = null; return; }
            AudioClip[] clips = ClipsOf(id);
            if (clips.Length == 0) return;
            next.clip = clips[0];
            next.volume = 0f;
            next.Play();
        }

        private void Update()
        {
            // Warm one clip a frame so the first gunshot does not hitch.
            if (_warmIndex < SfxBank.All.Length) ClipsOf(SfxBank.All[_warmIndex++]);

            float amb = BusVolume(SfxBus.Ambience) * (_ambience == SfxId.None ? 0.5f : InfoOf(_ambience).Volume);
            _ambBlend = Mathf.MoveTowards(_ambBlend, 1f, Time.unscaledDeltaTime / AmbienceFade);
            AudioSource live = _ambUsingA ? _ambA : _ambB;
            AudioSource old = _ambUsingA ? _ambB : _ambA;
            live.volume = _ambience == SfxId.None ? 0f : amb * _ambBlend;
            old.volume = old.isPlaying ? amb * (1f - _ambBlend) : 0f;
            if (_ambBlend >= 1f && old.isPlaying) old.Stop();
            if (_ambience == SfxId.None && live.isPlaying && old.volume <= 0f) live.Stop();

            // Slider changes apply to voices already playing.
            foreach (Voice v in _voices)
                if (v.Source.isPlaying) v.Source.volume = v.BaseVolume * BusVolume(v.Bus);
        }
    }
}
