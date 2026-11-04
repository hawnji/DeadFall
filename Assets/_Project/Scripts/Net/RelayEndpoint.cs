namespace Deadfall.Net
{
    /// <summary>The Relay server and allocation data the transport needs (filled from a Unity Relay allocation).</summary>
    public struct RelayEndpoint
    {
        public string Host;
        public ushort Port;
        public bool Secure;
        public byte[] AllocationId;
        public byte[] Key;
        public byte[] ConnectionData;
        /// <summary>Joining clients only: the host's connection data.</summary>
        public byte[] HostConnectionData;
    }
}
