using System;

namespace Deadfall.Rules.Common
{
    /// <summary>
    /// Every sound the game plays (M13). The game ships no audio files yet: each id is synthesised once at startup
    /// from a small recipe in <see cref="SfxBank"/>, so swapping in recorded clips later only means mapping ids to
    /// files. Not sent over the network, so the order is free to change.
    /// </summary>
    public enum SfxId : byte
    {
        None,
        UiClick, UiConfirm, UiDeny,
        ShotPistol, ShotRifle, ShotShotgun, ShotSmg, ShotHeavy, ShotLauncher, ShotTurret,
        MeleeSwing, Reload, DryFire,
        HitMarker, CritMarker, KillMarker,
        PlayerHurt, PlayerDowned, PlayerRevived,
        EnemyGrowl, EnemyAttack, EnemyDeath, EnemySpit, BossRoar,
        Explosion, BarricadeBreak, Pickup, Credits,
        SurgeStart, SurgeClear, ObjectiveDone, EventAlarm,
        Footstep,
        NailBoard,
        PlayerJump,
        EnemyWalk,
        DoorOpening,
        LowHealthHeartbeat,
        PurchaseDoor,
        /// <summary>Creepers' and the Broodmother's skittering legs, in place of <see cref="EnemyWalk"/>.</summary>
        SpiderWalk,
    }

    public enum SfxBus : byte { Effects, Interface, Ambience }

    public enum SfxWave : byte { Noise, Sine, Square, Saw, Triangle }

    /// <summary>One voice of a recipe: a waveform with a pitch glide, an attack/decay envelope and a sweeping low-pass.</summary>
    public struct SfxLayer
    {
        public SfxWave Wave;
        public float Delay, Duration;
        /// <summary>Seconds to full volume, then an exponential fall with this time constant (0 = hold).</summary>
        public float Attack, Decay;
        public float Freq0, Freq1;
        public float Gain;
        /// <summary>One-pole low-pass cutoff in Hz at the start and end (0 = off).</summary>
        public float Cutoff0, Cutoff1;
        /// <summary>Soft clipping amount (1 or less = clean).</summary>
        public float Drive;
        /// <summary>Amplitude wobble rate in Hz and depth 0..1 (ambience beds, alarms).</summary>
        public float Tremolo, TremoloDepth;

        public static SfxLayer Noise(float dur, float decay, float gain, float cut0, float cut1, float delay = 0f) => new SfxLayer
            { Wave = SfxWave.Noise, Duration = dur, Decay = decay, Gain = gain, Cutoff0 = cut0, Cutoff1 = cut1, Delay = delay, Attack = 0.002f };

        public static SfxLayer Tone(SfxWave wave, float dur, float f0, float f1, float decay, float gain, float delay = 0f, float cutoff = 0f) => new SfxLayer
            { Wave = wave, Duration = dur, Freq0 = f0, Freq1 = f1, Decay = decay, Gain = gain, Delay = delay, Attack = 0.003f, Cutoff0 = cutoff, Cutoff1 = cutoff };
    }

    /// <summary>How the game plays an id: which volume slider it follows, how many can overlap, and how it varies.</summary>
    public struct SfxInfo
    {
        public SfxBus Bus;
        public float Volume;
        public int MaxVoices;
        public float PitchJitter;
        /// <summary>Played in the world (3D) rather than flat in the player's ears.</summary>
        public bool Spatial;
        public float MaxDistance;
        /// <summary>Different noise seeds rendered, so repeats are not identical.</summary>
        public int Variants;
        /// <summary>Ambience: rendered as a seamless loop of this many seconds.</summary>
        public float LoopSeconds;
    }

    /// <summary>Renders <see cref="SfxLayer"/> recipes into mono float samples. Engine-free and deterministic per seed.</summary>
    public static class SfxSynth
    {
        public const int SampleRate = 22050;
        public const float Peak = 0.9f;

