using System;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Optional capabilities a custom transport can advertise to ONI Together.
    /// </summary>
    [Flags]
    public enum TransportCapabilities
    {
        None = 0,
        NativeFragmentation = 1,
        NatTraversal = 2,
        DirectConnect = 4,
        Steam = 8,
        Custom = 16
    }
}
