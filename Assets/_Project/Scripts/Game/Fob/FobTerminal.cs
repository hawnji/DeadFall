using Deadfall.Core;
using Deadfall.Rules.Progression;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>A facility console in FOB Halcyon. Interact (F) opens the FOB screen on that facility's tab.</summary>
    public sealed class FobTerminal : MonoBehaviour, IInteractable
    {
        public FacilityId Facility;

        public string InteractPrompt(GameObject player)
        {
            PlayerProfile p = ProfileService.Profile;
            int level = p != null ? p.OperatorLevel : 1;
            if (!Facilities.IsOpen(Facility, level)) return $"{Facilities.Name(Facility)} (opens at operator level {Facilities.UnlockLevel(Facility)})";
            return $"Use {Facilities.Name(Facility)}";
        }

        public bool CanInteract(GameObject player)
        {
            PlayerProfile p = ProfileService.Profile;
            return p != null && Facilities.IsOpen(Facility, p.OperatorLevel) && !UiModal.Active;
        }

        public void Interact(GameObject player) => FobPanel.Open(this);
    }
}
