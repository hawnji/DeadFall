using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Something the player can use with the Interact key (F): pickups, Supply Caches, terminals, objective
    /// items, structures. Found by <c>PlayerInteractor</c> with a short ray from the camera.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>HUD text such as "Take ammo" or "Supply Cache (250 credits)". Empty hides the prompt.</summary>
        string InteractPrompt(GameObject player);
        bool CanInteract(GameObject player);
        void Interact(GameObject player);
    }
}

namespace Deadfall.Core
{
    /// <summary>An interactable that repeats while Interact is held (one use per <see cref="RepeatSeconds"/>).</summary>
    public interface IHoldInteractable : IInteractable
    {
        float RepeatSeconds { get; }
    }

    /// <summary>
    /// An interactable that also works from nearby without aiming at it (barricades). <c>PlayerInteractor</c> uses
    /// the closest one in reach when nothing is under the crosshair.
    /// </summary>
    public interface INearbyInteractable : IInteractable
    {
        /// <summary>True when a player standing at <paramref name="feet"/> can use it; <paramref name="distance"/> ranks candidates.</summary>
        bool InReach(Vector3 feet, out float distance);
    }

    /// <summary>Every live <see cref="INearbyInteractable"/> (they register themselves).</summary>
    public static class NearbyInteractables
    {
        private static readonly System.Collections.Generic.List<INearbyInteractable> Items = new System.Collections.Generic.List<INearbyInteractable>();

        public static System.Collections.Generic.IReadOnlyList<INearbyInteractable> All => Items;
        public static void Register(INearbyInteractable i) { if (i != null && !Items.Contains(i)) Items.Add(i); }
        public static void Unregister(INearbyInteractable i) => Items.Remove(i);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Items.Clear();
    }
}
