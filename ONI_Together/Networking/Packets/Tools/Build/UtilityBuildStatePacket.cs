using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Build
{
	public sealed class UtilityBuildOutcome
	{
		public int Cell;
		public BuildPlacementKind Kind;
		public int NetId;
		public ulong LifecycleRevision;
	}

	public sealed class UtilityBuildStatePacket : IPacket, IHostOnlyPacket
	{
		public string PrefabID = string.Empty;
		public string FacadeID = BuildAuthority.DefaultFacade;
		public List<int> Cells = [];
		public List<string> MaterialTags = [];
		public int PriorityClass;
		public int PriorityValue;
		public int ObjectLayer;
		public bool InstantBuild;
		public List<UtilityBuildOutcome> Outcomes = [];

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			ValidateWire();
			UtilityBuildWire.WriteRequest(writer, ToRequest());
			writer.Write(InstantBuild);
			writer.Write(Outcomes.Count);
			foreach (UtilityBuildOutcome outcome in Outcomes)
			{
				writer.Write(outcome.Cell);
				writer.Write((byte)outcome.Kind);
				writer.Write(outcome.NetId);
				writer.Write(outcome.LifecycleRevision);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			UtilityBuildPacket request = new();
			UtilityBuildWire.ReadRequest(reader, request);
			CopyFrom(request);
			InstantBuild = reader.ReadBoolean();
			int count = reader.ReadInt32();
			if (count < 0 || count > UtilityBuildAuthority.MaxPathNodeCount)
				throw new InvalidDataException("Invalid utility outcome count");
			Outcomes = new List<UtilityBuildOutcome>(count);
			for (int index = 0; index < count; index++)
			{
				Outcomes.Add(new UtilityBuildOutcome
				{
					Cell = reader.ReadInt32(),
					Kind = (BuildPlacementKind)reader.ReadByte(),
					NetId = reader.ReadInt32(),
					LifecycleRevision = reader.ReadUInt64(),
				});
			}
			ValidateWire();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				UtilityBuildAuthority.TryApply(this);
		}

		internal static UtilityBuildStatePacket FromRequest(
			UtilityBuildPacket request,
			bool instantBuild,
			List<UtilityBuildOutcome> outcomes)
		{
			var state = new UtilityBuildStatePacket
			{
				InstantBuild = instantBuild,
				Outcomes = outcomes ?? []
			};
			state.CopyFrom(request);
			return state;
		}

		internal UtilityBuildPacket ToRequest()
			=> new()
			{
				PrefabID = PrefabID,
				FacadeID = FacadeID,
				Cells = Cells == null ? [] : [.. Cells],
				MaterialTags = MaterialTags == null ? [] : [.. MaterialTags],
				PriorityClass = PriorityClass,
				PriorityValue = PriorityValue,
				ObjectLayer = ObjectLayer
			};

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		private void CopyFrom(UtilityBuildPacket request)
		{
			PrefabID = request.PrefabID;
			FacadeID = BuildAuthority.NormalizeFacade(request.FacadeID);
			Cells = request.Cells == null ? [] : [.. request.Cells];
			MaterialTags = request.MaterialTags == null ? [] : [.. request.MaterialTags];
			PriorityClass = request.PriorityClass;
			PriorityValue = request.PriorityValue;
			ObjectLayer = request.ObjectLayer;
		}

		private void ValidateWire()
		{
			ToRequest().ValidateWire();
			if (Outcomes == null || Outcomes.Count > Cells.Count)
				throw new InvalidDataException("Invalid utility outcome list");
			var path = new HashSet<int>(Cells);
			var seen = new HashSet<int>();
			foreach (UtilityBuildOutcome outcome in Outcomes)
			{
				if (outcome == null || !path.Contains(outcome.Cell) || !seen.Add(outcome.Cell) ||
				    outcome.Kind < BuildPlacementKind.Queued ||
				    outcome.Kind > BuildPlacementKind.CompletedReplacement)
					throw new InvalidDataException("Invalid utility outcome entry");
				if ((outcome.NetId == 0) != (outcome.LifecycleRevision == 0))
					throw new InvalidDataException("Utility outcome has incomplete lifecycle identity");
				bool completed = outcome.Kind is BuildPlacementKind.Completed or BuildPlacementKind.CompletedReplacement;
				if (completed != InstantBuild)
					throw new InvalidDataException("Utility outcome disagrees with host instant-build policy");
			}
		}
	}

	internal static partial class UtilityBuildAuthority
	{
		internal static UtilityBuildCapture Capture(UtilityBuildPacket request, BuildingDef def)
		{
			var capture = new UtilityBuildCapture { Request = request };
			foreach (int cell in request.Cells)
			{
				capture.TilesBefore[cell] = Grid.Objects[cell, (int)def.TileLayer];
				if (def.ReplacementLayer != global::ObjectLayer.NumLayers)
					capture.ReplacementsBefore[cell] = Grid.Objects[cell, (int)def.ReplacementLayer];
			}
			return capture;
		}

		internal static UtilityBuildCapture Capture(BaseUtilityBuildTool tool)
		{
			if (tool?.def == null || tool.path == null || tool.path.Count == 0 || tool.selectedElements == null)
				return null;
			PrioritySetting priority = PlanScreen.Instance != null
				? PlanScreen.Instance.GetBuildingPriority()
				: default;
			var request = new UtilityBuildPacket(
				tool.def, tool.path, tool.selectedElements, priority, tool.facadeID);
			return Capture(request, tool.def);
		}

		internal static bool TryCaptureOutcome(
			UtilityBuildCapture capture,
			out UtilityBuildStatePacket state)
		{
			state = null;
			if (capture?.Request == null || !TryResolve(capture.Request, out BuildingDef def,
				    out _, out PrioritySetting priority, out _))
				return false;
			List<UtilityBuildOutcome> outcomes = CaptureOutcomes(capture, def, priority);
			state = UtilityBuildStatePacket.FromRequest(
				capture.Request, BuildAuthority.GetHostInstantBuildPolicy(), outcomes);
			return true;
		}

		internal static List<UtilityBuildOutcome> CaptureOutcomes(
			UtilityBuildCapture capture,
			BuildingDef def,
			PrioritySetting priority)
		{
			var outcomes = new List<UtilityBuildOutcome>();
			foreach (int cell in capture.Request.Cells)
			{
				GameObject tile = Grid.Objects[cell, (int)def.TileLayer];
				capture.TilesBefore.TryGetValue(cell, out GameObject tileBefore);
				bool replacement = false;
				GameObject placed = tile != null && tile != tileBefore && IsDefinition(tile, def) ? tile : null;
				if (placed == null && def.ReplacementLayer != global::ObjectLayer.NumLayers)
				{
					GameObject candidate = Grid.Objects[cell, (int)def.ReplacementLayer];
					capture.ReplacementsBefore.TryGetValue(cell, out GameObject replacementBefore);
					if (candidate != null && candidate != replacementBefore && IsDefinition(candidate, def))
					{
						placed = candidate;
						replacement = true;
					}
				}
				if (placed == null)
					continue;
				BuildAuthority.SetPriority(placed, priority);
				bool complete = placed.GetComponent<BuildingComplete>() != null;
				var outcome = new UtilityBuildOutcome
				{
					Cell = cell,
					Kind = (complete, replacement) switch
					{
						(true, true) => BuildPlacementKind.CompletedReplacement,
						(true, false) => BuildPlacementKind.Completed,
						(false, true) => BuildPlacementKind.QueuedReplacement,
						_ => BuildPlacementKind.Queued
					}
				};
				NetworkIdentity identity = placed.GetNetIdentity();
				if (identity != null && identity.NetId != 0)
				{
					outcome.NetId = identity.NetId;
					outcome.LifecycleRevision =
						NetworkIdentityRegistry.GetLastLifecycleRevision(identity.NetId);
				}
				outcomes.Add(outcome);
			}
			return outcomes;
		}

		internal static bool TryApply(UtilityBuildStatePacket state)
		{
			UtilityBuildPacket request = state.ToRequest();
			if (!TryResolve(request, out BuildingDef def, out _, out _, out _))
				return false;
			bool applied = true;
			foreach (UtilityBuildOutcome outcome in state.Outcomes)
			{
				var buildRequest = new BuildPacket
				{
					PrefabID = state.PrefabID,
					Cell = outcome.Cell,
					Orientation = Orientation.Neutral,
					MaterialTags = [.. state.MaterialTags],
					PriorityClass = state.PriorityClass,
					PriorityValue = state.PriorityValue,
					ObjectLayer = state.ObjectLayer,
					FacadeID = state.FacadeID
				};
				BuildStatePacket buildState = BuildStatePacket.FromRequest(
					buildRequest, outcome.Kind, state.InstantBuild);
				buildState.NetId = outcome.NetId;
				buildState.LifecycleRevision = outcome.LifecycleRevision;
				applied &= BuildAuthority.TryApply(buildState);
			}
			RefreshConnections(def, state.Cells, state.Outcomes.Select(outcome => outcome.Cell));
			return applied;
		}

		private static void RefreshConnections(
			BuildingDef def,
			IReadOnlyList<int> path,
			IEnumerable<int> changedCells)
		{
			IHaveUtilityNetworkMgr owner = def.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>();
			IUtilityNetworkMgr manager = owner?.GetNetworkManager();
			if (manager == null)
				return;
			ApplyPathConnections(def, manager, path);
			var cells = new HashSet<int>();
			foreach (int cell in changedCells)
			{
				cells.Add(cell);
				cells.Add(Grid.CellLeft(cell));
				cells.Add(Grid.CellRight(cell));
				cells.Add(Grid.CellAbove(cell));
				cells.Add(Grid.CellBelow(cell));
			}
			foreach (int cell in cells)
			{
				if (!Grid.IsValidCell(cell))
					continue;
				GameObject gameObject = Grid.Objects[cell, (int)def.TileLayer];
				if (gameObject == null && def.ReplacementLayer != global::ObjectLayer.NumLayers)
					gameObject = Grid.Objects[cell, (int)def.ReplacementLayer];
				if (!NetworkIdentityRegistry.IsAvailableBindingCandidate(gameObject))
					continue;
				IUtilityItem utility = gameObject?.GetComponent<KAnimGraphTileVisualizer>();
				if (utility != null)
				{
					UtilityConnections connections = utility.Connections |
						manager.GetConnections(cell, is_physical_building: false);
					utility.Connections = connections;
					if (gameObject.GetComponent<BuildingComplete>() != null)
						utility.UpdateConnections(connections);
				}
				TileVisualizer.RefreshCell(cell, def.TileLayer, def.ReplacementLayer);
			}
		}

		private static void ApplyPathConnections(
			BuildingDef def,
			IUtilityNetworkMgr manager,
			IReadOnlyList<int> path)
		{
			bool isWire = def.BuildingComplete.GetComponent<Wire>() != null;
			for (int index = 1; index < path.Count; index++)
			{
				int previous = path[index - 1];
				int current = path[index];
				UtilityConnections forward = UtilityConnectionsExtensions.DirectionFromToCell(previous, current);
				if (forward == 0)
					continue;
				UtilityConnections backward = forward.InverseDirection();
				if (!isWire &&
				    (!manager.CanAddConnection(forward, previous, false, out _) ||
				     !manager.CanAddConnection(backward, current, false, out _)))
					continue;
				manager.AddConnection(forward, previous, is_physical_building: false);
				manager.AddConnection(backward, current, is_physical_building: false);
			}
		}

		private static bool IsDefinition(GameObject gameObject, BuildingDef def)
		{
			Building building = gameObject?.GetComponent<Building>();
			return building?.Def == def;
		}
	}
}
