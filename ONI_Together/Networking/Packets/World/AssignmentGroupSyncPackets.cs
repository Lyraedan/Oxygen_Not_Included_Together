using System.IO;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	public sealed class AssignmentGroupMemberRequestPacket : IPacket, IClientRelayable
	{
		public int ControllerNetId;
		public int Cell;
		public int MinionNetId;
		public bool IsMember;

		public void Serialize(BinaryWriter writer) => Write(writer);

		public void Deserialize(BinaryReader reader)
		{
			Read(reader);
			if (!IsWireValid())
				throw new InvalidDataException("Invalid assignment group request");
		}

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool verified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, verified) ||
			    !AssignmentGroupMemberSync.TryApply(ControllerNetId, Cell, MinionNetId, IsMember))
				return;
			AssignmentGroupMemberSync.Broadcast(ControllerNetId, Cell, MinionNetId);
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;

		private bool IsWireValid()
			=> ControllerNetId != 0 && MinionNetId != 0 && Cell >= 0 && Cell < AssignmentData.MaxCell;

		private void Write(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid assignment group request");
			writer.Write(ControllerNetId);
			writer.Write(Cell);
			writer.Write(MinionNetId);
			writer.Write(IsMember);
		}

		private void Read(BinaryReader reader)
		{
			ControllerNetId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			MinionNetId = reader.ReadInt32();
			IsMember = reader.ReadBoolean();
		}
	}

	public sealed class AssignmentGroupMemberStatePacket : IPacket, IHostOnlyPacket
	{
		public int ControllerNetId;
		public int Cell;
		public int MinionNetId;
		public bool IsMember;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid assignment group state");
			writer.Write(ControllerNetId);
			writer.Write(Cell);
			writer.Write(MinionNetId);
			writer.Write(IsMember);
		}

		public void Deserialize(BinaryReader reader)
		{
			ControllerNetId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			MinionNetId = reader.ReadInt32();
			IsMember = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid assignment group state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				AssignmentGroupMemberSync.TryApply(ControllerNetId, Cell, MinionNetId, IsMember);
		}

		internal bool IsWireValid()
			=> ControllerNetId != 0 && MinionNetId != 0 && Cell >= 0 && Cell < AssignmentData.MaxCell;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}

	internal static class AssignmentGroupMemberSync
	{
		internal static bool IsApplying;

		internal static bool TryApply(int controllerNetId, int cell, int minionNetId, bool isMember)
		{
			if (!TryResolve(controllerNetId, cell, minionNetId,
				    out AssignmentGroupController controller, out MinionAssignablesProxy minion))
				return false;
			if (controller.CheckMinionIsMember(minion) == isMember)
				return true;
			IsApplying = true;
			try
			{
				controller.SetMember(minion, isMember);
			}
			finally
			{
				IsApplying = false;
			}
			return true;
		}

		internal static void SendRequest(
			AssignmentGroupController controller,
			MinionAssignablesProxy minion,
			bool isMember)
		{
			if (!TryCapture(controller, minion, out int controllerId, out int cell, out int minionId))
				return;
			PacketSender.SendToAllOtherPeers(new AssignmentGroupMemberRequestPacket
			{
				ControllerNetId = controllerId,
				Cell = cell,
				MinionNetId = minionId,
				IsMember = isMember
			});
		}

		internal static void Broadcast(AssignmentGroupController controller, MinionAssignablesProxy minion)
		{
			if (IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !TryCapture(controller, minion, out int controllerId, out int cell, out int minionId))
				return;
			Broadcast(controllerId, cell, minionId);
		}

		internal static void Broadcast(int controllerId, int cell, int minionId)
		{
			if (!TryResolve(controllerId, cell, minionId,
				    out AssignmentGroupController controller, out MinionAssignablesProxy minion))
				return;
			PacketSender.SendToAllClients(new AssignmentGroupMemberStatePacket
			{
				ControllerNetId = controllerId,
				Cell = cell,
				MinionNetId = minionId,
				IsMember = controller.CheckMinionIsMember(minion)
			});
		}

		private static bool TryCapture(
			AssignmentGroupController controller,
			MinionAssignablesProxy minion,
			out int controllerId,
			out int cell,
			out int minionId)
		{
			NetworkIdentity controllerIdentity = controller?.GetComponent<NetworkIdentity>();
			NetworkIdentity minionIdentity = minion?.GetTargetGameObject()?.GetComponent<NetworkIdentity>();
			controllerId = controllerIdentity?.NetId ?? 0;
			cell = controller == null ? -1 : Grid.PosToCell(controller.gameObject);
			minionId = minionIdentity?.NetId ?? 0;
			return controllerId != 0 && minionId != 0 && cell >= 0 && cell < AssignmentData.MaxCell;
		}

		private static bool TryResolve(
			int controllerId,
			int cell,
			int minionId,
			out AssignmentGroupController controller,
			out MinionAssignablesProxy minion)
		{
			controller = null;
			minion = null;
			if (!NetworkIdentityRegistry.TryGet(controllerId, out NetworkIdentity target) || target == null ||
			    !NetworkIdentityRegistry.TryGet(minionId, out NetworkIdentity minionTarget) || minionTarget == null)
				return false;
			controller = target.GetComponent<AssignmentGroupController>();
			MinionIdentity identity = minionTarget.GetComponent<MinionIdentity>();
			minion = identity?.GetSoleOwner()?.GetComponent<MinionAssignablesProxy>() ??
			         minionTarget.GetComponent<MinionAssignablesProxy>();
			if (controller == null || minion == null)
				return false;
			int deterministicId = target.TryGetComponent<Building>(out _)
				? NetIdHelper.GetDeterministicBuildingId(target.gameObject)
				: 0;
			return AssignmentSync.IdentityMatches(controllerId, cell, target.NetId,
				Grid.PosToCell(target.gameObject), deterministicId);
		}
	}
}
