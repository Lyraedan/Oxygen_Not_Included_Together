using System;

namespace Shared.OxySync.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ClientRpcAttribute : Attribute
    {
        public bool IncludeHost { get; set; } = false;
        public int InterestGroup { get; set; } = -1;
        public int SendMode { get; set; } = 9; // PacketSendMode.ReliableImmediate
    }
}
