using System.IO;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.World
{
	public sealed class WorldDataProgressAckPacket : IPacket
	{
		public ulong ClientId;
		public long SnapshotGeneration;
		public int AppliedThroughChunkIndex;

		internal static bool ShouldAccept(
			ulong clientId, long snapshotGeneration, DispatchContext context)
			=> clientId != 0 && snapshotGeneration > 0 && !context.SenderIsHost
			   && SyncBarrier.SenderMatches(clientId, context.SenderId)
			   && ReadyManager.IsCurrentSnapshot(clientId, snapshotGeneration);

		public void Serialize(BinaryWriter writer)
		{
			if (ClientId == 0 || SnapshotGeneration <= 0 || AppliedThroughChunkIndex < 0)
				throw new InvalidDataException("Invalid world baseline progress ACK");
			writer.Write(ClientId);
			writer.Write(SnapshotGeneration);
			writer.Write(AppliedThroughChunkIndex);
		}

		public void Deserialize(BinaryReader reader)
		{
			ClientId = reader.ReadUInt64();
			SnapshotGeneration = reader.ReadInt64();
			AppliedThroughChunkIndex = reader.ReadInt32();
			if (ClientId == 0 || SnapshotGeneration <= 0 || AppliedThroughChunkIndex < 0)
				throw new InvalidDataException("Invalid world baseline progress ACK");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost
			    || !ShouldAccept(ClientId, SnapshotGeneration, PacketHandler.CurrentContext))
				return;
			WorldDataRequestPacket.AcceptProgress(
				ClientId, SnapshotGeneration, AppliedThroughChunkIndex);
		}
	}
}