        public static float Length(SfxLayer[] layers)
        {
            float len = 0f;
            if (layers == null) return 0f;
            foreach (SfxLayer l in layers) len = Math.Max(len, l.Delay + l.Duration);
            return len;
        }

        public static float[] Render(SfxLayer[] layers, ulong seed, int sampleRate = SampleRate)
        {
            int n = Math.Max(1, (int)Math.Ceiling(Length(layers) * sampleRate));
            var buf = new float[n];
            if (layers == null) return buf;
            var rng = new DeterministicRandom(seed == 0 ? 0x5EEDUL : seed);
            foreach (SfxLayer l in layers) Mix(buf, l, rng, sampleRate);
            Normalize(buf, Peak);
            return buf;
        }

        /// <summary>A loop of <paramref name="seconds"/> whose end runs straight into its start (one second crossfade).</summary>
        public static float[] RenderLoop(SfxLayer[] layers, ulong seed, float seconds, int sampleRate = SampleRate)
        {
            int n = Math.Max(2, (int)(seconds * sampleRate));
            int fade = Math.Min(n / 2, sampleRate);
            var stretched = new SfxLayer[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                stretched[i] = layers[i];
                stretched[i].Duration = seconds + fade / (float)sampleRate;
                stretched[i].Delay = 0f;
            }
            float[] raw = Render(stretched, seed, sampleRate);
            var loop = new float[n];
            Array.Copy(raw, loop, n);
            for (int i = 0; i < fade && n + i < raw.Length; i++)
            {
                float t = i / (float)fade;
                loop[i] = loop[i] * t + raw[n + i] * (1f - t);
            }
            Normalize(loop, Peak * 0.8f);
            return loop;
        }

        private static void Mix(float[] buf, SfxLayer l, DeterministicRandom rng, int sr)
        {
            int start = (int)(l.Delay * sr);
            int len = (int)(l.Duration * sr);
            if (len <= 0 || l.Gain <= 0f) return;
            double phase = 0.0;
            float lp = 0f;
            float tail = Math.Min(0.006f, l.Duration * 0.25f);
            float drive = l.Drive > 1f ? l.Drive : 0f;
            float driveNorm = drive > 0f ? (float)Math.Tanh(drive) : 1f;
            for (int i = 0; i < len; i++)
            {
                int k = start + i;
                if (k >= buf.Length) break;
                float t = i / (float)sr;
                float u = len > 1 ? i / (float)(len - 1) : 0f;

                float env = l.Attack > 0f && t < l.Attack ? t / l.Attack : l.Decay > 0f ? (float)Math.Exp(-(t - l.Attack) / l.Decay) : 1f;
                float left = l.Duration - t;
                if (left < tail) env *= Math.Max(0f, left / tail);
                if (l.TremoloDepth > 0f) env *= 1f - l.TremoloDepth * 0.5f * (1f + (float)Math.Sin(2.0 * Math.PI * l.Tremolo * t));

                float s;
                if (l.Wave == SfxWave.Noise) s = rng.NextFloat() * 2f - 1f;
                else
                {
                    double f = Glide(l.Freq0, l.Freq1, u);
                    phase += f / sr;
                    phase -= Math.Floor(phase);
                    s = Wave(l.Wave, (float)phase);
                }

                float cut = (float)Glide(l.Cutoff0, l.Cutoff1, u);
                if (cut > 0f)
                {
                    float a = 1f - (float)Math.Exp(-2.0 * Math.PI * Math.Min(cut, sr * 0.45f) / sr);
                    lp += a * (s - lp);
                    s = lp;
                }
                s *= env * l.Gain;
                if (drive > 0f) s = (float)Math.Tanh(s * drive) / driveNorm;
                buf[k] += s;
            }
        }

        private static double Glide(float a, float b, float u)
        {
            if (a <= 0f || b <= 0f) return a + (b - a) * u;
            return a * Math.Pow(b / a, u);
        }

