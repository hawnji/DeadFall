using System.Collections.Generic;
using Deadfall.Combat;
using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>
    /// Builds a complete local player from code. Used by sandboxes until the player prefab exists
    /// (the prefab will have the same components, so nothing else changes).
    /// </summary>
    public static class PlayerFactory
    {
        /// <summary>Built-in "Ignore Raycast" layer: keeps the player's own capsule out of their bullets.</summary>
        public const int PlayerLayer = 2;

        public static GameObject CreateLocalPlayer(Vector3 position, float yaw, IList<WeaponDefinition> loadout)
        {
            var root = new GameObject("Player");
            root.SetActive(false);
            root.layer = PlayerLayer;
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            root.AddComponent<CharacterController>();

            var pivot = new GameObject("CameraPivot");
            pivot.layer = PlayerLayer;
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.68f, 0f);

            var camGo = new GameObject("PlayerCamera");
            camGo.layer = PlayerLayer;
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(pivot.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 300f;
            camGo.AddComponent<AudioListener>();

            var vm = new GameObject("Viewmodel");
            vm.layer = PlayerLayer;
            vm.transform.SetParent(camGo.transform, false);
            vm.AddComponent<WeaponViewmodel>();

            root.AddComponent<PlayerIdentity>();
            root.AddComponent<PlayerStatus>();
            root.AddComponent<PlayerInputReader>();
            root.AddComponent<PlayerLook>();
            root.AddComponent<PlayerMotor>();
            root.AddComponent<PlayerHealth>();
            var weapons = root.AddComponent<PlayerWeapons>();
            weapons.SetLoadout(loadout);
            root.AddComponent<PlayerCursorLock>();

            root.SetActive(true);
            return root;
        }
    }
}
