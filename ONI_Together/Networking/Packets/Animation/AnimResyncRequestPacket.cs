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

		public ulong RequesterId;
		public int[] NetIds = [];

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

			if (!MultiplayerSession.IsHost || RequesterId == 0 || NetIds.Length == 0)
				return;

			AnimSyncCoordinator.Instance?.QueueResyncRequest(RequesterId, NetIds);
		}
	}
}