        private static float Wave(SfxWave w, float p) => w switch
        {
            SfxWave.Sine => (float)Math.Sin(2.0 * Math.PI * p),
            SfxWave.Square => p < 0.5f ? 0.6f : -0.6f,
            SfxWave.Saw => (2f * p - 1f) * 0.7f,
            SfxWave.Triangle => 1f - 4f * Math.Abs(p - 0.5f),
            _ => 0f,
        };

        public static void Normalize(float[] buf, float peak)
        {
            float max = 0f;
            foreach (float v in buf) max = Math.Max(max, Math.Abs(v));
            if (max < 1e-6f) return;
            float g = peak / max;
            for (int i = 0; i < buf.Length; i++) buf[i] *= g;
        }
    }

    /// <summary>The recipes and playback rules for every <see cref="SfxId"/>.</summary>
    public static class SfxBank
    {
        public static readonly SfxId[] All = (SfxId[])Enum.GetValues(typeof(SfxId));

        public static SfxInfo Info(SfxId id)
        {
            var i = new SfxInfo { Bus = SfxBus.Effects, Volume = 0.8f, MaxVoices = 4, PitchJitter = 0.06f, Spatial = true, MaxDistance = 45f, Variants = 1 };
            switch (id)
            {
                case SfxId.UiClick: case SfxId.UiConfirm: case SfxId.UiDeny:
                    i.Bus = SfxBus.Interface; i.Spatial = false; i.PitchJitter = 0f; i.MaxVoices = 2; i.Volume = 0.5f; break;
                case SfxId.HitMarker: case SfxId.CritMarker: case SfxId.KillMarker:
                    i.Spatial = false; i.MaxVoices = 3; i.Volume = 0.45f; i.PitchJitter = 0.03f; break;
                case SfxId.ShotPistol: case SfxId.ShotRifle: case SfxId.ShotSmg: case SfxId.ShotHeavy:
                case SfxId.ShotShotgun: case SfxId.ShotLauncher: case SfxId.ShotTurret:
                    i.Variants = 3; i.MaxVoices = 8; i.MaxDistance = 90f; i.Volume = 0.75f; i.PitchJitter = 0.05f; break;
                case SfxId.EnemyGrowl: i.Variants = 4; i.MaxVoices = 4; i.MaxDistance = 28f; i.Volume = 0.55f; i.PitchJitter = 0.18f; break;
                case SfxId.EnemyAttack: i.Variants = 2; i.MaxVoices = 4; i.MaxDistance = 25f; i.PitchJitter = 0.15f; break;
                case SfxId.EnemyDeath: i.Variants = 3; i.MaxVoices = 5; i.MaxDistance = 35f; i.Volume = 0.6f; i.PitchJitter = 0.2f; break;
                case SfxId.Explosion: i.Variants = 2; i.MaxVoices = 4; i.MaxDistance = 120f; i.Volume = 1f; break;
                case SfxId.BossRoar: i.MaxVoices = 1; i.MaxDistance = 140f; i.Volume = 1f; i.PitchJitter = 0.04f; break;
                case SfxId.PlayerHurt: case SfxId.PlayerDowned: case SfxId.PlayerRevived: case SfxId.Reload: case SfxId.DryFire:
                case SfxId.MeleeSwing: case SfxId.Pickup: case SfxId.Credits:
                    i.Spatial = false; i.MaxVoices = 2; i.Volume = 0.6f; break;
                case SfxId.SurgeStart: case SfxId.SurgeClear: case SfxId.ObjectiveDone: case SfxId.EventAlarm:
                    i.Spatial = false; i.MaxVoices = 1; i.Volume = 0.7f; i.PitchJitter = 0f; break;
                case SfxId.Footstep: i.Spatial = false; i.MaxVoices = 2; i.Volume = 0.35f; i.PitchJitter = 0.08f; i.Variants = 3; break;
                case SfxId.NailBoard: i.MaxVoices = 6; i.MaxDistance = 30f; i.Volume = 0.8f; i.PitchJitter = 0.06f; break;
                case SfxId.PlayerJump: i.Spatial = false; i.MaxVoices = 1; i.Volume = 0.55f; i.PitchJitter = 0.06f; break;
                case SfxId.EnemyWalk: i.MaxVoices = 6; i.MaxDistance = 20f; i.Volume = 0.5f; i.PitchJitter = 0.12f; break;
                case SfxId.SpiderWalk: i.MaxVoices = 6; i.MaxDistance = 28f; i.Volume = 0.7f; i.PitchJitter = 0.1f; break;
                case SfxId.DoorOpening: i.MaxVoices = 3; i.MaxDistance = 35f; i.Volume = 0.9f; i.PitchJitter = 0.03f; break;
                case SfxId.LowHealthHeartbeat: i.Spatial = false; i.MaxVoices = 1; i.Volume = 0.7f; i.PitchJitter = 0f; break;
                case SfxId.PurchaseDoor: i.MaxVoices = 2; i.MaxDistance = 30f; i.Volume = 0.85f; i.PitchJitter = 0f; break;
            }
            return i;
        }

