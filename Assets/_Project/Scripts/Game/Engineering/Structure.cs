using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Rules.Combat;
using Deadfall.Rules.Engineering;
using UnityEngine;

namespace Deadfall.Game
{
    public enum StructureAction : byte { Repair, Refill, Upgrade, Dismantle, ToggleMode }

    /// <summary>
    /// Base for Engineering structures. The host's copy is authoritative (health, supply, damage dealt); clients
    /// keep mirrors updated from <see cref="EngineeringDirector"/>'s state and only animate. Solid parts sit on the
    /// Ignore Raycast layer so player bullets pass through, but they still block movement. F repairs or refills.
    /// </summary>
    public abstract class Structure : MonoBehaviour, IDamageable, IInteractable
    {
        protected const int IgnoreRaycastLayer = 2;

        private Material _healthMat;
        private Color _baseColor;
        private Material _accentMat;
        private readonly List<Renderer> _placeholders = new List<Renderer>();
        private bool _dressed;

        public ushort Id { get; private set; }
        public StructureKind Kind { get; private set; }
        public ulong OwnerClientId { get; private set; }
        public bool OwnerIsEngineer { get; private set; }
        public int Tier { get; private set; } = 1;
        public bool Mirror { get; private set; }
        public float Health { get; protected set; }
        public float MaxHealth { get; private set; }
        public float Supply { get; protected set; }
        public float SupplyMax { get; private set; }
        public DroneMode Mode { get; set; }
        /// <summary>Fed by the map's power grid (always true without one). Unpowered fences don't shock and sentries don't fire.</summary>
        public bool Powered { get; set; } = true;

        public StructureDef Def => StructureCatalog.Of(Kind);
        protected StructureTier Stats => Def.Tier(Tier);
        public bool IsAlive => Health > 0f;
        public virtual float DamageReduction => 0f;
        public float Health01 => MaxHealth > 0f ? Mathf.Clamp01(Health / MaxHealth) : 0f;
        public float Supply01 => SupplyMax > 0f ? Mathf.Clamp01(Supply / SupplyMax) : 0f;
        /// <summary>The owner's player object on this machine (null if they left).</summary>
        protected GameObject OwnerObject
        {
            get
            {
                PlayerIdentity p = PlayerIdentity.Find(OwnerClientId);
                return p != null ? p.gameObject : null;
            }
        }

        public void Init(ushort id, StructureKind kind, ulong owner, bool ownerIsEngineer, bool mirror)
        {
            Id = id;
            Kind = kind;
            OwnerClientId = owner;
            OwnerIsEngineer = ownerIsEngineer;
            Mirror = mirror;
            Build();
            SetTier(1, true);
            InteractGlow.Add(gameObject, InteractGlow.Objectives, () => CanInteract(null));
        }

        /// <summary>Creates the placeholder visuals and colliders.</summary>
        protected abstract void Build();

        public void SetTier(int tier, bool refill)
        {
            float health01 = MaxHealth > 0f ? Health01 : 1f;
            Tier = Mathf.Clamp(tier, 1, Def.MaxTier);
            MaxHealth = Stats.Health;
            SupplyMax = Stats.Supply;
            Health = refill ? MaxHealth : Mathf.Max(1f, health01 * MaxHealth);
            if (refill) Supply = SupplyMax;
            OnTierChanged();
            RefreshTint();
        }

        protected virtual void OnTierChanged() { }

        /// <summary>Clients: values from the host.</summary>
        public void ApplySynced(int tier, float health01, float supply01, DroneMode mode)
        {
            if (tier != Tier) SetTier(tier, false);
            Health = Mathf.Max(0.01f, health01 * MaxHealth);
            Supply = supply01 * SupplyMax;
            Mode = mode;
            RefreshTint();
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || !IsAlive) return;
            Health = Mathf.Min(MaxHealth, Health + amount);
            RefreshTint();
        }

        public void Refill() => Supply = SupplyMax;

        public float ApplyDamage(in DamageInfo info)
        {
            if (Mirror || !IsAlive || info.Amount <= 0f) return 0f;
            float applied = Mathf.Min(Health, info.Amount * (1f - DamageReduction));
            Health -= applied;
            RefreshTint();
            EngineeringDirector.Current?.MarkDirty();
            if (Health <= 0f) EngineeringDirector.Current?.DestroyStructure(this, true);
            return applied;
        }

        private void Update()
        {
            if (!Mirror) TickHost(Time.deltaTime);
            TickVisual(Time.deltaTime);
        }

        /// <summary>Authoritative behaviour (offline or host).</summary>
        protected virtual void TickHost(float dt) { }

        /// <summary>Animation on every machine.</summary>
        protected virtual void TickVisual(float dt) { }

        // ---------------------------------------------------------------- interaction

