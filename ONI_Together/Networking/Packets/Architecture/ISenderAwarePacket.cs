namespace ONI_Together.Networking.Packets.Architecture
{
    // Supplied by the transport, never read from the packet payload.
    internal interface ISenderAwarePacket
    {
        ulong? SenderId { get; set; }
    }
}
