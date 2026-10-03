using System;

namespace Shared.OxySync.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TargetRpcAttribute : Attribute
    {
        public int SendMode { get; set; } = 9; // PacketSendMode.ReliableImmediate
    }
}
