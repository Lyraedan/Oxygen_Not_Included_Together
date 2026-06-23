using System;

namespace Shared.OxySync.Attributes
{
    /// <summary>
    /// Tag marker for methods that should only execute on the server (host).
    /// Currently documentation-only for non-RPC methods; auto-enforced via
    /// <c>InvokeMethod</c> when called through the OxySync RPC dispatch
    /// (Command, ClientRpc, TargetRpc). Use <c>if (!isServer) return;</c> for
    /// Unity lifecycle methods (Update, LateUpdate, etc.).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ServerAttribute : Attribute
    {
    }
}
