using System;

namespace Shared.OxySync.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class CommandAttribute : Attribute
    {
        public bool RequiresHost { get; set; } = false;
        public int SendMode { get; set; } = 9; // PacketSendMode.ReliableImmediate
    }
}
