using Deadfall.Net;
using UnityEngine;

namespace Deadfall.Combat
{
    /// <summary>
    /// Which player a body belongs to, so kills and rewards can be credited to the right person. On the local
    /// player the id follows the session (0 offline and on the host, the assigned id on a client); on a remote
    /// player's stand-in it is that player's client id.
    /// </summary>
    public sealed class PlayerIdentity : MonoBehaviour
    {
        private static readonly System.Collections.Generic.List<PlayerIdentity> Registry = new System.Collections.Generic.List<PlayerIdentity>(4);
        private ulong _remoteId;

        /// <summary>Every player body in the scene (local player and remote stand-ins).</summary>
        public static System.Collections.Generic.IReadOnlyList<PlayerIdentity> All => Registry;

        /// <summary>The body of player <paramref name="clientId"/>, or null.</summary>
        public static PlayerIdentity Find(ulong clientId)
        {
            for (int i = 0; i < Registry.Count; i++)
                if (Registry[i] != null && Registry[i].ClientId == clientId) return Registry[i];
            return null;
        }

        /// <summary>The local player's body, or null.</summary>
        public static PlayerIdentity Local
        {
            get
            {
                for (int i = 0; i < Registry.Count; i++)
                    if (Registry[i] != null && Registry[i].IsLocal) return Registry[i];
                return null;
            }
        }

        private void OnEnable()
        {
            if (!Registry.Contains(this)) Registry.Add(this);
        }

        private void OnDisable() => Registry.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Registry.Clear();

        public bool IsLocal { get; private set; } = true;

        public ulong ClientId => IsLocal ? (SessionService.Current != null ? SessionService.Current.LocalClientId : 0UL) : _remoteId;

        public void SetRemote(ulong clientId)
        {
            IsLocal = false;
            _remoteId = clientId;
        }

        /// <summary>The player behind <paramref name="obj"/> (the object itself or a parent), or null for non-players.</summary>
        public static PlayerIdentity Of(GameObject obj) => obj != null ? obj.GetComponentInParent<PlayerIdentity>() : null;
    }
}
