using System.Collections.Generic;
using Deadfall.Combat;
using Deadfall.Rules.Classes;
using UnityEngine;

namespace Deadfall.Classes
{
    /// <summary>
    /// Host-side Squad Doctrine effects that touch damage (GAME_DESIGN §5.1):
    /// Covering Fire (Gunner + Tank): enemies the Gunner hit recently deal less damage to the Tank.
    /// Overwatch (Gunner + Engineer): enemies a sentry (ability or scrap-built) or drone hit recently take bonus damage from the Gunner.
    /// Runs wherever damage is applied for real (host or offline); mirrors never apply damage.
    /// </summary>
    public static class DoctrineEffects
    {
        public const string GunnerClassId = "class.gunner";
        public const string TankClassId = "class.tank";
        /// <summary>Seconds a Gunner hit keeps an enemy suppressed.</summary>
        public const float CoveringFireSeconds = 4f;
        /// <summary>Damage the Tank takes from a suppressed enemy (0.7 = 30% less).</summary>
        public const float CoveringFireMultiplier = 0.7f;
        /// <summary>Seconds a sentry hit keeps an enemy marked.</summary>
        public const float OverwatchSeconds = 5f;
        /// <summary>Gunner damage against marked enemies (1.2 = +20%).</summary>
        public const float OverwatchMultiplier = 1.2f;

        private static readonly Dictionary<GameObject, double> Suppressed = new Dictionary<GameObject, double>();
        private static readonly Dictionary<GameObject, double> Marked = new Dictionary<GameObject, double>();
        private static readonly Dictionary<ulong, List<string>> DoctrinesByClient = new Dictionary<ulong, List<string>>();
        private static readonly List<GameObject> Stale = new List<GameObject>();
        private static readonly List<string> Classes = new List<string>();
        private static bool _doctrinesDirty = true;

        /// <summary>True when the enemy was marked by a sentry in the last few seconds (HUD hooks can show it).</summary>
        public static bool IsMarked(GameObject enemy) =>
            enemy != null && Marked.TryGetValue(enemy, out double until) && Time.timeAsDouble < until;

        /// <summary>The doctrines a given player has right now (the same rules the HUD uses).</summary>
        public static bool Has(ulong clientId, string doctrine)
        {
            if (_doctrinesDirty) RebuildDoctrines();
            return DoctrinesByClient.TryGetValue(clientId, out List<string> list) && list.Contains(doctrine);
        }

        /// <summary>Pure rule for tests: the multiplier for one hit given who dealt it and the target's marks.</summary>
        public static float Multiplier(bool gunnerWithOverwatch, bool targetMarked, bool tankWithCoveringFire, bool sourceSuppressed)
        {
            float m = 1f;
            if (gunnerWithOverwatch && targetMarked) m *= OverwatchMultiplier;
            if (tankWithCoveringFire && sourceSuppressed) m *= CoveringFireMultiplier;
            return m;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            DamageModifiers.Scale = Scale;
            ClassRoster.Changed -= MarkDirty;
            ClassRoster.Changed += MarkDirty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Suppressed.Clear();
            Marked.Clear();
            DoctrinesByClient.Clear();
            _doctrinesDirty = true;
        }

        private static void MarkDirty() => _doctrinesDirty = true;

        private static float Scale(DamageInfo info, GameObject target)
        {
            double now = Time.timeAsDouble;
            PlayerIdentity attacker = PlayerIdentity.Of(info.Source);
            PlayerIdentity victim = PlayerIdentity.Of(target);
            if (attacker != null && victim == null)
            {
                // A player (or their sentry) hitting an enemy.
                // Any of the Engineer's automation marks: the Deploy Sentry turret, scrap-built sentries and repair drones.
                bool sentry = info.WeaponId == SentryTurret.WeaponId || info.WeaponId == "structure.sentry" || info.WeaponId == "structure.drone";
                string cls = ClassRoster.ClassOf(attacker.ClientId);
                if (sentry)
                {
                    Remember(Marked, target, now + OverwatchSeconds);
                    return 1f;
                }
                if (cls != GunnerClassId) return 1f;
                if (Has(attacker.ClientId, PrototypeClasses.CoveringFire)) Remember(Suppressed, target, now + CoveringFireSeconds);
                return Multiplier(Has(attacker.ClientId, PrototypeClasses.Overwatch), IsMarked(target), false, false);
            }
            if (attacker == null && victim != null && info.Source != null)
            {
                // An enemy hitting a player.
                if (ClassRoster.ClassOf(victim.ClientId) != TankClassId || !Has(victim.ClientId, PrototypeClasses.CoveringFire)) return 1f;
                bool suppressed = Suppressed.TryGetValue(info.Source, out double until) && now < until;
                return Multiplier(false, false, true, suppressed);
            }
            return 1f;
        }

        private static void Remember(Dictionary<GameObject, double> marks, GameObject target, double until)
        {
            if (target == null) return;
            if (marks.Count > 256) Prune(marks);
            marks[target] = until;
        }

        private static void Prune(Dictionary<GameObject, double> marks)
        {
            double now = Time.timeAsDouble;
            Stale.Clear();
            foreach (KeyValuePair<GameObject, double> kv in marks)
                if (kv.Key == null || kv.Value <= now) Stale.Add(kv.Key);
            foreach (GameObject g in Stale) marks.Remove(g);
            Stale.Clear();
        }

        private static void RebuildDoctrines()
        {
            _doctrinesDirty = false;
            DoctrinesByClient.Clear();
            foreach (KeyValuePair<ulong, string> me in ClassRoster.All)
            {
                Classes.Clear();
                Classes.Add(me.Value);
                foreach (KeyValuePair<ulong, string> other in ClassRoster.All)
                    if (other.Key != me.Key) Classes.Add(other.Value);
                List<string>[] result = DoctrineResolver.Resolve(Classes, PrototypeClasses.Doctrines);
                DoctrinesByClient[me.Key] = result.Length > 0 ? new List<string>(result[0]) : new List<string>();
            }
        }
    }
}
