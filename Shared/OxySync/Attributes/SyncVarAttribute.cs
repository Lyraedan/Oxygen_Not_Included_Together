using System;

namespace Shared.OxySync.Attributes
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SyncVarAttribute : Attribute
    {
        public string? Hook { get; set; }
        public float Epsilon { get; set; } = 0.01f;
        public int InterestGroup { get; set; } = -1;
        public int SendMode { get; set; } = 0; // PacketSendMode.Unreliable
    }
}
