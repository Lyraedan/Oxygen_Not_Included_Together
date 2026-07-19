using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Prehistoric;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Prehistoric
{
	public sealed class MosquitoTargetStatePacket : IPacket, IHostOnlyPacket
	{
		public int MosquitoNetId;
		public bool HasVictim;
		public int VictimNetId;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid mosquito target state");
			writer.Write(MosquitoNetId);
			writer.Write(HasVictim);
			writer.Write(VictimNetId);
		}

		public void Deserialize(BinaryReader reader)
		{
			MosquitoNetId = reader.ReadInt32();
			HasVictim = reader.ReadBoolean();
			VictimNetId = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid mosquito target state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				MosquitoHungerSync.TryApply(this);
		}

		internal bool IsWireValid()
			=> MosquitoNetId != 0 && (HasVictim ? VictimNetId != 0 : VictimNetId == 0);

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
