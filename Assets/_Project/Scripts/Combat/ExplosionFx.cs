using Deadfall.Rules.Common;
using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// Explosion visuals and sound: fireball, rolling smoke, sparks, debris, a light flash and a scorch mark
    /// (<see cref="FxParticles.Explosion"/>). Visual only; damage is handled by <see cref="Explosions"/>.
    /// </summary>
    public sealed class ExplosionFx : MonoBehaviour
    {
        public static void Spawn(Vector3 position, float radius)
        {
            GameAudio.Play(SfxId.Explosion, position, Mathf.Clamp(radius / 4f, 0.5f, 1.3f), radius > 5f ? 0.8f : 1f);
            FxParticles.Explosion(position, radius);
        }
    }
}
