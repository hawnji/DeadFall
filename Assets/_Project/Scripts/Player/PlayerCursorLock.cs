using UnityEngine;

namespace Deadfall.Player
{
    /// <summary>Locks the cursor during play; Escape frees it and pauses gameplay input; Escape again resumes.</summary>
    public sealed class PlayerCursorLock : MonoBehaviour
    {
        private PlayerInputReader _input;
        private bool _locked;

        public bool IsLocked => _locked;

        private void Awake() => _input = GetComponent<PlayerInputReader>();

        private void OnEnable() => SetLocked(true);
        private void OnDisable() => SetLocked(false);

        private void Update()
        {
            if (_input == null) return;
            if (_input.Current.PausePressed) SetLocked(!_locked);
        }

        private void OnApplicationFocus(bool focus)
        {
            if (focus && _locked) SetLocked(true);
        }

        public void SetLocked(bool locked)
        {
            _locked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
            if (_input != null) _input.GameplayEnabled = locked;
        }
    }
}
