using ONI_Together.Networking;
using System;

namespace Shared.OxySync.Attributes
{
    /// <summary>
    /// Marks a method as a host→all-clients RPC.
    /// The host calls <c>CallClientRpc(nameof(Method), args...)</c> to execute
    /// the method on every connected client (excluding the host).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ClientRpcAttribute : Attribute
    {
        public int InterestGroup { get; set; } = -1;
        public PacketSendMode SendMode { get; set; } = PacketSendMode.Unreliable;
    }
}
