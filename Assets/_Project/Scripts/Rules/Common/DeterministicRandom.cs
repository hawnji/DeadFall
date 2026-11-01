using System;

namespace Deadfall.Rules.Common
{
    /// <summary>
    /// Seeded xoshiro128** generator. Produces the same sequence on every machine for the same seed,
    /// which map generation, loot rolls, and encounter planning rely on in multiplayer.
    /// Not thread-safe; give each system its own instance.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private uint _s0, _s1, _s2, _s3;

        public DeterministicRandom(ulong seed)
        {
            // SplitMix64 expands the seed into four non-zero state words.
            ulong x = seed;
            _s0 = (uint)SplitMix(ref x);
            _s1 = (uint)SplitMix(ref x);
            _s2 = (uint)SplitMix(ref x);
            _s3 = (uint)SplitMix(ref x);
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
        }

        private static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

        public uint NextUInt()
        {
            uint result = Rotl(_s1 * 5, 7) * 9;
            uint t = _s1 << 9;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 11);
            return result;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % span);
        }

        /// <summary>Uniform point inside the unit disc, used for cone spread.</summary>
        public void InsideUnitDisc(out float x, out float y)
        {
            float angle = NextFloat() * (float)(Math.PI * 2.0);
            float radius = (float)Math.Sqrt(NextFloat());
            x = radius * (float)Math.Cos(angle);
            y = radius * (float)Math.Sin(angle);
        }
    }
}
