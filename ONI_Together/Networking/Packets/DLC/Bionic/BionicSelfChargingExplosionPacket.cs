using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Bionic;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Bionic
{
	public sealed class BionicSelfChargingExplosionPacket : IPacket, IHostOnlyPacket
	{
		public int NetId;
		public int Sequence;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid bionic explosion outcome");
			writer.Write(NetId);
			writer.Write(Sequence);
		}

		public void Deserialize(BinaryReader reader)
		{
			NetId = reader.ReadInt32();
			Sequence = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid bionic explosion outcome");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				BionicExplosionSync.TryApply(this);
		}

		internal bool IsWireValid() => NetId != 0 && Sequence > 0;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
