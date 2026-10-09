using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.World.Buildings;
using ONI_Together.Networking.Overlay;
using ONI_Together.Networking.States;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Architecture
{
	public static class PacketHandler
	{
		public static void HandleIncoming(byte[] data)
		{
			using var _ = Profiler.Scope();

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
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

		public static bool ShouldDispatchPacket(IPacket packet)
		{
            if (MultiplayerSession.IsClient)
            {
                if (GameClient.State == ClientState.InGame)
                    return true;

                if (GameClient.State == ClientState.LoadingWorld)
                    return packet is IAllowedWithoutWorldPacket || IsLoadingWorldResponse(packet);

                return packet is IAllowedWithoutWorldPacket || packet is IModApiPacket;
            }

            // Single player case
			if (!MultiplayerSession.IsHost || !MultiplayerSession.SessionHasPlayers)
				return true;

            if (ReadyManager.IsSynchronizing)
            {
                if (UnityEngine.Time.realtimeSinceStartup < ReadyManager.GameplayDrainUntil)
                    return true;

                return packet is IAllowedWithoutWorldPacket || IsLoadingWorldRequest(packet);
            }

            return true;
		}

		internal static bool IsLoadingWorldRequest(IPacket packet)
		{
			return packet is RequestOperationalStatePacket or StructureStateRequestPacket;
		}

		internal static bool IsLoadingWorldResponse(IPacket packet)
		{
			return packet is OperationalStatePacket or StructureStatePacket or LogicStatePacket;
		}

		private static void Dispatch(IPacket packet)
		{
			using var _ = Profiler.Scope();

			packet.OnDispatched();
		}
	}
}
