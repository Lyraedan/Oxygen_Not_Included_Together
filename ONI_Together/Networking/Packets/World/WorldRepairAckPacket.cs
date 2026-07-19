using System.IO;
using ONI_Together.Misc.World;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public sealed class WorldRepairAckPacket : IPacket
	{
		public long AppliedThrough;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			Validate();
			writer.Write(AppliedThrough);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			AppliedThrough = reader.ReadInt64();
			Validate();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHost || PacketHandler.CurrentContext.SenderIsHost)
				return;
			WorldUpdateBatcher.AcceptRepairAck(
				PacketHandler.CurrentContext.SenderId, AppliedThrough);
		}

		private void Validate()
		{
			if (AppliedThrough <= 0)
				throw new InvalidDataException("Invalid world repair ACK");
		}
	}
}
