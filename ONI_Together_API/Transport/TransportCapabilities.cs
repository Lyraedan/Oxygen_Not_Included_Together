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
        Custom = 16,

        /// <summary>
        /// The transport can expose a direct TCP endpoint for large save-file transfers.
        /// When advertised, the host/server also implements GetFileTransferPort() and the
        /// client implements GetFileTransferHost().
        /// </summary>
        TcpFileTransfer = 32
    }
}
