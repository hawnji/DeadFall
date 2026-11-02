using Deadfall.Classes;
using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.Net;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Networked launcher projectiles (T-38). Offline and on the host the grenade is real. A client sends
    /// "df.proj" (weapon, origin, velocity, damage multiplier) and flies a harmless copy; the host checks it, fires
    /// the real one from the client's body (kills credit the client, their own blast hurts them through their
    /// proxy) and tells the other clients ("df.projsp") to show a copy.
    /// </summary>
    public sealed class ProjectileNetwork : MonoBehaviour, IProjectileService
    {
        private const string MsgRequest = "df.proj";
        private const string MsgSpawn = "df.projsp";
        private const float MaxClaimedMultiplier = 3f;

        private SessionService _session;

        private void Awake()
        {
            _session = SessionService.Instance;
            _session.RegisterHandler(MsgRequest, OnRequest);
            _session.RegisterHandler(MsgSpawn, OnSpawn);
            ServiceRegistry.Register<IProjectileService>(this);
            Grenade.Exploded += OnExploded;
        }

        private void OnDestroy()
        {
            Grenade.Exploded -= OnExploded;
            if (ServiceRegistry.TryGet(out IProjectileService s) && ReferenceEquals(s, this))
                ServiceRegistry.Register<IProjectileService>(new LocalProjectileService());
            if (_session == null) return;
            _session.UnregisterHandler(MsgRequest);
            _session.UnregisterHandler(MsgSpawn);
        }

        private static void OnExploded(Vector3 p, float radius) => ExplosionFx.Spawn(p, radius);

        public void Launch(GameObject owner, string weaponId, Vector3 origin, Vector3 velocity, float damage, float radius)
        {
            WeaponDefinition def = WeaponCatalog.ById(weaponId);
            int index = WeaponCatalog.IndexOf(def);
            ulong me = _session.LocalClientId;
            if (_session.IsConnectedClient)
            {
                Grenade.Spawn(origin, velocity, 0f, radius, 0f, owner, weaponId, authoritative: false);
                if (index < 0) return;
                using (var w = new FastBufferWriter(48, Allocator.Temp))
                {
                    w.WriteValueSafe((byte)index);
                    w.WriteValueSafe(origin);
                    w.WriteValueSafe(velocity);
                    float baseDamage = def.Firearm.Damage;
                    w.WriteValueSafe(baseDamage > 0f ? damage / baseDamage : 1f);
                    _session.SendToServer(MsgRequest, w, NetworkDelivery.ReliableSequenced);
                }
                return;
            }
            Grenade.Spawn(origin, velocity, damage, radius, ClassNetwork.SelfBlastMultiplier(me), owner, weaponId, authoritative: true);
            if (_session.IsHost && index >= 0) Broadcast(index, origin, velocity, ulong.MaxValue);
        }

        private void Broadcast(int index, Vector3 origin, Vector3 velocity, ulong except)
        {
            using (var w = new FastBufferWriter(32, Allocator.Temp))
            {
                w.WriteValueSafe((byte)index);
                w.WriteValueSafe(origin);
                w.WriteValueSafe(velocity);
                _session.SendToRemoteClients(MsgSpawn, w, NetworkDelivery.ReliableSequenced, except);
            }
        }

        private void OnRequest(ulong sender, FastBufferReader r)
        {
            if (!_session.IsHost) return;
            r.ReadValueSafe(out byte index);
            r.ReadValueSafe(out Vector3 origin);
            r.ReadValueSafe(out Vector3 velocity);
            r.ReadValueSafe(out float mult);
            WeaponDefinition def = WeaponCatalog.At(index);
            PlayerIdentity who = PlayerIdentity.Find(sender);
            if (def == null || def.Kind != WeaponKind.Firearm || !def.Firearm.IsProjectile || who == null) return;
            if (float.IsNaN(mult) || float.IsNaN(origin.x) || float.IsNaN(velocity.x)) return;
            if ((origin - (who.transform.position + Vector3.up * 1.6f)).sqrMagnitude > 3f * 3f) return;
            float speed = def.Firearm.ProjectileSpeed;
            if (velocity.sqrMagnitude > speed * speed * 1.1f) velocity = velocity.normalized * speed;
            float damage = def.Firearm.Damage * Mathf.Clamp(mult, 0f, MaxClaimedMultiplier);
            Grenade.Spawn(origin, velocity, damage, def.Firearm.ExplosionRadius, ClassNetwork.SelfBlastMultiplier(sender), who.gameObject, def.ContentId, authoritative: true);
            Broadcast(index, origin, velocity, sender);
        }

        private void OnSpawn(ulong sender, FastBufferReader r)
        {
            if (_session.IsHost) return;
            r.ReadValueSafe(out byte index);
            r.ReadValueSafe(out Vector3 origin);
            r.ReadValueSafe(out Vector3 velocity);
            WeaponDefinition def = WeaponCatalog.At(index);
            if (def == null) return;
            Grenade.Spawn(origin, velocity, 0f, def.Firearm.ExplosionRadius, 0f, null, def.ContentId, authoritative: false);
        }
    }
}
