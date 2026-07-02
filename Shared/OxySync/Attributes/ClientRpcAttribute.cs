using System;

namespace Shared.OxySync.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ClientRpcAttribute : Attribute
    {
        public int InterestGroup { get; set; } = -1;
        public int SendMode { get; set; } = 8; // PacketSendMode.Reliable
    }
}
