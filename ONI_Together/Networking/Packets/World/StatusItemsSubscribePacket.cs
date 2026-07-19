using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.World
{
    public class StatusItemsSubscribePacket : IPacket
    {
        public int NetId;
        public bool Subscribe;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
            writer.Write(NetId);
            writer.Write(Subscribe);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
            NetId = reader.ReadInt32();
            Subscribe = reader.ReadBoolean();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();
            if (!MultiplayerSession.IsHost) return;
			DispatchContext context = PacketHandler.CurrentContext;
			var player = MultiplayerSession.GetPlayer(context.SenderId);
			if (context.SenderIsHost || player == null || !player.ProtocolVerified
			    || !NetworkIdentityRegistry.Exists(NetId))
				return;
			StatusBroadcaster.SetSubscription(context.SenderId, NetId, Subscribe);
        }
    }
}
