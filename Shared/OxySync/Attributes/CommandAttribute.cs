using System;

namespace Shared.OxySync.Attributes
{
    /// <summary>
    /// Marks a method as a client→host RPC.
    /// The caller invokes it via <c>CallCommand(nameof(Method), args...)</c>.
    /// On a client it sends a CommandPacket to the host; on the host it invokes directly.
    /// </summary>
    /// <param name="RequiresHost">If true, only the host player may call this command.
    /// Commands from clients are silently dropped.</param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class CommandAttribute : Attribute
    {
        public bool RequiresHost { get; set; } = false;
    }
}
