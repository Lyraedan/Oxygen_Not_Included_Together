using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Bionic;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Bionic
{
	public sealed class BionicMicrochipProgressStatePacket : IPacket, IHostOnlyPacket
	{
		public int NetId;
		public float Progress;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid bionic microchip progress");
			writer.Write(NetId);
			writer.Write(Progress);
		}

		public void Deserialize(BinaryReader reader)
		{
			NetId = reader.ReadInt32();
			Progress = reader.ReadSingle();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid bionic microchip progress");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				BionicMicrochipSync.TryApply(this);
		}

		internal bool IsWireValid()
			=> NetId != 0 && !float.IsNaN(Progress) && !float.IsInfinity(Progress) &&
			   Progress >= 0f && Progress <= 1f;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
