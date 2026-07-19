using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Animation
{
	internal class AnimResyncRequestPacket : IPacket
	{
		private const int MaxNetIds = 4096;
		private static int _rejectedPackets;

		public ulong RequesterId;
		public int[] NetIds = [];
		internal static bool ShouldAccept(ulong requesterId, DispatchContext context) =>
			requesterId != 0 && !context.SenderIsHost && SyncBarrier.SenderMatches(requesterId, context.SenderId);

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(RequesterId);
			writer.Write(NetIds.Length);
			foreach (var netId in NetIds)
				writer.Write(netId);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			RequesterId = reader.ReadUInt64();
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxNetIds)
			{
				DebugConsole.LogWarning($"[AnimResyncRequestPacket] Invalid NetId count {count}, dropping request");
				NetIds = [];
				return;
			}
			NetIds = new int[count];
			for (int i = 0; i < count; i++)
				NetIds[i] = reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;
			if (!ShouldAccept(RequesterId, PacketHandler.CurrentContext))
			{
				int rejected = ++_rejectedPackets;
				if (rejected <= 5 || rejected % 100 == 0)
					DebugConsole.LogWarning($"[AnimResyncRequestPacket] Rejected requester {RequesterId} from {PacketHandler.CurrentContext.SenderId}, host={PacketHandler.CurrentContext.SenderIsHost} (#{rejected})");
				return;
			}
			if (NetIds.Length == 0)
				return;

			AnimSyncCoordinator.Instance?.QueueResyncRequest(RequesterId, NetIds);
		}
	}
}
