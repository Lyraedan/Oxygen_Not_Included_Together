namespace ONI_Together.Networking.Transport.External
{
    internal delegate bool TryDequeueDelegate(out ulong senderId, out byte[] payload);
}
