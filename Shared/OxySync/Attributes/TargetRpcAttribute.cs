using System;

namespace Shared.OxySync.Attributes
{
    /// <summary>
    /// Marks a method as a host→specific-client RPC.
    /// The host calls <c>CallTargetRpc(targetPlayerId, nameof(Method), args...)</c>
    /// to execute the method on exactly one connected client.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TargetRpcAttribute : Attribute
    {
    }
}
