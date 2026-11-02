using Deadfall.Core;
using Deadfall.Player;
using Deadfall.Rules.Engineering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadfall.Game
{
    /// <summary>
    /// The local player's build controls (M9). B enters build mode: 1-4 or the wheel pick Sentry, Drone, Arc Fence or
    /// Barricade, R turns the ghost, left click places, right click or B leaves. The ghost is green when the spot is
    /// valid and red with the reason when it isn't. Outside build mode, looking at a structure: U upgrades it,
    /// X dismantles your own, and Z switches your drone between attack and repair.
    /// </summary>
    public sealed class BuildTool : MonoBehaviour
    {
        private const float RayDistance = 8f;

        private PlayerInputReader _input;
        private PlayerLook _look;
        private Transform _player;
        private InputActionMap _map;
        private InputAction _toggle, _place, _cancel, _rotate, _scroll, _upgrade, _dismantle, _droneMode;
        private InputAction[] _slots;
        private GameObject _ghost;
        private Material _ghostMat;
        private StructureKind _ghostKind = (StructureKind)255;
        private float _yawOffset;
        private Vector3 _spot;
        private bool _hasSpot;

        public bool Active { get; private set; }
        public StructureKind Selected { get; private set; } = StructureKind.Sentry;
        public PlacementError LastError { get; private set; }
        /// <summary>The structure under the crosshair (outside build mode), for the HUD.</summary>
        public Structure LookedAt { get; private set; }

        public void Bind(GameObject player)
        {
            _player = player != null ? player.transform : null;
            _input = player != null ? player.GetComponent<PlayerInputReader>() : null;
            _look = player != null ? player.GetComponent<PlayerLook>() : null;
        }

        private void Awake()
        {
            _map = new InputActionMap("Build");
            _toggle = _map.AddAction("BuildMode", InputActionType.Button, "<Keyboard>/b");
            _place = _map.AddAction("Place", InputActionType.Button, "<Mouse>/leftButton");
            _cancel = _map.AddAction("Cancel", InputActionType.Button, "<Mouse>/rightButton");
            _rotate = _map.AddAction("Rotate", InputActionType.Button, "<Keyboard>/r");
            _scroll = _map.AddAction("Cycle", InputActionType.Value, "<Mouse>/scroll/y");
            _upgrade = _map.AddAction("Upgrade", InputActionType.Button, "<Keyboard>/u");
            _dismantle = _map.AddAction("Dismantle", InputActionType.Button, "<Keyboard>/x");
            _droneMode = _map.AddAction("DroneMode", InputActionType.Button, "<Keyboard>/z");
            _slots = new InputAction[4];
            for (int i = 0; i < 4; i++) _slots[i] = _map.AddAction("Pick" + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 1));
            _map.Enable();
        }

        private void OnDestroy()
        {
            _map?.Dispose();
            if (_ghost != null) Destroy(_ghost);
        }

        private void Update()
        {
            EngineeringDirector d = EngineeringDirector.Current;
            bool blocked = d == null || _player == null || _input == null || !_input.GameplayEnabled || UiModal.Active;
            if (blocked)
            {
                if (Active) SetActive(false);
                LookedAt = null;
                return;
            }

            if (_toggle.WasPressedThisFrame()) SetActive(!Active);
            if (!Active)
            {
                LookedAt = LookForStructure();
                if (LookedAt != null)
                {
                    if (_upgrade.WasPressedThisFrame()) d.RequestAction(LookedAt, StructureAction.Upgrade);
                    if (_dismantle.WasPressedThisFrame() && d.IsMine(LookedAt)) d.RequestAction(LookedAt, StructureAction.Dismantle);
                }
                if (_droneMode.WasPressedThisFrame()) d.ToggleMyDrone();
                return;
            }

            for (int i = 0; i < _slots.Length; i++) if (_slots[i].WasPressedThisFrame()) Selected = (StructureKind)i;
            float wheel = _scroll.ReadValue<float>();
            if (wheel > 0.01f) Selected = (StructureKind)(((int)Selected + 3) % 4);
            else if (wheel < -0.01f) Selected = (StructureKind)(((int)Selected + 1) % 4);
            if (_rotate.WasPressedThisFrame()) _yawOffset = Mathf.Repeat(_yawOffset + 90f, 360f);
            if (_cancel.WasPressedThisFrame())
            {
                SetActive(false);
                return;
            }

            float yaw = (_look != null ? _look.Yaw : _player.eulerAngles.y) + _yawOffset;
            UpdateGhost(d, yaw);
            if (_place.WasPressedThisFrame())
            {
                if (LastError == PlacementError.Ok) d.RequestBuild(Selected, _spot, yaw);
                else RunToasts.Show(PlacementRules.Explain(LastError), new Color(1f, 0.6f, 0.5f), 1.5f);
            }
        }

        private void SetActive(bool on)
        {
            Active = on;
            if (_input != null) _input.WeaponsBlocked = on;
            if (!on && _ghost != null) _ghost.SetActive(false);
        }

        private void UpdateGhost(EngineeringDirector d, float yaw)
        {
            Camera cam = _look != null ? _look.Camera : Camera.main;
            if (Selected == StructureKind.Drone)
            {
                if (_ghost != null) _ghost.SetActive(false);
                _spot = _player.position;
                LastError = d.Check(Selected, _spot, yaw, d.LocalId, _player.position);
                return;
            }
            RaycastHit hit = default;
            _hasSpot = cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, RayDistance, ~0, QueryTriggerInteraction.Ignore)
                       && !hit.collider.transform.IsChildOf(_player);
            if (!_hasSpot)
            {
                if (_ghost != null) _ghost.SetActive(false);
                LastError = PlacementError.TooFar;
                return;
            }
            _spot = hit.point;
            LastError = d.Check(Selected, _spot, yaw, d.LocalId, _player.position);
            EnsureGhost();
            _ghost.SetActive(true);
            Vector3 half = PlacementProbe.HalfExtents(Selected);
            _ghost.transform.SetPositionAndRotation(_spot + Vector3.up * half.y, Quaternion.Euler(0f, yaw, 0f));
            RuntimeMaterials.SetColor(_ghostMat, LastError == PlacementError.Ok ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 0.3f, 0.25f));
        }

        private void EnsureGhost()
        {
            if (_ghost != null && _ghostKind == Selected) return;
            if (_ghost != null) Destroy(_ghost);
            _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(_ghost.GetComponent<Collider>()); // immediate: the placement overlap check runs this frame
            _ghost.name = "BuildGhost";
            _ghost.transform.localScale = PlacementProbe.HalfExtents(Selected) * 2f;
            if (_ghostMat == null) _ghostMat = RuntimeMaterials.CreateLit(Color.green);
            _ghost.GetComponent<Renderer>().sharedMaterial = _ghostMat;
            _ghostKind = Selected;
        }

        private Structure LookForStructure()
        {
            Camera cam = _look != null ? _look.Camera : Camera.main;
            if (cam == null) return null;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore)) return null;
            return hit.collider.GetComponentInParent<Structure>();
        }
    }
}
