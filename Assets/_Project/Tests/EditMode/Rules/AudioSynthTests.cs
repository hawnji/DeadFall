using System;
using Deadfall.Rules.Common;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    /// <summary>M13: every sound id has a recipe that renders to clean, bounded, deterministic samples.</summary>
    public class AudioSynthTests
    {
        [Test]
        public void EverySoundRendersCleanly()
        {
            foreach (SfxId id in SfxBank.All)
            {
                if (id == SfxId.None) continue;
                SfxLayer[] recipe = SfxBank.Recipe(id);
                Assert.IsNotEmpty(recipe, id.ToString());
                SfxInfo info = SfxBank.Info(id);
                float[] data = info.LoopSeconds > 0f ? SfxSynth.RenderLoop(recipe, 7, info.LoopSeconds) : SfxSynth.Render(recipe, 7);
                Assert.Greater(data.Length, SfxSynth.SampleRate / 100, id.ToString());
                float peak = 0f;
                foreach (float v in data)
                {
                    Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), id.ToString());
                    peak = Math.Max(peak, Math.Abs(v));
                }
                Assert.LessOrEqual(peak, SfxSynth.Peak + 1e-4f, id.ToString());
                Assert.Greater(peak, 0.1f, $"{id} is silent");
                Assert.Less(data.Length, SfxSynth.SampleRate * 13, $"{id} is too long");
            }
        }

        [Test]
        public void OneShotsEndQuietly()
        {
            foreach (SfxId id in SfxBank.All)
            {
                if (id == SfxId.None || SfxBank.Info(id).LoopSeconds > 0f) continue;
                float[] d = SfxSynth.Render(SfxBank.Recipe(id), 3);
                Assert.Less(Math.Abs(d[d.Length - 1]), 0.02f, $"{id} clicks at the end");
            }
        }

        [Test]
        public void RenderingIsDeterministicPerSeed()
        {
            float[] a = SfxSynth.Render(SfxBank.Recipe(SfxId.ShotRifle), 11);
            float[] b = SfxSynth.Render(SfxBank.Recipe(SfxId.ShotRifle), 11);
            float[] c = SfxSynth.Render(SfxBank.Recipe(SfxId.ShotRifle), 12);
            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreNotEqual(a, c);
        }

        [Test]
        public void LoopsRenderWithoutASeam()
        {
            var bed = new[] { new SfxLayer { Wave = SfxWave.Noise, Duration = 4f, Gain = 0.6f, Cutoff0 = 400f, Cutoff1 = 400f, Tremolo = 0.2f, TremoloDepth = 0.4f } };
            float[] d = SfxSynth.RenderLoop(bed, 5, 4f);
            Assert.AreEqual(4 * SfxSynth.SampleRate, d.Length);
            Assert.Less(Math.Abs(d[d.Length - 1] - d[0]), 0.08f);
        }

        [Test]
        public void PlaybackRulesAreSane()
        {
            foreach (SfxId id in SfxBank.All)
            {
                if (id == SfxId.None) continue;
                SfxInfo i = SfxBank.Info(id);
                Assert.GreaterOrEqual(i.MaxVoices, 1, id.ToString());
                Assert.GreaterOrEqual(i.Variants, 1, id.ToString());
                Assert.That(i.Volume > 0f && i.Volume <= 1f, id.ToString());
                Assert.That(i.PitchJitter >= 0f && i.PitchJitter < 0.3f, id.ToString());
                if (i.Spatial) Assert.Greater(i.MaxDistance, 5f, id.ToString());
            }
            Assert.AreEqual(SfxBus.Interface, SfxBank.Info(SfxId.UiClick).Bus);
            Assert.IsFalse(SfxBank.Info(SfxId.HitMarker).Spatial, "markers play in the player's ears");
        }
    }
}
