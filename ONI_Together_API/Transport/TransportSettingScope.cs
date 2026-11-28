using System;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Whether a setting is shown while hosting, while joining, or both.
    /// </summary>
    [Flags]
    public enum TransportSettingScope
    {
        Host = 1,
        Client = 2,
        Both = Host | Client
    }
}
