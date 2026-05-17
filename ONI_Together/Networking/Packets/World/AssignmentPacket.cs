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
						buildingIdentity = buildingGO.AddOrGet<NetworkIdentity>();
						buildingIdentity.NetId = BuildingNetId;
						buildingIdentity.RegisterIdentity();
						DebugConsole.Log($"[AssignmentPacket] Resolved building by cell {Cell}, assigned NetId {BuildingNetId}");
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