        public static SfxLayer[] Recipe(SfxId id)
        {
            switch (id)
            {
                case SfxId.UiClick: return new[] { SfxLayer.Tone(SfxWave.Triangle, 0.05f, 1800f, 1200f, 0.012f, 0.8f), SfxLayer.Noise(0.02f, 0.004f, 0.3f, 6000f, 3000f) };
                case SfxId.UiConfirm: return new[] { SfxLayer.Tone(SfxWave.Triangle, 0.09f, 660f, 660f, 0.05f, 0.7f), SfxLayer.Tone(SfxWave.Triangle, 0.14f, 990f, 990f, 0.07f, 0.7f, 0.07f) };
                case SfxId.UiDeny: return new[] { SfxLayer.Tone(SfxWave.Square, 0.18f, 220f, 180f, 0.08f, 0.6f, 0f, 1800f) };

                case SfxId.ShotPistol: return Shot(0.32f, 2600f, 180f, 1.0f, 140f);
                case SfxId.ShotRifle: return Shot(0.42f, 3400f, 140f, 1.2f, 110f);
                case SfxId.ShotSmg: return Shot(0.26f, 3800f, 210f, 0.9f, 170f);
                case SfxId.ShotHeavy: return Shot(0.6f, 2400f, 90f, 1.5f, 70f);
                case SfxId.ShotTurret: return Shot(0.22f, 4200f, 260f, 0.8f, 200f);
                case SfxId.ShotShotgun: return new[]
                {
                    SfxLayer.Noise(0.65f, 0.14f, 1f, 2200f, 300f),
                    SfxLayer.Noise(0.08f, 0.02f, 0.8f, 8000f, 3000f),
                    SfxLayer.Tone(SfxWave.Sine, 0.3f, 95f, 45f, 0.09f, 0.9f),
                };
                case SfxId.ShotLauncher: return new[]
                {
                    SfxLayer.Noise(0.4f, 0.1f, 0.7f, 900f, 200f),
                    SfxLayer.Tone(SfxWave.Sine, 0.25f, 160f, 60f, 0.08f, 0.8f),
                };

                case SfxId.MeleeSwing: return new[] { new SfxLayer { Wave = SfxWave.Noise, Duration = 0.28f, Attack = 0.1f, Decay = 0.06f, Gain = 0.7f, Cutoff0 = 600f, Cutoff1 = 2600f } };
                case SfxId.Reload: return new[]
                {
                    SfxLayer.Noise(0.05f, 0.01f, 0.7f, 5000f, 2500f),
                    SfxLayer.Tone(SfxWave.Square, 0.04f, 900f, 700f, 0.01f, 0.4f, 0f, 3000f),
                    SfxLayer.Noise(0.06f, 0.012f, 0.9f, 4000f, 2000f, 0.28f),
                    SfxLayer.Tone(SfxWave.Square, 0.05f, 1200f, 800f, 0.012f, 0.5f, 0.3f, 3500f),
                };
                case SfxId.DryFire: return new[] { SfxLayer.Tone(SfxWave.Square, 0.04f, 2400f, 1600f, 0.008f, 0.5f, 0f, 5000f), SfxLayer.Noise(0.03f, 0.006f, 0.4f, 7000f, 4000f) };

                case SfxId.HitMarker: return new[] { SfxLayer.Tone(SfxWave.Sine, 0.05f, 2400f, 2000f, 0.015f, 0.8f) };
                case SfxId.CritMarker: return new[] { SfxLayer.Tone(SfxWave.Sine, 0.06f, 3200f, 2800f, 0.02f, 0.8f), SfxLayer.Tone(SfxWave.Sine, 0.06f, 4800f, 4000f, 0.015f, 0.3f) };
                case SfxId.KillMarker: return new[] { SfxLayer.Tone(SfxWave.Triangle, 0.12f, 1500f, 1500f, 0.04f, 0.8f), SfxLayer.Tone(SfxWave.Triangle, 0.16f, 2250f, 2250f, 0.05f, 0.6f, 0.05f) };

                case SfxId.PlayerHurt: return new[] { SfxLayer.Tone(SfxWave.Sine, 0.25f, 120f, 55f, 0.08f, 1f), SfxLayer.Noise(0.18f, 0.05f, 0.5f, 900f, 200f) };
                case SfxId.PlayerDowned: return new[]
                {
                    SfxLayer.Tone(SfxWave.Sine, 1.4f, 220f, 110f, 0.6f, 0.7f),
                    SfxLayer.Tone(SfxWave.Sine, 1.4f, 233f, 116f, 0.6f, 0.5f),
                    SfxLayer.Noise(1.2f, 0.4f, 0.3f, 400f, 120f),
                };
                case SfxId.PlayerRevived: return new[]
                {
                    SfxLayer.Tone(SfxWave.Triangle, 0.18f, 440f, 440f, 0.1f, 0.6f),
                    SfxLayer.Tone(SfxWave.Triangle, 0.18f, 554f, 554f, 0.1f, 0.6f, 0.1f),
                    SfxLayer.Tone(SfxWave.Triangle, 0.4f, 659f, 659f, 0.2f, 0.6f, 0.2f),
                };

                case SfxId.EnemyGrowl: return new[]
                {
                    new SfxLayer { Wave = SfxWave.Saw, Duration = 0.9f, Attack = 0.15f, Decay = 0.4f, Freq0 = 85f, Freq1 = 62f, Gain = 0.7f, Cutoff0 = 700f, Cutoff1 = 350f, Drive = 2.5f, Tremolo = 17f, TremoloDepth = 0.6f },
                    new SfxLayer { Wave = SfxWave.Noise, Duration = 0.9f, Attack = 0.2f, Decay = 0.35f, Gain = 0.35f, Cutoff0 = 1200f, Cutoff1 = 500f },
                };
                case SfxId.EnemyAttack: return new[]
                {
                    new SfxLayer { Wave = SfxWave.Saw, Duration = 0.4f, Attack = 0.03f, Decay = 0.15f, Freq0 = 160f, Freq1 = 90f, Gain = 0.8f, Cutoff0 = 1500f, Cutoff1 = 500f, Drive = 3f },
                    SfxLayer.Noise(0.12f, 0.03f, 0.7f, 1800f, 600f, 0.15f),
                };
                case SfxId.EnemyDeath: return new[]
                {
                    new SfxLayer { Wave = SfxWave.Saw, Duration = 0.8f, Attack = 0.02f, Decay = 0.3f, Freq0 = 140f, Freq1 = 50f, Gain = 0.7f, Cutoff0 = 900f, Cutoff1 = 200f, Drive = 2f, Tremolo = 11f, TremoloDepth = 0.5f },
                    SfxLayer.Noise(0.25f, 0.08f, 0.6f, 700f, 150f, 0.35f),
                };
                case SfxId.EnemySpit: return new[] { SfxLayer.Noise(0.3f, 0.08f, 0.8f, 3000f, 800f), SfxLayer.Tone(SfxWave.Sine, 0.2f, 300f, 700f, 0.06f, 0.4f) };
                case SfxId.BossRoar: return new[]
                {
                    new SfxLayer { Wave = SfxWave.Saw, Duration = 2.2f, Attack = 0.25f, Decay = 0.9f, Freq0 = 70f, Freq1 = 42f, Gain = 0.8f, Cutoff0 = 900f, Cutoff1 = 250f, Drive = 4f, Tremolo = 23f, TremoloDepth = 0.45f },
                    new SfxLayer { Wave = SfxWave.Saw, Duration = 2.2f, Attack = 0.3f, Decay = 0.9f, Freq0 = 105f, Freq1 = 63f, Gain = 0.5f, Cutoff0 = 1200f, Cutoff1 = 300f, Drive = 3f },
                    new SfxLayer { Wave = SfxWave.Noise, Duration = 2f, Attack = 0.3f, Decay = 0.8f, Gain = 0.4f, Cutoff0 = 1600f, Cutoff1 = 400f },
                };

                case SfxId.Explosion: return new[]
                {
                    SfxLayer.Noise(1.8f, 0.45f, 1f, 1800f, 120f),
                    SfxLayer.Tone(SfxWave.Sine, 0.9f, 70f, 28f, 0.3f, 1f),
                    SfxLayer.Noise(0.1f, 0.025f, 0.6f, 9000f, 4000f),
                };
                case SfxId.BarricadeBreak: return new[] { SfxLayer.Noise(0.35f, 0.07f, 0.9f, 2500f, 700f), SfxLayer.Tone(SfxWave.Triangle, 0.2f, 180f, 90f, 0.05f, 0.6f, 0.02f) };
                case SfxId.Footstep: return new[] { SfxLayer.Noise(0.09f, 0.025f, 0.9f, 900f, 300f), SfxLayer.Tone(SfxWave.Sine, 0.07f, 110f, 70f, 0.02f, 0.5f) };
                case SfxId.NailBoard: return new[] { SfxLayer.Noise(0.06f, 0.02f, 0.9f, 3200f, 1200f), SfxLayer.Tone(SfxWave.Triangle, 0.12f, 420f, 260f, 0.03f, 0.6f, 0.01f) };
                case SfxId.PlayerJump: return new[] { SfxLayer.Noise(0.12f, 0.04f, 0.6f, 1400f, 500f), SfxLayer.Tone(SfxWave.Sine, 0.1f, 140f, 200f, 0.04f, 0.4f) };
                case SfxId.SpiderWalk: return new[] { SfxLayer.Noise(0.05f, 0.01f, 0.9f, 2600f, 900f), SfxLayer.Noise(0.05f, 0.01f, 0.7f, 2200f, 800f, 0.07f) };
                case SfxId.EnemyWalk: return new[] { SfxLayer.Noise(0.1f, 0.03f, 0.8f, 700f, 250f), SfxLayer.Tone(SfxWave.Sine, 0.08f, 90f, 60f, 0.03f, 0.5f) };
                case SfxId.DoorOpening: return new[] { SfxLayer.Noise(1.1f, 0.6f, 0.5f, 900f, 400f), SfxLayer.Tone(SfxWave.Saw, 1f, 70f, 110f, 0.6f, 0.25f, 0f, 600f) };
                case SfxId.LowHealthHeartbeat: return new[] { SfxLayer.Tone(SfxWave.Sine, 0.12f, 60f, 45f, 0.05f, 0.9f), SfxLayer.Tone(SfxWave.Sine, 0.12f, 55f, 40f, 0.05f, 0.7f, 0.18f) };
                case SfxId.PurchaseDoor: return new[] { SfxLayer.Tone(SfxWave.Triangle, 0.12f, 880f, 880f, 0.06f, 0.6f), SfxLayer.Tone(SfxWave.Triangle, 0.2f, 1320f, 1320f, 0.1f, 0.6f, 0.1f) };
                case SfxId.Pickup: return new[] { SfxLayer.Tone(SfxWave.Triangle, 0.08f, 880f, 1320f, 0.04f, 0.6f), SfxLayer.Noise(0.05f, 0.01f, 0.3f, 6000f, 3000f) };
                case SfxId.Credits: return new[] { SfxLayer.Tone(SfxWave.Sine, 0.12f, 1568f, 1568f, 0.05f, 0.5f), SfxLayer.Tone(SfxWave.Sine, 0.16f, 2093f, 2093f, 0.06f, 0.5f, 0.05f) };

                case SfxId.SurgeStart: return new[]
                {
                    new SfxLayer { Wave = SfxWave.Saw, Duration = 1.8f, Attack = 0.3f, Decay = 0.9f, Freq0 = 110f, Freq1 = 104f, Gain = 0.6f, Cutoff0 = 600f, Cutoff1 = 1200f, Drive = 1.6f },
                    new SfxLayer { Wave = SfxWave.Saw, Duration = 1.8f, Attack = 0.3f, Decay = 0.9f, Freq0 = 165f, Freq1 = 156f, Gain = 0.45f, Cutoff0 = 700f, Cutoff1 = 1300f },
                    SfxLayer.Tone(SfxWave.Sine, 1.2f, 55f, 52f, 0.6f, 0.8f),
                };
                case SfxId.SurgeClear: return new[]
                {
                    SfxLayer.Tone(SfxWave.Triangle, 0.3f, 523f, 523f, 0.18f, 0.6f),
                    SfxLayer.Tone(SfxWave.Triangle, 0.3f, 659f, 659f, 0.18f, 0.6f, 0.12f),
                    SfxLayer.Tone(SfxWave.Triangle, 0.8f, 784f, 784f, 0.4f, 0.6f, 0.24f),
                };
                case SfxId.ObjectiveDone: return new[]
                {
                    SfxLayer.Tone(SfxWave.Triangle, 0.25f, 587f, 587f, 0.12f, 0.6f),
                    SfxLayer.Tone(SfxWave.Triangle, 0.6f, 880f, 880f, 0.3f, 0.6f, 0.1f),
                };
                case SfxId.EventAlarm: return new[]
                {
                    new SfxLayer { Wave = SfxWave.Square, Duration = 1.6f, Attack = 0.02f, Decay = 0f, Freq0 = 620f, Freq1 = 620f, Gain = 0.4f, Cutoff0 = 2500f, Cutoff1 = 2500f, Tremolo = 2.5f, TremoloDepth = 1f },
                    new SfxLayer { Wave = SfxWave.Square, Duration = 1.6f, Attack = 0.02f, Decay = 0f, Freq0 = 465f, Freq1 = 465f, Gain = 0.3f, Cutoff0 = 2500f, Cutoff1 = 2500f, Tremolo = 2.5f, TremoloDepth = 1f },
                };

            }
            return Array.Empty<SfxLayer>();
        }

        /// <summary>A gunshot: a bright crack, a darker body and a low thump, scaled by <paramref name="weight"/>.</summary>
        private static SfxLayer[] Shot(float length, float brightness, float thump, float weight, float thumpEnd) => new[]
        {
            SfxLayer.Noise(0.05f, 0.012f, 0.9f, brightness * 2.5f, brightness),
            new SfxLayer { Wave = SfxWave.Noise, Duration = length, Attack = 0.001f, Decay = length * 0.22f * weight, Gain = 1f, Cutoff0 = brightness, Cutoff1 = 250f, Drive = 1.8f },
            SfxLayer.Tone(SfxWave.Sine, length * 0.6f, thump, thumpEnd * 0.5f, length * 0.15f * weight, 0.8f * weight),
        };
    }
}
