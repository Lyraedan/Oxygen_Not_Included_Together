using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Overlay;
using ONI_Together.Networking.States;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Architecture
{
	public static class PacketHandler
	{
		private static long _blockedGameplayReceiveCount;

		/// <summary>
		/// A client in the frontend receives the host's live broadcasts before it has a world, and
		/// they throw there (Grid is 0x0). It only runs IAllowedWithoutWorldPacket packets and mod
		/// API packets until the world loads; the rest is dropped, the save carries that state.
		/// </summary>
		public static bool ShouldDispatchWithoutWorld(IPacket packet)
		{
			if (MultiplayerSession.IsHost) return true;
			if (GameClient.State == ClientState.LoadingWorld) return true;
			if (!Utils.IsInMenu()) return true;
			return packet is IAllowedWithoutWorldPacket || packet is IModApiPacket;
		}

		public static void HandleIncoming(byte[] data)
		{
			using var _ = Profiler.Scope();

			using (var ms = new MemoryStream(data))
			using (var reader = new BinaryReader(ms))
			{
				int type = reader.ReadInt32();
				if (!PacketRegistry.HasRegisteredPacket(type))
				{
					DebugConsole.LogError($"Invalid PacketType received: {type}", false);
					return;
				}

				using var scope = Profiler.Scope();
				var packet = PacketRegistry.Create(type);
				packet.Deserialize(reader);

				if (!ShouldDispatchPacket(packet))
					return;

				Dispatch(packet);

				scope.End(packet.GetType().Name, data.Length);
				PacketTracker.TrackIncoming(new PacketTracker.PacketTrackData
				{
					packet = packet,
					size = data.Length
				});

				var tracker = NetIdActivityTracker.Instance;
				if (tracker != null)
				{
					int netId = NetIdActivityTracker.GetNetIdFromPacket(packet);
					if (netId > 0)
						tracker.RecordActivity(netId, data.Length);
				}
			}
		}

		public static bool ShouldDispatchPacket(IPacket packet)
		{
			if (!ShouldDispatchWithoutWorld(packet))
				return false;

			bool clientGameplayBlocked = !MultiplayerSession.IsHost
				&& GameClient.State != ClientState.InGame;
			bool hostSynchronizationActive = MultiplayerSession.IsHost
				&& ReadyManager.IsSynchronizing;
			if ((!clientGameplayBlocked && !hostSynchronizationActive)
				|| PacketLoadGate.Allows(packet))
				return true;

			long blocked = ++_blockedGameplayReceiveCount;
			if (blocked <= 5 || blocked % 100 == 0)
				DebugConsole.LogWarning($"[PacketHandler] discarded gameplay-gated packet #{blocked} packet={packet.GetType().Name}");
			return false;
		}

		private static void Dispatch(IPacket packet)
		{
			using var _ = Profiler.Scope();

			packet.OnDispatched();
		}
	}
}
