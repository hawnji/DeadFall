using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Something live to show behind the title menu (attract mode). The title screen calls <see cref="Begin"/> when it
    /// opens, <see cref="Place"/> every frame to move its camera, and <see cref="End"/> when the player picks an option.
    /// </summary>
    public interface ITitleBackdrop
    {
        /// <summary>Sets the scene up; false when it can't (the title then falls back to its FOB drift).</summary>
        bool Begin();
        /// <summary>Moves the title camera; <paramref name="dt"/> is unscaled seconds since the last call.</summary>
        void Place(Camera camera, float dt);
        /// <summary>Tears the scene down again.</summary>
        void End();
    }
}
