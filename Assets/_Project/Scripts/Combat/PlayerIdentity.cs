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
        private ulong _remoteId;

        public bool IsLocal { get; private set; } = true;

        public ulong ClientId => IsLocal ? SessionService.Instance.LocalClientId : _remoteId;

        public void SetRemote(ulong clientId)
        {
            IsLocal = false;
            _remoteId = clientId;
        }

        /// <summary>The player behind <paramref name="obj"/> (the object itself or a parent), or null for non-players.</summary>
        public static PlayerIdentity Of(GameObject obj) => obj != null ? obj.GetComponentInParent<PlayerIdentity>() : null;
    }
}
