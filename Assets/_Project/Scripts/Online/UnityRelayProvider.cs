using System;
using System.Threading.Tasks;
using Deadfall.Core;
using Deadfall.Net;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace Deadfall.Online
{
    /// <summary>
    /// Unity Relay behind <see cref="OnlineRelay"/>: signs in anonymously, creates or joins an allocation, and hands
    /// the server data to <see cref="SessionService"/>. This assembly only compiles when the Multiplayer Services
    /// package is installed (DEADFALL_UGS), and needs the project linked to Unity Cloud (Project Settings > Services).
    /// </summary>
    public sealed class UnityRelayProvider : IRelayProvider
    {
        private const string LogCategory = "Online";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => OnlineRelay.Provider = new UnityRelayProvider();

        public async void Host(int maxClients, Action<string, string> done)
        {
            try
            {
                await SignIn();
                Allocation a = await RelayService.Instance.CreateAllocationAsync(maxClients);
                string code = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);
                if (!TryEndpoint(a.ServerEndpoints, out RelayServerEndpoint ep)) { done?.Invoke(null, "No Relay server endpoint was offered."); return; }
                var relay = new RelayEndpoint
                {
                    Host = ep.Host, Port = (ushort)ep.Port, Secure = ep.Secure,
                    AllocationId = a.AllocationIdBytes, Key = a.Key, ConnectionData = a.ConnectionData,
                };
                if (!SessionService.Instance.StartRelayHost(relay, code))
                {
                    done?.Invoke(null, Or(SessionService.Instance.LastError, "Could not start the online session."));
                    return;
                }
                done?.Invoke(code, null);
            }
            catch (Exception e)
            {
                GameLog.Warn(LogCategory, $"Online host failed: {e}");
                done?.Invoke(null, Explain(e));
            }
        }

        public async void Join(string code, Action<string> done)
        {
            try
            {
                await SignIn();
                JoinAllocation a = await RelayService.Instance.JoinAllocationAsync(code);
                if (!TryEndpoint(a.ServerEndpoints, out RelayServerEndpoint ep)) { done?.Invoke("No Relay server endpoint was offered."); return; }
                var relay = new RelayEndpoint
                {
                    Host = ep.Host, Port = (ushort)ep.Port, Secure = ep.Secure,
                    AllocationId = a.AllocationIdBytes, Key = a.Key, ConnectionData = a.ConnectionData,
                    HostConnectionData = a.HostConnectionData,
                };
                if (!SessionService.Instance.StartRelayClient(relay, code))
                {
                    done?.Invoke(Or(SessionService.Instance.LastError, "Could not start the online connection."));
                    return;
                }
                done?.Invoke(null);
            }
            catch (Exception e)
            {
                GameLog.Warn(LogCategory, $"Online join failed: {e}");
                done?.Invoke(Explain(e, code));
            }
        }

        private static async Task SignIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>Prefers the encrypted DTLS endpoint, then plain UDP.</summary>
        private static bool TryEndpoint(System.Collections.Generic.List<RelayServerEndpoint> endpoints, out RelayServerEndpoint chosen)
        {
            chosen = null;
            if (endpoints == null) return false;
            foreach (RelayServerEndpoint e in endpoints) if (e.ConnectionType == "dtls") { chosen = e; return true; }
            foreach (RelayServerEndpoint e in endpoints) if (e.ConnectionType == "udp") { chosen = e; return true; }
            return false;
        }

        private static string Or(string a, string b) => string.IsNullOrEmpty(a) ? b : a;

        private static string Explain(Exception e, string code = null)
        {
            string m = e.Message ?? string.Empty;
            if (code != null && (m.IndexOf("join code", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0))
                return $"No game found for code {code}. Check the code with the host.";
            if (m.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0 && m.IndexOf("id", StringComparison.OrdinalIgnoreCase) >= 0)
                return "This copy of the game isn't linked to Unity Cloud (Project Settings > Services).";
            if (e is RequestFailedException) return $"Online service error: {m}";
            return $"Could not go online: {m}";
        }
    }
}
