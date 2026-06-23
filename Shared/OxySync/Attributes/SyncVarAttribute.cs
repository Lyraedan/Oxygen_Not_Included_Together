using System;

namespace Shared.OxySync.Attributes
{
    /// <summary>
    /// Marks a field for automatic host→client replication.
    /// The OxySyncManager polls all [SyncVar] fields every SyncInterval (default 0.5s),
    /// compares current vs last-sent value, and broadcasts changes via SyncVarPacket
    /// or SyncVarBatchPacket.
    /// </summary>
    /// <param name="Hook">Name of a method `void OnFieldChanged(OldType old, NewType val)`
    /// invoked on the client when this SyncVar changes.</param>
    /// <param name="Epsilon">Minimum magnitude change for float/Vector2/Vector3 to trigger a send.
    /// Default 0.01f. Avoids sending noise from tiny floating-point drift.</param>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class SyncVarAttribute : Attribute
    {
        public string? Hook { get; set; }
        public float Epsilon { get; set; } = 0.01f;
    }
}