        public string InteractPrompt(GameObject player)
        {
            EngineeringDirector d = EngineeringDirector.Current;
            if (d == null) return string.Empty;
            string head = $"{Def.Name} T{Tier}  {Mathf.CeilToInt(Health)}/{Mathf.CeilToInt(MaxHealth)}";
            if (SupplyMax > 0f) head += Kind == StructureKind.Sentry ? $"  ammo {Mathf.CeilToInt(Supply)}" : $"  charge {Supply01 * 100f:0}%";
            if (!Powered) head += "  ·  NO POWER (restart a grid generator)";
            string primary = PrimaryAction(d, out int cost) switch
            {
                StructureAction.Repair => $"F repair ({cost} Scrap)",
                StructureAction.Refill => $"F refill ({cost} Scrap)",
                _ => string.Empty,
            };
            int up = d.UpgradeCostFor(this);
            string more = up >= 0 ? $"U upgrade ({up})" : "max tier";
            if (d.IsMine(this)) more += "  ·  X dismantle";
            return primary.Length > 0 ? $"{head}\n{primary}  ·  {more}" : $"{head}\n{more}";
        }

        /// <summary>Repair if damaged, else refill if it uses supply and isn't full; -1 cost means nothing to do.</summary>
        public StructureAction? PrimaryAction(EngineeringDirector d, out int cost)
        {
            bool engineer = d.LocalIsEngineer;
            float amount = RepairRules.Amount(Health, MaxHealth, engineer);
            if (amount > 0.5f)
            {
                cost = d.Free ? 0 : RepairRules.Cost(amount, engineer);
                return StructureAction.Repair;
            }
            if (SupplyMax > 0f && Supply < SupplyMax - 0.5f)
            {
                cost = d.Free ? 0 : Def.RefillCost;
                return StructureAction.Refill;
            }
            cost = -1;
            return null;
        }

        public bool CanInteract(GameObject player)
        {
            EngineeringDirector d = EngineeringDirector.Current;
            return d != null && PrimaryAction(d, out _) != null;
        }

        public void Interact(GameObject player)
        {
            EngineeringDirector d = EngineeringDirector.Current;
            StructureAction? a = d != null ? PrimaryAction(d, out _) : null;
            if (a.HasValue) d.RequestAction(this, a.Value);
        }

        // ---------------------------------------------------------------- visuals

        protected GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color, bool solid, bool tintWithHealth = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.layer = IgnoreRaycastLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Material m = RuntimeMaterials.CreateLit(color);
            go.GetComponent<Renderer>().sharedMaterial = m;
            _placeholders.Add(go.GetComponent<Renderer>());
            if (!solid) Destroy(go.GetComponent<Collider>());
            if (tintWithHealth)
            {
                _healthMat = m;
                _baseColor = color;
            }
            return go;
        }

        /// <summary>
        /// Puts Blender model <paramref name="id"/> under <paramref name="parent"/> at <paramref name="localPos"/>. Its
        /// "Accent" parts share this structure's own material, which takes the health/tier tint (call after the
        /// tintWithHealth <see cref="Part"/>). Returns null when the model isn't available (placeholders stay).
        /// </summary>
        protected Transform Dress(string id, Transform parent, Vector3 localPos)
        {
            GameObject model = ModelLibrary.Spawn(id, parent);
            if (model == null) return null;
            model.transform.localPosition = localPos;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].name != "Model Accent") continue;
                    if (_accentMat == null)
                    {
                        _accentMat = RuntimeMaterials.CreateLit(_baseColor);
                        _healthMat = _accentMat;
                    }
                    mats[i] = _accentMat;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
                r.gameObject.layer = IgnoreRaycastLayer;
            }
            _dressed = true;
            return model.transform;
        }

        /// <summary>Once a model is dressed on, hides the placeholder primitives (their colliders stay), except <paramref name="keep"/>.</summary>
        protected void HidePlaceholders(params GameObject[] keep)
        {
            if (!_dressed) return;
            foreach (Renderer r in _placeholders)
            {
                if (r == null || System.Array.IndexOf(keep, r.gameObject) >= 0) continue;
                r.enabled = false;
            }
        }

        private void OnDestroyMaterials()
        {
            if (_accentMat != null) Destroy(_accentMat);
        }

        private void RefreshTint()
        {
            if (_healthMat == null) return;
            Color tierColor = Tier >= 3 ? Color.Lerp(_baseColor, new Color(1f, 0.85f, 0.3f), 0.45f)
                : Tier == 2 ? Color.Lerp(_baseColor, new Color(0.5f, 0.75f, 1f), 0.4f) : _baseColor;
            RuntimeMaterials.SetColor(_healthMat, Color.Lerp(new Color(0.55f, 0.15f, 0.12f), tierColor, Health01));
        }

        protected virtual void OnDestroy() => OnDestroyMaterials();
    }
}
