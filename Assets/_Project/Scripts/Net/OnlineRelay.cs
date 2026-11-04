using System;

namespace Deadfall.Net
{
    /// <summary>
    /// Online play through Unity Relay: the host gets a short join code, friends type it in. No IP addresses or port
    /// forwarding. The real work lives in Deadfall.Online, which only compiles when the Multiplayer Services package is
    /// installed and registers itself as the <see cref="Provider"/>; without it <see cref="Available"/> is false and the
    /// menus only offer LAN/IP play.
    /// </summary>
    public interface IRelayProvider
    {
        /// <summary>Signs in, allocates a Relay server and starts hosting. Calls back with the join code, or an error.</summary>
        void Host(int maxClients, Action<string, string> done);
        /// <summary>Resolves the code and starts joining. Calls back with null on success (connecting), or an error.</summary>
        void Join(string code, Action<string> done);
    }

    public static class OnlineRelay
    {
        /// <summary>Up to four players: the host plus three.</summary>
        public const int MaxClients = 3;

        public static IRelayProvider Provider { get; set; }
        public static bool Available => Provider != null;
        /// <summary>True while a host or join request is in flight.</summary>
        public static bool Busy { get; private set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Busy = false;

        /// <summary>Upper-cases and strips spaces from a typed code.</summary>
        public static string CleanCode(string code) => string.IsNullOrEmpty(code) ? string.Empty : code.Replace(" ", string.Empty).Replace("-", string.Empty).Trim().ToUpperInvariant();

        public static bool Host(Action<string, string> done)
        {
            if (!Available) { done?.Invoke(null, "Online play needs the Multiplayer Services package."); return false; }
            if (Busy) return false;
            Busy = true;
            Provider.Host(MaxClients, (code, error) => { Busy = false; done?.Invoke(code, error); });
            return true;
        }

        public static bool Join(string code, Action<string> done)
        {
            code = CleanCode(code);
            if (!Available) { done?.Invoke("Online play needs the Multiplayer Services package."); return false; }
            if (code.Length < 4) { done?.Invoke("Enter the host's join code."); return false; }
            if (Busy) return false;
            Busy = true;
            Provider.Join(code, error => { Busy = false; done?.Invoke(error); });
            return true;
        }
    }
}
