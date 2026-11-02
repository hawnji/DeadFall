using System;
using System.Collections.Generic;
using Deadfall.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Deadfall.Net
{
    public enum SessionRole
    {
        Offline,
        Host,
        Client,
    }

    /// <summary>Receives one named message. The reader is only valid during the call.</summary>
    public delegate void SessionMessageHandler(ulong senderClientId, FastBufferReader reader);

    /// <summary>
    /// Owns the Netcode for GameObjects NetworkManager and exposes a small, game-facing API: host, join,
    /// leave, peer events, and named messages. Gameplay code talks to this instead of NGO directly, so
    /// Relay/Lobby (M11) only changes how the transport is configured here.
    ///
    /// The NetworkManager is created from code (no prefab or scene object needed). It has no network
    /// prefabs: players and targets are synced with named messages (DECISIONS T-19).
    /// </summary>
    public sealed class SessionService : MonoBehaviour
    {
        public const ushort DefaultPort = 7777;
        public const string DefaultAddress = "127.0.0.1";
        private const string LogCategory = "Session";

        private static SessionService _instance;

        private readonly Dictionary<string, SessionMessageHandler> _handlers = new Dictionary<string, SessionMessageHandler>();
        private readonly List<ulong> _sendList = new List<ulong>(4);
        private NetworkManager _network;
        private UnityTransport _transport;
        private bool _clientConnected;

        public SessionRole Role { get; private set; } = SessionRole.Offline;
        public bool IsActive => Role != SessionRole.Offline;
        public bool IsHost => Role == SessionRole.Host;
        /// <summary>True for a joined client that has finished connecting (not the host).</summary>
        public bool IsConnectedClient => Role == SessionRole.Client && _clientConnected;
        /// <summary>True while a client is still trying to reach the host.</summary>
        public bool IsConnecting => Role == SessionRole.Client && !_clientConnected;
        public ulong LocalClientId => _network != null && IsActive ? _network.LocalClientId : NetworkManager.ServerClientId;
        /// <summary>Human-readable reason for the last failure or disconnect, empty when none.</summary>
        public string LastError { get; private set; } = string.Empty;
        public string Address { get; private set; } = DefaultAddress;
        public ushort Port { get; private set; } = DefaultPort;
        /// <summary>Number of remote players connected to this host (host only).</summary>
        public int RemotePeerCount => IsHost && _network != null ? Mathf.Max(0, _network.ConnectedClientsIds.Count - 1) : 0;

        /// <summary>The host started, or a client finished connecting.</summary>
        public event Action<SessionRole> Started;
        /// <summary>The session ended for any reason (left, host closed, connection failed).</summary>
        public event Action Stopped;
        /// <summary>Host only: a remote player connected.</summary>
        public event Action<ulong> PeerConnected;
        /// <summary>Host only: a remote player disconnected.</summary>
        public event Action<ulong> PeerDisconnected;

        /// <summary>The service if it exists, without creating one (safe during teardown).</summary>
        public static SessionService Current => _instance;

        public static SessionService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[SessionService]");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<SessionService>();
                }
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            Stop();
            if (_network != null)
            {
                _network.OnClientConnectedCallback -= OnClientConnected;
                _network.OnClientDisconnectCallback -= OnClientDisconnected;
                _network.OnTransportFailure -= OnTransportFailure;
                Destroy(_network.gameObject);
            }
            _instance = null;
        }

        private void OnApplicationQuit() => Stop();

        // ---------------------------------------------------------------- lifecycle

        public bool StartHost(ushort port)
        {
            if (IsActive) return false;
            EnsureNetworkManager();
            Port = port;
            Address = "0.0.0.0";
            _transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            LastError = string.Empty;
            if (!_network.StartHost())
            {
                Fail($"Could not host on port {port}. Is another game already using it?");
                return false;
            }
            Role = SessionRole.Host;
            AttachHandlers();
            GameLog.Info(LogCategory, $"Hosting on port {port}");
            Started?.Invoke(Role);
            return true;
        }

        public bool StartClient(string address, ushort port)
        {
            if (IsActive) return false;
            address = string.IsNullOrWhiteSpace(address) ? DefaultAddress : address.Trim();
            EnsureNetworkManager();
            Address = address;
            Port = port;
            _transport.SetConnectionData(address, port);
            LastError = string.Empty;
            _clientConnected = false;
            if (!_network.StartClient())
            {
                Fail($"Could not start a connection to {address}:{port}. Check the address.");
                return false;
            }
            // Started fires from OnClientConnected once the host accepts us. Handlers are bound now so that
            // nothing the host sends right after accepting us arrives before they exist.
            Role = SessionRole.Client;
            AttachHandlers();
            GameLog.Info(LogCategory, $"Connecting to {address}:{port}");
            return true;
        }

        /// <summary>Leaves or closes the session. Safe to call when offline.</summary>
        public void Stop() => EndSession(string.Empty);

        private void EndSession(string reason)
        {
            if (!string.IsNullOrEmpty(reason)) LastError = reason;
            if (Role == SessionRole.Offline && (_network == null || !_network.IsListening)) return;
            bool wasActive = Role != SessionRole.Offline;
            DetachHandlers();
            Role = SessionRole.Offline;
            _clientConnected = false;
            if (_network != null && (_network.IsListening || _network.IsClient || _network.IsServer) && !_network.ShutdownInProgress)
                _network.Shutdown();
            if (wasActive)
            {
                GameLog.Info(LogCategory, string.IsNullOrEmpty(reason) ? "Session ended" : $"Session ended: {reason}");
                Stopped?.Invoke();
            }
        }

        private void Fail(string message)
        {
            LastError = message;
            GameLog.Warn(LogCategory, message);
            if (_network != null && _network.IsListening) _network.Shutdown();
        }

        private void EnsureNetworkManager()
        {
            if (_network != null) return;
            // Create inactive so NetworkConfig is assigned before NetworkManager.Awake/OnEnable run.
            // It must stay a root object: NGO refuses nested NetworkManagers and marks it DontDestroyOnLoad itself.
            var go = new GameObject("[NetworkManager]");
            go.SetActive(false);
            _transport = go.AddComponent<UnityTransport>();
            _network = go.AddComponent<NetworkManager>();
            _network.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _transport,
                EnableSceneManagement = false,
                ConnectionApproval = false,
                // No network prefabs exist yet; peers only need to agree on message formats.
                ForceSamePrefabs = false,
            };
            _network.RunInBackground = true;
            go.SetActive(true);

            _network.OnClientConnectedCallback += OnClientConnected;
            _network.OnClientDisconnectCallback += OnClientDisconnected;
            _network.OnTransportFailure += OnTransportFailure;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (Role == SessionRole.Host)
            {
                if (clientId == _network.LocalClientId) return; // the host's own local client
                GameLog.Info(LogCategory, $"Player {clientId} joined");
                PeerConnected?.Invoke(clientId);
            }
            else if (Role == SessionRole.Client && clientId == _network.LocalClientId && !_clientConnected)
            {
                _clientConnected = true;
                GameLog.Info(LogCategory, $"Connected as player {clientId}");
                Started?.Invoke(Role);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (Role == SessionRole.Host)
            {
                if (clientId == _network.LocalClientId) return;
                GameLog.Info(LogCategory, $"Player {clientId} left");
                PeerDisconnected?.Invoke(clientId);
                return;
            }
            if (Role != SessionRole.Client) return;
            // On a client this fires for our own connection: either the host went away or we never got in.
            string why = _network.DisconnectReason;
            if (string.IsNullOrEmpty(why))
                why = _clientConnected ? "The host closed the session or the connection dropped." : $"Could not reach a host at {Address}:{Port}.";
            EndSession(why);
        }

        private void OnTransportFailure() => EndSession("Network transport failed.");

        // ---------------------------------------------------------------- messages

        /// <summary>Registers a handler for a named message. Handlers survive across sessions.</summary>
        public void RegisterHandler(string messageName, SessionMessageHandler handler)
        {
            _handlers[messageName] = handler;
            if (IsActive) Bind(messageName, handler);
        }

        public void UnregisterHandler(string messageName)
        {
            if (!_handlers.Remove(messageName)) return;
            if (_network != null && _network.CustomMessagingManager != null)
                _network.CustomMessagingManager.UnregisterNamedMessageHandler(messageName);
        }

        private void AttachHandlers()
        {
            foreach (KeyValuePair<string, SessionMessageHandler> kv in _handlers) Bind(kv.Key, kv.Value);
        }

        private void DetachHandlers()
        {
            if (_network == null || _network.CustomMessagingManager == null) return;
            foreach (string messageName in _handlers.Keys)
                _network.CustomMessagingManager.UnregisterNamedMessageHandler(messageName);
        }

        private void Bind(string messageName, SessionMessageHandler handler)
        {
            CustomMessagingManager cmm = _network != null ? _network.CustomMessagingManager : null;
            if (cmm == null) return;
            cmm.RegisterNamedMessageHandler(messageName, (sender, reader) =>
            {
                try
                {
                    handler(sender, reader);
                }
                catch (Exception e)
                {
                    // A malformed message must never take the session down.
                    GameLog.Warn(LogCategory, $"Bad '{messageName}' message from {sender}: {e.Message}");
                }
            });
        }

        /// <summary>Client: sends to the host. Host: does nothing (call your handler directly instead).</summary>
        public void SendToServer(string messageName, FastBufferWriter writer, NetworkDelivery delivery)
        {
            if (!IsConnectedClient) return;
            _network.CustomMessagingManager.SendNamedMessage(messageName, NetworkManager.ServerClientId, writer, delivery);
        }

        /// <summary>Host only: sends to one remote player. Never sends to the host's own client.</summary>
        public void SendToClient(string messageName, ulong clientId, FastBufferWriter writer, NetworkDelivery delivery)
        {
            if (!IsHost || clientId == _network.LocalClientId) return;
            _network.CustomMessagingManager.SendNamedMessage(messageName, clientId, writer, delivery);
        }

        /// <summary>Host only: sends to every remote player except <paramref name="exceptClientId"/>.</summary>
        public void SendToRemoteClients(string messageName, FastBufferWriter writer, NetworkDelivery delivery, ulong exceptClientId = ulong.MaxValue)
        {
            if (!IsHost) return;
            _sendList.Clear();
            IReadOnlyList<ulong> ids = _network.ConnectedClientsIds;
            for (int i = 0; i < ids.Count; i++)
            {
                ulong id = ids[i];
                if (id != _network.LocalClientId && id != exceptClientId) _sendList.Add(id);
            }
            if (_sendList.Count == 0) return;
            _network.CustomMessagingManager.SendNamedMessage(messageName, _sendList, writer, delivery);
        }
    }
}
