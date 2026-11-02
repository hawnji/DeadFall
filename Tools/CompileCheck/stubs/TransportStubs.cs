// Minimal stand-in for com.unity.transport (Unity.Networking.Transport), used by the Netcode stub.
// Netcode's UnityTransport.SetConnectionData has an overload taking NetworkEndpoint, so any assembly that
// calls SetConnectionData must reference this assembly too (CS0012 otherwise). Keeping it separate makes
// the compile check catch a missing reference the same way Unity does.
namespace Unity.Networking.Transport
{
    public struct NetworkEndpoint
    {
        public string Address => string.Empty;
    }
}
