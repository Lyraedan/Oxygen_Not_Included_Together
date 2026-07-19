using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	public sealed class ReadyAcceptedAckPacket : IPacket
	{
		public ulong ReconnectToken;
		public long SnapshotGeneration;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			ValidateProof();
			writer.Write(ReconnectToken);
			writer.Write(SnapshotGeneration);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			ReconnectToken = reader.ReadUInt64();
			SnapshotGeneration = reader.ReadInt64();
			ValidateProof();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHost || PacketHandler.CurrentContext.SenderIsHost)
				return;
			if (!ReadyManager.AcknowledgeReadyAccepted(
				    PacketHandler.CurrentContext.SenderId,
				    ReconnectToken,
				    SnapshotGeneration))
			{
				DebugConsole.LogWarning("[ReadyAcceptedAck] Rejected stale or mismatched proof");
			}
		}

		private void ValidateProof()
		{
			if (ReconnectToken == 0 || SnapshotGeneration <= 0)
				throw new InvalidDataException("Invalid Ready completion proof");
		}
	}
}
