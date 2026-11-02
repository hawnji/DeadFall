// Minimal compile-time stand-ins for Netcode for GameObjects 2.13.3, copied from the
// public signatures of those packages. Only what Deadfall uses is declared. Nothing here runs: the real
// packages replace this file inside Unity. Keep signatures identical when adding members.
using System;
using System.Collections.Generic;
using UnityEngine;

// Unity.Collections.Allocator comes from UnityEngine.CoreModule, so it is not stubbed here.
namespace Unity.Netcode
{
    using Unity.Collections;

    public enum NetworkDelivery { Unreliable, UnreliableSequenced, Reliable, ReliableSequenced, ReliableFragmentedSequenced }

    public abstract class NetworkTransport : MonoBehaviour { }

    public class NetworkConfig
    {
        public NetworkTransport NetworkTransport = null;
        public uint TickRate = 30;
        public bool ConnectionApproval = false;
        public bool EnableSceneManagement = true;
        public bool ForceSamePrefabs = true;
        public int ClientConnectionBufferTimeout = 10;
    }

    public struct FastBufferWriter : IDisposable
    {
        public struct ForPrimitives { }
        public struct ForEnums { }
        public FastBufferWriter(int size, Allocator allocator, int maxSize = -1) { Length = 0; }
        public int Length { get; }
        public void Dispose() { }
        public void WriteValueSafe(string s, bool oneByteChars = false) { }
        public void WriteValueSafe(in Vector3 value) { }
        public void WriteValueSafe<T>(in T value, ForPrimitives unused = default)
            where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> { }
        public void WriteValueSafe<T>(in T value, ForEnums unused = default) where T : unmanaged, Enum { }
    }

    public struct FastBufferReader : IDisposable
    {
        public int Length { get; }
        public int Position { get; }
        public void Dispose() { }
        public void ReadValueSafe(out string s, bool oneByteChars = false) { s = null; }
        public void ReadValueSafe(out Vector3 value) { value = default; }
        public void ReadValueSafe<T>(out T value, FastBufferWriter.ForPrimitives unused = default)
            where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> { value = default; }
        public void ReadValueSafe<T>(out T value, FastBufferWriter.ForEnums unused = default) where T : unmanaged, Enum { value = default; }
    }

    public class CustomMessagingManager
    {
        public delegate void HandleNamedMessageDelegate(ulong senderClientId, FastBufferReader messagePayload);
        public void RegisterNamedMessageHandler(string name, HandleNamedMessageDelegate callback) { }
        public void UnregisterNamedMessageHandler(string name) { }
        public void SendNamedMessageToAll(string messageName, FastBufferWriter messageStream, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced) { }
        public void SendNamedMessage(string messageName, ulong clientId, FastBufferWriter messageStream, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced) { }
        public void SendNamedMessage(string messageName, IReadOnlyList<ulong> clientIds, FastBufferWriter messageStream, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced) { }
    }

    public class NetworkManager : MonoBehaviour
    {
        public const ulong ServerClientId = 0;
        public static NetworkManager Singleton { get; private set; }
        public NetworkConfig NetworkConfig;
        public bool RunInBackground = true;
        public CustomMessagingManager CustomMessagingManager { get; private set; }
        public ulong LocalClientId { get; }
        public bool IsServer { get; }
        public bool IsClient { get; }
        public bool IsHost { get; }
        public bool IsListening { get; }
        public bool IsConnectedClient { get; }
        public bool ShutdownInProgress { get; }
        public string DisconnectReason { get; }
        public IReadOnlyList<ulong> ConnectedClientsIds { get; }
        public event Action<ulong> OnClientConnectedCallback { add { } remove { } }
        public event Action<ulong> OnClientDisconnectCallback { add { } remove { } }
        public event Action OnTransportFailure { add { } remove { } }
        public bool StartHost() => false;
        public bool StartClient() => false;
        public void Shutdown(bool discardMessageQueue = false) { }
    }
}

namespace Unity.Netcode.Transports.UTP
{
    public class UnityTransport : Unity.Netcode.NetworkTransport
    {
        public void SetConnectionData(string ipv4Address, ushort port, string listenAddress = null) { }
    }
}
