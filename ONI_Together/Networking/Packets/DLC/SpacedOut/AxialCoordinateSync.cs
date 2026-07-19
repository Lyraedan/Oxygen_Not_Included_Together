namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	internal static class AxialCoordinateSync
	{
		// AxialI's constructor is (r, q), while the wire format is explicitly (q, r).
		internal static AxialI FromQr(int q, int r) => new(r, q);
	}
}
