// Stand-ins for Unity Gaming Services (Multiplayer Services package: Core, Authentication, Relay) so
// Deadfall.Online compiles here. Only the members the game uses.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unity.Services.Core
{
    public enum ServicesInitializationState { Uninitialized, Initializing, Initialized }
    public static class UnityServices
    {
        public static ServicesInitializationState State => ServicesInitializationState.Uninitialized;
        public static Task InitializeAsync() => Task.CompletedTask;
    }
    public class RequestFailedException : Exception
    {
        public int ErrorCode => 0;
        public RequestFailedException(int code, string message) : base(message) { }
    }
}

namespace Unity.Services.Authentication
{
    public interface IAuthenticationService
    {
        bool IsSignedIn { get; }
        Task SignInAnonymouslyAsync(SignInOptions options = null);
    }
    public class SignInOptions { }
    public static class AuthenticationService
    {
        public static IAuthenticationService Instance => null;
    }
}

namespace Unity.Services.Relay.Models
{
    public class RelayServerEndpoint
    {
        public string ConnectionType { get; }
        public string Host { get; }
        public int Port { get; }
        public bool Secure { get; }
    }
    public class Allocation
    {
        public Guid AllocationId { get; }
        public byte[] AllocationIdBytes { get; }
        public byte[] Key { get; }
        public byte[] ConnectionData { get; }
        public List<RelayServerEndpoint> ServerEndpoints { get; }
    }
    public class JoinAllocation
    {
        public Guid AllocationId { get; }
        public byte[] AllocationIdBytes { get; }
        public byte[] Key { get; }
        public byte[] ConnectionData { get; }
        public byte[] HostConnectionData { get; }
        public List<RelayServerEndpoint> ServerEndpoints { get; }
    }
}

namespace Unity.Services.Relay
{
    using Unity.Services.Relay.Models;
    public interface IRelayService
    {
        Task<Allocation> CreateAllocationAsync(int maxConnections, string region = null);
        Task<string> GetJoinCodeAsync(Guid allocationId);
        Task<JoinAllocation> JoinAllocationAsync(string joinCode);
    }
    public static class RelayService
    {
        public static IRelayService Instance => null;
    }
    public class RelayServiceException : Unity.Services.Core.RequestFailedException
    {
        public RelayServiceException(int code, string message) : base(code, message) { }
    }
}
