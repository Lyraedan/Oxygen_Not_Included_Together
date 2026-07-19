using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.Chores
{
	public class ChoreErrandsSubscribePacket : IPacket
	{
		public int DupeNetId;
		public bool Subscribe;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			writer.Write(DupeNetId);
			writer.Write(Subscribe);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			DupeNetId = reader.ReadInt32();
			Subscribe = reader.ReadBoolean();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHost) return;
			DispatchContext context = PacketHandler.CurrentContext;
			var player = MultiplayerSession.GetPlayer(context.SenderId);
			if (context.SenderIsHost || player == null || !player.ProtocolVerified
			    || !NetworkIdentityRegistry.Exists(DupeNetId))
				return;
			DuplicantChoreBroadcaster.SetSubscription(context.SenderId, DupeNetId, Subscribe);
		}
	}
}
