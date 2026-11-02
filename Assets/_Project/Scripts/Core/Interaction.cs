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
