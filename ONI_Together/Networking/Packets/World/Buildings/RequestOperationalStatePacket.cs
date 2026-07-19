using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;
namespace ONI_Together.Networking.Packets.World.Buildings
{
	internal class RequestOperationalStatePacket : IPacket
	{
		public RequestOperationalStatePacket() { }
		public RequestOperationalStatePacket(MonoBehaviour o)
		{
			using var _ = Profiler.Scope();

			NetId = o.GetNetId();
		}

		public int NetId;
		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			if (NetId == 0)
				throw new InvalidDataException("Invalid operational state request NetId");
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (NetId == 0)
				throw new InvalidDataException("Invalid operational state request NetId");

			writer.Write(NetId);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGet(NetId, out var entity))
				return;
			if (!entity.TryGetComponent<Operational>(out var server))
				return;

			if (OperationalStatePacket.TryCreate(server, out OperationalStatePacket packet))
				PacketSender.SendToPlayer(
					PacketHandler.CurrentContext.SenderId, packet,
					PacketSendMode.ReliableImmediate);
		}
	}
}
