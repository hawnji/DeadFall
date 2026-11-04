using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// The Gun Dispenser's body on every peer: a vending cabinet with a window where weapons cycle while it
    /// dispenses, the result waits for its buyer, and a beacon light above it. State comes from the
    /// <see cref="GunDispenserDirector"/> (host-authoritative); pressing F asks it to act.
    /// </summary>
    public sealed class GunDispenser : MonoBehaviour, IInteractable
    {
        public enum Mode : byte { Hidden, Idle, Spinning, Offering, Leaving }

        private Transform _pivot;
        private GameObject _shown;
        private WeaponDefinition _shownDef;
        private Material _window;
        private Light _beacon;
        private float _spinTimer;
        private int _spinIndex;

        public Mode State { get; private set; } = Mode.Hidden;
        public WeaponDefinition Offer { get; private set; }
        public WeaponMods OfferMods { get; private set; }
        public ulong Buyer { get; private set; }
        public float TimeLeft { get; private set; }

        public static GunDispenser Create(Transform parent)
        {
            var root = new GameObject("Gun Dispenser");
            root.transform.SetParent(parent, false);
            var g = root.AddComponent<GunDispenser>();
            Part(root.transform, new Vector3(0f, 1.1f, 0f), new Vector3(1.5f, 2.2f, 0.9f), new Color(0.18f, 0.2f, 0.24f), true);     // cabinet
            Part(root.transform, new Vector3(0f, 2.28f, 0f), new Vector3(1.6f, 0.16f, 1f), new Color(0.85f, 0.55f, 0.1f), false);    // roof band
            Renderer window = Part(root.transform, new Vector3(0f, 1.35f, 0.46f), new Vector3(1.2f, 0.8f, 0.02f), new Color(0.2f, 0.5f, 0.7f), false);
            g._window = RuntimeMaterials.CreateEmissive(new Color(0.25f, 0.6f, 0.9f), 0.6f);
            window.sharedMaterial = g._window;
            Part(root.transform, new Vector3(0f, 0.55f, 0.47f), new Vector3(0.9f, 0.18f, 0.04f), new Color(0.08f, 0.08f, 0.08f), false); // slot
            g._pivot = new GameObject("Display").transform;
            g._pivot.SetParent(root.transform, false);
            g._pivot.localPosition = new Vector3(0f, 1.35f, 0.75f);
            var beacon = new GameObject("Beacon");
            beacon.transform.SetParent(root.transform, false);
            beacon.transform.localPosition = new Vector3(0f, 3f, 0.4f);
            g._beacon = beacon.AddComponent<Light>();
            g._beacon.type = LightType.Point;
            g._beacon.range = 7f;
            g._beacon.intensity = 1.6f;
            g._beacon.color = new Color(1f, 0.7f, 0.2f);
            InteractGlow.Add(root, InteractGlow.Guns, () => g.State == Mode.Idle || g.State == Mode.Offering);
            root.SetActive(false);
            return g;
        }

        private static Renderer Part(Transform parent, Vector3 local, Vector3 size, Color color, bool solid)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(color);
            if (!solid) Destroy(go.GetComponent<Collider>());
            return go.GetComponent<Renderer>();
        }

        public void Apply(Mode mode, Vector3 position, float yaw, ulong buyer, WeaponDefinition offer, WeaponMods mods, float timeLeft)
        {
            bool moved = (transform.position - position).sqrMagnitude > 0.01f;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            State = mode;
            Buyer = buyer;
            Offer = offer;
            OfferMods = mods;
            TimeLeft = timeLeft;
            gameObject.SetActive(mode != Mode.Hidden);
            if (mode == Mode.Offering) Show(offer);
            else if (mode != Mode.Spinning || moved) Show(null);
            Color c = mode == Mode.Leaving ? new Color(1f, 0.2f, 0.15f)
                : mode == Mode.Offering && mods != null ? SupplyCacheMenu.TierColor(mods.Tier)
                : new Color(0.25f, 0.6f, 0.9f);
            RuntimeMaterials.SetColor(_window, c);
            if (_beacon != null) _beacon.color = mode == Mode.Leaving ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.7f, 0.2f);
        }

        private void Show(WeaponDefinition def)
        {
            if (def == _shownDef) return;
            if (_shown != null) Destroy(_shown);
            _shown = null;
            _shownDef = def;
            if (def == null) return;
            GameObject model = ModelLibrary.Spawn(def.ContentId.Replace('.', '_'), _pivot, slot => ModelLibrary.SlotFromAccent(slot, def.PlaceholderColor));
            if (model != null)
            {
                MeshFilter mf = model.GetComponentInChildren<MeshFilter>();
                Vector3 centre = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero;
                const float scale = 1.4f;
                model.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = -centre * scale;
                foreach (Collider col in model.GetComponentsInChildren<Collider>()) Destroy(col);
                _shown = model;
                return;
            }
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(_pivot, false);
            cube.transform.localScale = def.Kind == WeaponKind.Melee ? new Vector3(0.08f, 0.08f, 0.9f) : new Vector3(0.1f, 0.18f, 0.75f);
            cube.GetComponent<Renderer>().sharedMaterial = RuntimeMaterials.CreateLit(def.PlaceholderColor);
            Destroy(cube.GetComponent<Collider>());
            _shown = cube;
        }

        private void Update()
        {
            if (TimeLeft > 0f) TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
            if (State == Mode.Spinning)
            {
                // Cycle the catalog, slowing down towards the end of the spin.
                float interval = Mathf.Lerp(0.3f, 0.07f, Mathf.Clamp01(TimeLeft / GunDispenserRules.SpinSeconds));
                _spinTimer -= Time.deltaTime;
                if (_spinTimer <= 0f && WeaponCatalog.All.Count > 0)
                {
                    _spinTimer = interval;
                    _spinIndex = (_spinIndex + 1) % WeaponCatalog.All.Count;
                    Show(WeaponCatalog.All[_spinIndex]);
                }
            }
            if (_pivot != null) _pivot.localRotation = Quaternion.Euler(0f, Time.time * (State == Mode.Spinning ? 360f : 60f), 0f);
            if (_beacon != null) _beacon.intensity = State == Mode.Idle ? 1.2f + Mathf.Sin(Time.time * 2.5f) * 0.5f : 1.6f;
        }

        public string InteractPrompt(GameObject player)
        {
            switch (State)
            {
                case Mode.Idle: return $"Use Gun Dispenser ({GunDispenserRules.Price} credits)";
                case Mode.Spinning: return "Dispensing...";
                case Mode.Leaving: return "The Gun Dispenser is moving";
                case Mode.Offering:
                    if (Offer == null) return string.Empty;
                    string extra = OfferMods != null && OfferMods.Describe().Length > 0 ? $" ({OfferMods.Describe()})" : string.Empty;
                    return IsMine(player) ? $"Take {Offer.DisplayName}{extra} ({TimeLeft:0} s)" : $"{Offer.DisplayName}{extra}: another player's pull";
            }
            return string.Empty;
        }

        public bool CanInteract(GameObject player) => State == Mode.Idle || (State == Mode.Offering && IsMine(player));

        public void Interact(GameObject player) => GunDispenserDirector.Current?.RequestUse();

        private bool IsMine(GameObject player)
        {
            PlayerIdentity id = PlayerIdentity.Of(player);
            return id != null && id.ClientId == Buyer;
        }
    }
}
