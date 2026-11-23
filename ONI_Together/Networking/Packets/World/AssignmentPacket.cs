using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	/// <summary>
	/// Synchronizes building assignments (Outhouse, Lavatory, Triage Cot, Massage Table, etc.)
	/// Uses NetIDs for duplicants to ensure consistent assignment across host and clients.
	/// </summary>
	public class AssignmentPacket : IPacket
	{
		public int BuildingNetId;       // NetID of the building being assigned
		public int Cell;                // Cell location for fallback lookup
		public int AssigneeNetId;       // NetID of the duplicant being assigned (-1 for unassign)
		public string GroupId = "";     // For assignment groups like "public"

		public static bool IsApplying = false;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(BuildingNetId);
			writer.Write(Cell);
			writer.Write(AssigneeNetId);
			writer.Write(GroupId ?? "");
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			BuildingNetId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			AssigneeNetId = reader.ReadInt32();
			GroupId = reader.ReadString();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"[AssignmentPacket] Received: BuildingNetId={BuildingNetId}, Cell={Cell}, AssigneeNetId={AssigneeNetId}, GroupId={GroupId}");

			NetworkIdentity buildingIdentity = null;

			// Try to find by NetID first
			if (!NetworkIdentityRegistry.TryGet(BuildingNetId, out buildingIdentity) || buildingIdentity == null)
			{
				// Fallback: find building by cell
				if (Grid.IsValidCell(Cell))
				{
					GameObject buildingGO = Grid.Objects[Cell, (int)ObjectLayer.Building];
					if (buildingGO != null)
					{
						// Position finds a candidate; it does not license renaming it.
						//
						// An assignable is not always a building - a duplicant is assigned
						// to an atmo suit, and that suit sits inside a locker at the same
						// cell. When the id cannot be resolved, this took whatever building
						// occupied the cell and gave it the packet's id, so one number
						// meant a suit on one peer and a locker on the other.
						//
						// A building that already carries a different id is something else,
						// and this packet is not about it.
						var existing = buildingGO.GetComponent<NetworkIdentity>();
						if (existing != null && existing.NetId != 0 && existing.NetId != BuildingNetId)
						{
							DebugConsole.LogWarning(
								$"[AssignmentPacket] not renaming '{buildingGO.PrefabID()}' at cell {Cell} " +
								$"from NetId {existing.NetId} to {BuildingNetId} - it is already " +
								"addressed as something else, so this packet is about a different object.");
						}
						else
						{
							// OverrideNetId, not a field write.
							//
							// Assigning NetId directly moved the field without moving the
							// registry entry, and RegisterIdentity begins with
							// "if (IsRegistered) return" - so for a building that was already
							// registered the new id was never filed at all, leaving the object
							// unreachable at the old id and the new one alike. OverrideNetId
							// unregisters, reassigns and re-registers together.
							buildingIdentity = buildingGO.AddOrGet<NetworkIdentity>();
							buildingIdentity.OverrideNetId(BuildingNetId);
							DebugConsole.Log($"[AssignmentPacket] Resolved building by cell {Cell}, assigned NetId {BuildingNetId}");
						}
					}
				}
			}

			if (buildingIdentity == null || buildingIdentity.gameObject == null)
			{
				DebugConsole.LogWarning($"[AssignmentPacket] Building NetId {BuildingNetId} at Cell {Cell} not found.");
				return;
			}

			var assignable = buildingIdentity.gameObject.GetComponent<Assignable>();
			if (assignable == null)
			{
				DebugConsole.LogWarning($"[AssignmentPacket] Building {buildingIdentity.name} has no Assignable component.");
				return;
			}

			try
			{
				IsApplying = true;
				ApplyAssignment(assignable);
			}
			finally
			{
				IsApplying = false;
			}

			// HOST RELAY
			if (MultiplayerSession.IsHost)
			{
				PacketSender.SendToAllClients(this);
				DebugConsole.Log($"[AssignmentPacket] Host relayed assignment to all clients.");
			}
		}

		private void ApplyAssignment(Assignable assignable)
		{
			using var _ = Profiler.Scope();

			// Unassign case
			if (AssigneeNetId == -1 && string.IsNullOrEmpty(GroupId))
			{
				assignable.Unassign();
				DebugConsole.Log($"[AssignmentPacket] Unassigned {assignable.name}");
				return;
			}

			// Assignment group (e.g., "public")
			if (!string.IsNullOrEmpty(GroupId))
			{
				if (Game.Instance.assignmentManager.assignment_groups.TryGetValue(GroupId, out var group))
				{
					assignable.Assign(group);
					DebugConsole.Log($"[AssignmentPacket] Assigned {assignable.name} to group '{GroupId}'");
				}
				else
				{
					DebugConsole.LogWarning($"[AssignmentPacket] Assignment group '{GroupId}' not found.");
				}
				return;
			}

			// Duplicant assignment - find by NetID
			if (!NetworkIdentityRegistry.TryGet(AssigneeNetId, out var dupeIdentity) || dupeIdentity == null)
			{
				DebugConsole.LogWarning($"[AssignmentPacket] Assignee NetId {AssigneeNetId} not found.");
				return;
			}

			// Get the IAssignableIdentity from the duplicant
			var minionIdentity = dupeIdentity.gameObject.GetComponent<MinionIdentity>();
			if (minionIdentity != null)
			{
				// MinionIdentity needs to go through its proxy for assignments
				var proxy = minionIdentity.GetSoleOwner()?.GetComponent<MinionAssignablesProxy>();
				if (proxy != null)
				{
					assignable.Assign(proxy);
					DebugConsole.Log($"[AssignmentPacket] Assigned {assignable.name} to {minionIdentity.name} via proxy");
					return;
				}

				// Try direct assignment if proxy not found
				assignable.Assign(minionIdentity);
				DebugConsole.Log($"[AssignmentPacket] Assigned {assignable.name} to {minionIdentity.name}");
				return;
			}

			// Try StoredMinionIdentity (for frozen duplicants, etc.)
			var storedIdentity = dupeIdentity.gameObject.GetComponent<StoredMinionIdentity>();
			if (storedIdentity != null)
			{
				assignable.Assign(storedIdentity);
				DebugConsole.Log($"[AssignmentPacket] Assigned {assignable.name} to stored minion");
				return;
			}

			DebugConsole.LogWarning($"[AssignmentPacket] Could not find assignable identity on NetId {AssigneeNetId}");
		}
	}
}
