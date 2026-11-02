using System;
using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>A console that runs an action on Interact (FOB to range and back).</summary>
    public sealed class FobTeleporter : MonoBehaviour, IInteractable
    {
        public string Prompt = "Travel";
        public Action Action;

        public string InteractPrompt(GameObject player) => Prompt;
        public bool CanInteract(GameObject player) => Action != null && !UiModal.Active;
        public void Interact(GameObject player) => Action?.Invoke();
    }
}
