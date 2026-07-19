using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class HighEnergyParticleDirectionRequestPacket : IPacket, IClientRelayable
	{
		public int TargetNetId;
		public EightDirection ExpectedDirection;
		public EightDirection DesiredDirection;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid HEP direction request");
			writer.Write(TargetNetId);
			writer.Write((byte)ExpectedDirection);
			writer.Write((byte)DesiredDirection);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			ExpectedDirection = (EightDirection)reader.ReadByte();
			DesiredDirection = (EightDirection)reader.ReadByte();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid HEP direction request");
		}

		public void OnDispatched()
		{
			if (ShouldAccept(MultiplayerSession.IsHost, PacketHandler.CurrentContext))
				HighEnergyParticleDirectionSync.TryHandleRequest(this);
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 && ExpectedDirection != DesiredDirection &&
			   HighEnergyParticleDirectionSync.IsDirectionValid(ExpectedDirection) &&
			   HighEnergyParticleDirectionSync.IsDirectionValid(DesiredDirection);

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast;
	}

	public sealed class HighEnergyParticleDirectionStatePacket : IPacket, IHostOnlyPacket
	{
		public int TargetNetId;
		public EightDirection Direction;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid HEP direction state");
			writer.Write(TargetNetId);
			writer.Write((byte)Direction);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Direction = (EightDirection)reader.ReadByte();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid HEP direction state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				HighEnergyParticleDirectionSync.TryApplyState(TargetNetId, Direction);
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 && HighEnergyParticleDirectionSync.IsDirectionValid(Direction);

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
