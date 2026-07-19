using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.Bionics;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Bionic
{
	public sealed class BionicAssignmentData
	{
		public int UpgradeNetId;
		public bool HasAssignee;
		public int AssigneeNetId;

		internal bool IsWireValid()
			=> UpgradeNetId != 0 && (!HasAssignee || AssigneeNetId != 0);

		internal void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid bionic assignment");
			writer.Write(UpgradeNetId);
			writer.Write(HasAssignee);
			writer.Write(AssigneeNetId);
		}

		internal static BionicAssignmentData Deserialize(BinaryReader reader)
		{
			var data = new BionicAssignmentData
			{
				UpgradeNetId = reader.ReadInt32(),
				HasAssignee = reader.ReadBoolean(),
				AssigneeNetId = reader.ReadInt32()
			};
			if (!data.IsWireValid())
				throw new InvalidDataException("Invalid bionic assignment");
			return data;
		}
	}

	public sealed class BionicAssignmentRequestPacket : IPacket, IClientRelayable
	{
		public BionicAssignmentData Data = new();

		public BionicAssignmentRequestPacket()
		{
		}

		internal BionicAssignmentRequestPacket(BionicAssignmentData data) => Data = data;

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);
		public void Deserialize(BinaryReader reader) => Data = BionicAssignmentData.Deserialize(reader);

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool protocolVerified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, protocolVerified) ||
			    !BionicUpgradeAssignmentSync.TryApply(Data) ||
			    !NetworkIdentityRegistry.TryGetComponent(Data.UpgradeNetId, out BionicUpgradeComponent upgrade) ||
			    !BionicUpgradeAssignmentSync.TryCapture(upgrade, out BionicAssignmentData state))
				return;
			PacketSender.SendToAllClients(new BionicAssignmentStatePacket(state));
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;
	}

	public sealed class BionicAssignmentStatePacket : IPacket, IHostOnlyPacket
	{
		public BionicAssignmentData Data = new();

		public BionicAssignmentStatePacket()
		{
		}

		internal BionicAssignmentStatePacket(BionicAssignmentData data) => Data = data;

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);
		public void Deserialize(BinaryReader reader) => Data = BionicAssignmentData.Deserialize(reader);

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				BionicUpgradeAssignmentSync.TryApply(Data);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}

	public sealed class ExplorerGeyserRevealPacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxWorldId = 4096;
		internal const int MaxCell = 4 * 1024 * 1024;
		public int ExplorerNetId;
		public int WorldId;
		public int Cell;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid explorer geyser outcome");
			writer.Write(ExplorerNetId);
			writer.Write(WorldId);
			writer.Write(Cell);
		}

		public void Deserialize(BinaryReader reader)
		{
			ExplorerNetId = reader.ReadInt32();
			WorldId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid explorer geyser outcome");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				ExplorerGeyserRevealSync.TryApply(this);
		}

		internal bool IsWireValid()
			=> ExplorerNetId != 0 && WorldId >= 0 && WorldId <= MaxWorldId && Cell >= 0 && Cell < MaxCell;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
