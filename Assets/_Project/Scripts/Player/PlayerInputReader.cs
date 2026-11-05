using Deadfall.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deadfall.Player
{
    /// <summary>One frame of player intent, read once per frame and consumed by motor, look, and weapons.</summary>
    public struct PlayerInputFrame
    {
        public Vector2 Move;
        /// <summary>Raw mouse delta in counts this frame. Never scaled by deltaTime.</summary>
        public Vector2 Look;
        public bool SprintHeld;
        public bool JumpPressed;
        public bool CrouchHeld;
        public bool FirePressed;
        public bool FireHeld;
        public bool AimHeld;
        public bool ReloadPressed;
        public bool MeleePressed;
        public bool InteractPressed;
        public bool InteractHeld;
        public bool AbilityPressed;
        /// <summary>Throw a frag grenade (E).</summary>
        public bool GrenadePressed;
        /// <summary>-1, 0, or +1 from the scroll wheel.</summary>
        public int WeaponScroll;
        /// <summary>0-based slot from number keys, or -1.</summary>
        public int WeaponSlotPressed;
        public bool PausePressed;
    }

    /// <summary>
    /// Reads the Input System. Actions are defined in code so the project works without an .inputactions asset;
    /// keyboard and mouse keys come from <see cref="KeyBindings"/> (Settings › Keybinds) and the map is rebuilt when
    /// they change. Gamepad bindings are included so controller support is a tuning task, not a rewrite.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        private InputActionMap _map;
        private InputAction _move, _look, _sprint, _jump, _crouch, _fire, _aim, _reload, _melee, _interact, _ability, _grenade, _scroll, _next, _prev, _pause;
        private InputAction[] _slots;

        public PlayerInputFrame Current { get; private set; }

        /// <summary>When false (menus open, cursor free), gameplay input reads as idle.</summary>
        public bool GameplayEnabled { get; set; } = true;

        /// <summary>
        /// When true (Engineering build mode), weapon and interact input read as idle while movement and look still work;
        /// the build tool reads the mouse buttons, wheel and number keys itself.
        /// </summary>
        public bool WeaponsBlocked { get; set; }

        private ISettingsService _settings;
        private string _builtFor;

        private void Awake()
        {
            ServiceRegistry.TryGet(out _settings);
            Build();
            if (_settings != null) _settings.Changed += OnSettingsChanged;
        }

        private void OnSettingsChanged(GameSettings s)
        {
            if (Signature(s) != _builtFor) Build();
        }

        private static string Signature(GameSettings s) => s != null && s.KeyBindings != null ? string.Join("|", s.KeyBindings) : string.Empty;

        /// <summary>(Re)creates the action map from the current key bindings (Settings › Keybinds).</summary>
        private void Build()
        {
            GameSettings gs = _settings != null ? _settings.Current : null;
            _builtFor = Signature(gs);
            bool wasEnabled = _map == null || _map.enabled;
            _map?.Dispose();
            string Key(string id) => KeyBindings.Path(gs, id);
            bool Free(string path)
            {
                foreach (KeyBindings.Entry e in KeyBindings.All) if (Key(e.Id) == path) return false;
                return true;
            }

            _map = new InputActionMap("Player");

            _move = _map.AddAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", Key("MoveUp")).With("Down", Key("MoveDown"))
                .With("Left", Key("MoveLeft")).With("Right", Key("MoveRight"));
            _move.AddBinding("<Gamepad>/leftStick");

            _look = _map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            _sprint = _map.AddAction("Sprint", InputActionType.Button, Key("Sprint"));
            _sprint.AddBinding("<Gamepad>/leftStickPress");
            _jump = _map.AddAction("Jump", InputActionType.Button, Key("Jump"));
            _jump.AddBinding("<Gamepad>/buttonSouth");
            _crouch = _map.AddAction("Crouch", InputActionType.Button, Key("Crouch"));
            if (KeyBindings.IsDefault(gs, "Crouch") && Free("<Keyboard>/c")) _crouch.AddBinding("<Keyboard>/c");
            _crouch.AddBinding("<Gamepad>/buttonEast");
            _fire = _map.AddAction("Fire", InputActionType.Button, Key("Fire"));
            _fire.AddBinding("<Gamepad>/rightTrigger");
            _aim = _map.AddAction("Aim", InputActionType.Button, Key("Aim"));
            _aim.AddBinding("<Gamepad>/leftTrigger");
            _reload = _map.AddAction("Reload", InputActionType.Button, Key("Reload"));
            _reload.AddBinding("<Gamepad>/buttonWest");
            _melee = _map.AddAction("Melee", InputActionType.Button, Key("Melee"));
            if (KeyBindings.IsDefault(gs, "Melee") && Free("<Mouse>/middleButton")) _melee.AddBinding("<Mouse>/middleButton");
            _melee.AddBinding("<Gamepad>/rightStickPress");
            _interact = _map.AddAction("Interact", InputActionType.Button, Key("Interact"));
            _ability = _map.AddAction("Ability", InputActionType.Button, Key("Ability"));
            _ability.AddBinding("<Gamepad>/rightShoulder");
            _grenade = _map.AddAction("Grenade", InputActionType.Button, Key("Grenade"));
            _grenade.AddBinding("<Gamepad>/leftShoulder");
            _scroll = _map.AddAction("Scroll", InputActionType.Value, "<Mouse>/scroll/y");
            _next = _map.AddAction("NextWeapon", InputActionType.Button, "<Gamepad>/buttonNorth");
            _prev = _map.AddAction("PrevWeapon", InputActionType.Button);
            _pause = _map.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            _pause.AddBinding("<Gamepad>/start");

            _slots = new InputAction[4];
            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = _map.AddAction("Slot" + (i + 1), InputActionType.Button, "<Keyboard>/" + (i + 1));

            if (wasEnabled) _map.Enable();
        }

        private void OnEnable() => _map?.Enable();
        private void OnDisable() => _map?.Disable();

        private void OnDestroy()
        {
            if (_settings != null) _settings.Changed -= OnSettingsChanged;
            _map?.Dispose();
            _map = null;
        }

        private void Update()
        {
            // The settings service may register after the player is built.
            if (_settings == null && ServiceRegistry.TryGet(out _settings))
            {
                _settings.Changed += OnSettingsChanged;
                OnSettingsChanged(_settings.Current);
            }
            var f = new PlayerInputFrame { WeaponSlotPressed = -1, PausePressed = _pause.WasPressedThisFrame() };
            if (GameplayEnabled)
            {
                f.Move = Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);
                f.Look = _look.ReadValue<Vector2>();
                f.SprintHeld = _sprint.IsPressed();
                f.JumpPressed = _jump.WasPressedThisFrame();
                f.CrouchHeld = _crouch.IsPressed();
                f.FirePressed = _fire.WasPressedThisFrame();
                f.FireHeld = _fire.IsPressed();
                f.AimHeld = _aim.IsPressed();
                f.ReloadPressed = _reload.WasPressedThisFrame();
                f.MeleePressed = _melee.WasPressedThisFrame();
                f.InteractPressed = _interact.WasPressedThisFrame();
                f.InteractHeld = _interact.IsPressed();
                f.AbilityPressed = _ability.WasPressedThisFrame();
                f.GrenadePressed = _grenade.WasPressedThisFrame();

                float scroll = _scroll.ReadValue<float>();
                f.WeaponScroll = scroll > 0.01f ? -1 : scroll < -0.01f ? 1 : 0;
                if (_next.WasPressedThisFrame()) f.WeaponScroll = 1;
                if (_prev.WasPressedThisFrame()) f.WeaponScroll = -1;

                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].WasPressedThisFrame())
                    {
                        f.WeaponSlotPressed = i;
                        break;
                    }
                }
            }
            if (WeaponsBlocked)
            {
                f.FirePressed = f.FireHeld = f.AimHeld = f.ReloadPressed = f.MeleePressed = f.InteractPressed = f.InteractHeld = f.AbilityPressed = f.GrenadePressed = false;
                f.WeaponScroll = 0;
                f.WeaponSlotPressed = -1;
            }
            Current = f;
        }
    }
}
