using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public enum AssignmentAssigneeKind : byte
	{
		None,
		Entity,
		Group,
		Room
	}

	public sealed class AssignmentData
	{
		internal const int MaxCell = 4 * 1024 * 1024;
		internal const int MaxGroupIdLength = 256;
		internal const int MaxSlotIdLength = 256;

		public int TargetNetId;
		public int Cell;
		public AssignmentAssigneeKind AssigneeKind;
		public int AssigneeNetId;
		public string GroupId = "";
		public string SlotId = "";

		internal bool IsWireValid()
		{
			if (TargetNetId == 0 || Cell < 0 || Cell >= MaxCell ||
			    AssigneeKind > AssignmentAssigneeKind.Room || GroupId == null || SlotId == null ||
			    SlotId.Length > MaxSlotIdLength)
				return false;
			return AssigneeKind switch
			{
				AssignmentAssigneeKind.None => AssigneeNetId == 0 && GroupId.Length == 0 && SlotId.Length == 0,
				AssignmentAssigneeKind.Entity => AssigneeNetId != 0 && GroupId.Length == 0,
				AssignmentAssigneeKind.Group => AssigneeNetId == 0 && GroupId.Length > 0 &&
				                                GroupId.Length <= MaxGroupIdLength && SlotId.Length == 0,
				AssignmentAssigneeKind.Room => AssigneeNetId == 0 && GroupId.Length == 0 && SlotId.Length == 0,
				_ => false
			};
		}

		internal void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid assignment state");
			writer.Write(TargetNetId);
			writer.Write(Cell);
			writer.Write((byte)AssigneeKind);
			writer.Write(AssigneeNetId);
			writer.Write(GroupId);
			writer.Write(SlotId);
		}

		internal static AssignmentData Deserialize(BinaryReader reader)
		{
			var data = new AssignmentData
			{
				TargetNetId = reader.ReadInt32(),
				Cell = reader.ReadInt32(),
				AssigneeKind = (AssignmentAssigneeKind)reader.ReadByte(),
				AssigneeNetId = reader.ReadInt32(),
				GroupId = reader.ReadString(),
				SlotId = reader.ReadString()
			};
			if (!data.IsWireValid())
				throw new InvalidDataException("Invalid assignment state");
			return data;
		}
	}

	public sealed class AssignmentRequestPacket : IPacket, IClientRelayable
	{
		public AssignmentData Data = new();

		public AssignmentRequestPacket()
		{
		}

		internal AssignmentRequestPacket(AssignmentData data) => Data = data;

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);
		public void Deserialize(BinaryReader reader) => Data = AssignmentData.Deserialize(reader);

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool verified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, verified) ||
			    !AssignmentSync.TryApply(Data, enforceCanAssign: true, out Assignable assignable) ||
			    !AssignmentSync.TryCapture(assignable, out AssignmentData state))
				return;
			PacketSender.SendToAllClients(new AssignmentPacket(state));
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;
	}

	public sealed class AssignmentPacket : IPacket, IHostOnlyPacket
	{
		public AssignmentData Data = new();
		public static bool IsApplying;

		public AssignmentPacket()
		{
		}

		internal AssignmentPacket(AssignmentData data) => Data = data;

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);
		public void Deserialize(BinaryReader reader) => Data = AssignmentData.Deserialize(reader);

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				AssignmentSync.TryApply(Data, enforceCanAssign: false, out _);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}

	internal static class AssignmentSync
	{
		internal static bool IsApplyingHostOutcome { get; private set; }

		internal static bool TryCapture(Assignable assignable, out AssignmentData data)
		{
			data = null;
			NetworkIdentity target = assignable?.GetComponent<NetworkIdentity>();
			if (target == null || target.NetId == 0)
				return false;

			data = new AssignmentData
			{
				TargetNetId = target.NetId,
				Cell = Grid.PosToCell(assignable.gameObject)
			};
			if (!TryCaptureAssignee(assignable.assignee, data))
				return false;
			data.SlotId = FindAssignedSlotId(assignable, assignable.assignee);
			return data.IsWireValid();
		}

		internal static bool TryApply(
			AssignmentData data,
			bool enforceCanAssign,
			out Assignable assignable)
		{
			assignable = null;
			if (data == null || !data.IsWireValid() || !TryResolveTarget(data, out assignable))
				return false;
			if (!TryResolveAssignee(assignable, data, out IAssignableIdentity assignee))
				return false;
			if (!TryResolveSpecificSlot(assignable, assignee, data, out AssignableSlotInstance specificSlot))
				return false;
			if (ShouldRequireCanAssign(enforceCanAssign) && assignee != null && !assignable.CanAssignTo(assignee))
				return false;

			Assignable target = assignable;
			RunApplying(() =>
			{
				if (assignee == null)
				{
					if (target.IsAssigned())
						target.Unassign();
				}
				else if (!AssignmentMatches(target, assignee, specificSlot))
				{
					if (target.IsAssigned())
						target.Unassign();
					target.Assign(assignee, specificSlot);
				}
			}, applyingHostOutcome: !enforceCanAssign);
			return true;
		}

		internal static bool ShouldRequireCanAssign(bool isHostRequest) => isHostRequest;

		internal static void SendRequest(Assignable assignable, IAssignableIdentity assignee,
			AssignableSlotInstance specificSlot = null)
		{
			if (!TryBuildRequest(assignable, assignee, specificSlot, out AssignmentData data))
				return;
			PacketSender.SendToAllOtherPeers(new AssignmentRequestPacket(data));
		}

		internal static void Broadcast(Assignable assignable)
		{
			if (AssignmentPacket.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !TryCapture(assignable, out AssignmentData data))
				return;
			PacketSender.SendToAllClients(new AssignmentPacket(data));
		}

		internal static bool IdentityMatches(
			int requestedNetId,
			int claimedCell,
			int registeredNetId,
			int actualCell,
			int deterministicNetId)
			=> requestedNetId != 0 && requestedNetId == registeredNetId && claimedCell == actualCell &&
			   (deterministicNetId == 0 || deterministicNetId == requestedNetId);

		private static bool TryBuildRequest(
			Assignable assignable,
			IAssignableIdentity assignee,
			AssignableSlotInstance specificSlot,
			out AssignmentData data)
		{
			data = null;
			NetworkIdentity target = assignable?.GetComponent<NetworkIdentity>();
			if (target == null || target.NetId == 0)
				return false;
			data = new AssignmentData
			{
				TargetNetId = target.NetId,
				Cell = Grid.PosToCell(assignable.gameObject),
				SlotId = specificSlot?.ID ?? ""
			};
			return TryCaptureAssignee(assignee, data) && data.IsWireValid();
		}

		private static bool TryCaptureAssignee(IAssignableIdentity assignee, AssignmentData data)
		{
			if (assignee == null)
			{
				data.AssigneeKind = AssignmentAssigneeKind.None;
				return true;
			}
			if (assignee is AssignmentGroup group)
			{
				data.AssigneeKind = AssignmentAssigneeKind.Group;
				data.GroupId = group.id;
				return true;
			}
			if (assignee is Room)
			{
				data.AssigneeKind = AssignmentAssigneeKind.Room;
				return true;
			}

			GameObject target = assignee is MinionAssignablesProxy proxy
				? proxy.GetTargetGameObject()
				: (assignee as KMonoBehaviour)?.gameObject;
			NetworkIdentity identity = target?.GetComponent<NetworkIdentity>();
			if (identity == null || identity.NetId == 0)
				return false;
			data.AssigneeKind = AssignmentAssigneeKind.Entity;
			data.AssigneeNetId = identity.NetId;
			return true;
		}

		private static bool TryResolveTarget(AssignmentData data, out Assignable assignable)
		{
			assignable = null;
			if (!NetworkIdentityRegistry.TryGet(data.TargetNetId, out NetworkIdentity identity) || identity == null)
				return false;
			assignable = identity.GetComponent<Assignable>();
			if (assignable == null)
				return false;
			int deterministicId = identity.TryGetComponent<Building>(out _)
				? NetIdHelper.GetDeterministicBuildingId(identity.gameObject)
				: 0;
			return IdentityMatches(data.TargetNetId, data.Cell, identity.NetId,
				Grid.PosToCell(identity.gameObject), deterministicId);
		}

		private static bool TryResolveAssignee(
			Assignable assignable,
			AssignmentData data,
			out IAssignableIdentity assignee)
		{
			assignee = null;
			if (data.AssigneeKind == AssignmentAssigneeKind.None)
				return true;
			if (data.AssigneeKind == AssignmentAssigneeKind.Group)
				return Game.Instance.assignmentManager.assignment_groups.TryGetValue(data.GroupId, out AssignmentGroup group) &&
				       (assignee = group) != null;
			if (data.AssigneeKind == AssignmentAssigneeKind.Room)
				return (assignee = Game.Instance.roomProber.GetRoomOfGameObject(assignable.gameObject)) != null;
			if (!NetworkIdentityRegistry.TryGet(data.AssigneeNetId, out NetworkIdentity identity) || identity == null)
				return false;

			MinionIdentity minion = identity.GetComponent<MinionIdentity>();
			if (minion != null)
			{
				assignee = minion.GetSoleOwner()?.GetComponent<MinionAssignablesProxy>() ??
				           (IAssignableIdentity)minion;
				return true;
			}
			assignee = identity.GetComponent<StoredMinionIdentity>() ??
			           identity.GetComponent<IAssignableIdentity>();
			return assignee != null;
		}

		private static bool TryResolveSpecificSlot(
			Assignable assignable,
			IAssignableIdentity assignee,
			AssignmentData data,
			out AssignableSlotInstance specificSlot)
		{
			specificSlot = null;
			if (data.SlotId.Length == 0)
				return true;
			if (data.AssigneeKind != AssignmentAssigneeKind.Entity || assignee == null || assignable.slot == null)
				return false;
			Ownables owner = assignee.GetSoleOwner();
			if (owner == null)
				return false;
			if (TryFindSlot(owner.Slots, assignable.slot, data.SlotId, out specificSlot))
				return true;
			Equipment equipment = owner.GetComponent<Equipment>();
			return equipment != null && TryFindSlot(equipment.Slots, assignable.slot, data.SlotId,
				out specificSlot);
		}

		private static bool TryFindSlot(IEnumerable<AssignableSlotInstance> slots, AssignableSlot expectedSlot,
			string slotId, out AssignableSlotInstance match)
		{
			match = null;
			if (slots == null)
				return false;
			foreach (AssignableSlotInstance candidate in slots)
				if (candidate != null && ReferenceEquals(candidate.slot, expectedSlot) &&
				    string.Equals(candidate.ID, slotId, StringComparison.Ordinal))
				{
					match = candidate;
					return true;
				}
			return false;
		}

		private static string FindAssignedSlotId(Assignable assignable, IAssignableIdentity assignee)
		{
			if (assignable?.slot == null || assignee == null)
				return "";
			Ownables owner = assignee.GetSoleOwner();
			if (owner == null)
				return "";
			if (TryFindAssignedSlot(owner.Slots, assignable, out string slotId))
				return slotId;
			Equipment equipment = owner.GetComponent<Equipment>();
			return equipment != null && TryFindAssignedSlot(equipment.Slots, assignable, out slotId)
				? slotId
				: "";
		}

		private static bool TryFindAssignedSlot(IEnumerable<AssignableSlotInstance> slots,
			Assignable assignable, out string slotId)
		{
			slotId = "";
			if (slots == null)
				return false;
			foreach (AssignableSlotInstance candidate in slots)
				if (candidate != null && ReferenceEquals(candidate.slot, assignable.slot) &&
				    ReferenceEquals(candidate.assignable, assignable) &&
				    !string.IsNullOrEmpty(candidate.ID))
				{
					slotId = candidate.ID;
					return true;
				}
			return false;
		}

		private static bool AssignmentMatches(Assignable target, IAssignableIdentity assignee,
			AssignableSlotInstance specificSlot)
			=> ReferenceEquals(target.assignee, assignee) &&
			   (specificSlot == null || ReferenceEquals(specificSlot.assignable, target));

		private static void RunApplying(System.Action action, bool applyingHostOutcome)
		{
			bool previous = AssignmentPacket.IsApplying;
			bool previousOutcome = IsApplyingHostOutcome;
			AssignmentPacket.IsApplying = true;
			IsApplyingHostOutcome = applyingHostOutcome;
			try
			{
				action();
			}
			finally
			{
				AssignmentPacket.IsApplying = previous;
				IsApplyingHostOutcome = previousOutcome;
			}
		}
	}
}
