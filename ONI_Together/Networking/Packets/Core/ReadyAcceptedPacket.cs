using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	public sealed class ReadyAcceptedPacket : IPacket, IHostOnlyPacket
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
			if (MultiplayerSession.IsHost || !PacketHandler.CurrentContext.SenderIsHost)
				return;

			if (!ReadyManager.IsExactReadyAcceptance(
				    ReadyManager.ReconnectToken,
				    ReadyManager.ClientSnapshotGeneration,
				    ReconnectToken,
				    SnapshotGeneration))
				return;
			if (!PacketSender.SendToHost(new ReadyAcceptedAckPacket
			    {
				    ReconnectToken = ReconnectToken,
				    SnapshotGeneration = SnapshotGeneration
			    }, PacketSendMode.ReliableImmediate))
			{
				DebugConsole.LogWarning(
					"[ReadyAccepted] Could not acknowledge host; retaining retry proof");
				return;
			}

			if (ReadyManager.TryConfirmReadyAccepted(ReconnectToken, SnapshotGeneration))
				GameClient.OnReadyAccepted(SnapshotGeneration);
		}

		private void ValidateProof()
		{
			if (ReconnectToken == 0 || SnapshotGeneration <= 0)
				throw new InvalidDataException("Invalid Ready acknowledgement proof");
		}
	}
}
