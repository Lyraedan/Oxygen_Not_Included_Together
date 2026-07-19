using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Core
{
	public sealed class LoadingAcceptedPacket : IPacket, IHostOnlyPacket
	{
		public ulong ReconnectToken;
		public long SnapshotGeneration;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (ReconnectToken == 0 || SnapshotGeneration <= 0)
				throw new InvalidDataException("Invalid loading approval proof");
			writer.Write(ReconnectToken);
			writer.Write(SnapshotGeneration);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			ReconnectToken = reader.ReadUInt64();
			SnapshotGeneration = reader.ReadInt64();
			if (ReconnectToken == 0 || SnapshotGeneration <= 0)
				throw new InvalidDataException("Invalid loading approval proof");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (MultiplayerSession.IsHost || !PacketHandler.CurrentContext.SenderIsHost)
				return;

			if (!ReadyManager.AcceptLoadingApproval(ReconnectToken, SnapshotGeneration))
				DebugConsole.LogWarning("[LoadingAccepted] Rejected stale or mismatched loading approval");
		}
	}
}
