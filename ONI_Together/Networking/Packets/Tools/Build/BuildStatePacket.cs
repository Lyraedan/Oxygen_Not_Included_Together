using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Build
{
	public enum BuildPlacementKind : byte
	{
		Queued = 1,
		Completed = 2,
		QueuedReplacement = 3,
		CompletedReplacement = 4
	}

	public sealed class BuildStatePacket : IPacket, IHostOnlyPacket
	{
		public string PrefabID = string.Empty;
		public int Cell;
		public Orientation Orientation;
		public List<string> MaterialTags = [];
		public int PriorityClass;
		public int PriorityValue;
		public int ObjectLayer;
		public string FacadeID = BuildAuthority.DefaultFacade;
		public BuildPlacementKind Outcome;
		public bool InstantBuild;
		public int NetId;
		public ulong LifecycleRevision;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			ValidateWire();
			BuildWire.WriteCommon(writer, ToRequest());
			writer.Write((byte)Outcome);
			writer.Write(InstantBuild);
			writer.Write(NetId);
			writer.Write(LifecycleRevision);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			BuildPacket request = new();
			BuildWire.ReadCommon(reader, request);
			CopyFrom(request);
			Outcome = (BuildPlacementKind)reader.ReadByte();
			InstantBuild = reader.ReadBoolean();
			NetId = reader.ReadInt32();
			LifecycleRevision = reader.ReadUInt64();
			ValidateWire();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				BuildAuthority.TryApply(this);
		}

		internal static BuildStatePacket FromRequest(
			BuildPacket request,
			BuildPlacementKind outcome,
			bool instantBuild)
		{
			var state = new BuildStatePacket
			{
				Outcome = outcome,
				InstantBuild = instantBuild
			};
			state.CopyFrom(request);
			return state;
		}

		internal BuildPacket ToRequest()
			=> new()
			{
				PrefabID = PrefabID,
				Cell = Cell,
				Orientation = Orientation,
				MaterialTags = MaterialTags == null ? [] : [.. MaterialTags],
				PriorityClass = PriorityClass,
				PriorityValue = PriorityValue,
				ObjectLayer = ObjectLayer,
				FacadeID = FacadeID
			};

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal void CaptureIdentity(GameObject gameObject)
		{
			NetworkIdentity identity = gameObject?.GetNetIdentity();
			if (identity == null || identity.NetId == 0)
				return;
			NetId = identity.NetId;
			LifecycleRevision = NetworkIdentityRegistry.GetLastLifecycleRevision(NetId);
		}

		private void CopyFrom(BuildPacket request)
		{
			PrefabID = request.PrefabID;
			Cell = request.Cell;
			Orientation = request.Orientation;
			MaterialTags = request.MaterialTags == null ? [] : [.. request.MaterialTags];
			PriorityClass = request.PriorityClass;
			PriorityValue = request.PriorityValue;
			ObjectLayer = request.ObjectLayer;
			FacadeID = BuildAuthority.NormalizeFacade(request.FacadeID);
		}

		private void ValidateWire()
		{
			ToRequest().ValidateWire();
			if (Outcome < BuildPlacementKind.Queued || Outcome > BuildPlacementKind.CompletedReplacement)
				throw new InvalidDataException("Invalid build outcome kind");
			bool completed = Outcome is BuildPlacementKind.Completed or BuildPlacementKind.CompletedReplacement;
			if (completed != InstantBuild)
				throw new InvalidDataException("Build outcome disagrees with host instant-build policy");
			if ((NetId == 0) != (LifecycleRevision == 0))
				throw new InvalidDataException("Build outcome has incomplete lifecycle identity");
		}
	}

	internal static partial class BuildAuthority
	{
		internal static BuildCapture Capture(BuildTool tool, int cell)
		{
			BuildingDef def = tool?.def;
			if (def == null || tool.selectedElements == null)
				return null;
			PrioritySetting priority = PlanScreen.Instance != null
				? PlanScreen.Instance.GetBuildingPriority()
				: default;
			var capture = new BuildCapture
			{
				Request = new BuildPacket(def, cell, tool.GetBuildingOrientation,
					tool.selectedElements, priority, tool.facadeID)
			};
			if (!Grid.IsValidCell(cell))
				return capture;
			capture.ObjectBefore = Grid.Objects[cell, (int)def.ObjectLayer];
			if (def.TileLayer != global::ObjectLayer.NumLayers)
				capture.TileBefore = Grid.Objects[cell, (int)def.TileLayer];
			if (def.ReplacementLayer != global::ObjectLayer.NumLayers)
				capture.ReplacementBefore = Grid.Objects[cell, (int)def.ReplacementLayer];
			return capture;
		}

		internal static bool TryCaptureOutcome(BuildCapture capture, out BuildStatePacket state)
		{
			state = null;
			if (capture?.Request == null || !TryResolve(capture.Request, out BuildingDef def,
				    out _, out _, out _))
				return false;
			if (!TryFindChanged(def, capture, out GameObject placed, out bool replacement))
				return false;
			bool complete = placed.GetComponent<BuildingComplete>() != null;
			BuildPlacementKind kind = (complete, replacement) switch
			{
				(true, true) => BuildPlacementKind.CompletedReplacement,
				(true, false) => BuildPlacementKind.Completed,
				(false, true) => BuildPlacementKind.QueuedReplacement,
				_ => BuildPlacementKind.Queued
			};
			Prioritizable prioritizable = placed.GetComponent<Prioritizable>();
			if (prioritizable != null)
			{
				PrioritySetting actual = prioritizable.GetMasterPriority();
				capture.Request.PriorityClass = (int)actual.priority_class;
				capture.Request.PriorityValue = actual.priority_value;
			}
			state = BuildStatePacket.FromRequest(capture.Request, kind, GetHostInstantBuildPolicy());
			state.CaptureIdentity(placed);
			return true;
		}

		private static bool TryFindChanged(
			BuildingDef def,
			BuildCapture capture,
			out GameObject placed,
			out bool replacement)
		{
			placed = Grid.Objects[capture.Request.Cell, (int)def.ObjectLayer];
			replacement = false;
			if (placed != null && placed != capture.ObjectBefore && IsDefinition(placed, def))
				return true;
			if (def.TileLayer != global::ObjectLayer.NumLayers)
			{
				placed = Grid.Objects[capture.Request.Cell, (int)def.TileLayer];
				if (placed != null && placed != capture.TileBefore && IsDefinition(placed, def))
					return true;
			}
			if (def.ReplacementLayer != global::ObjectLayer.NumLayers)
			{
				placed = Grid.Objects[capture.Request.Cell, (int)def.ReplacementLayer];
				replacement = true;
				if (placed != null && placed != capture.ReplacementBefore && IsDefinition(placed, def))
					return true;
			}
			placed = null;
			return false;
		}

		private static bool TryFindMatchingOutcome(
			BuildingDef def,
			int cell,
			BuildPlacementKind kind,
			out GameObject gameObject)
		{
			bool replacement = kind is BuildPlacementKind.QueuedReplacement or BuildPlacementKind.CompletedReplacement;
			global::ObjectLayer layer = replacement ? def.ReplacementLayer : def.ObjectLayer;
			gameObject = layer == global::ObjectLayer.NumLayers ? null : Grid.Objects[cell, (int)layer];
			if (gameObject == null && !replacement && def.TileLayer != global::ObjectLayer.NumLayers)
				gameObject = Grid.Objects[cell, (int)def.TileLayer];
			if (!IsDefinition(gameObject, def))
				return false;
			if (!NetworkIdentityRegistry.IsAvailableBindingCandidate(gameObject))
			{
				RetireUnavailableOutcome(gameObject, cell, def);
				gameObject = null;
				return false;
			}
			bool complete = gameObject.GetComponent<BuildingComplete>() != null;
			return complete == (kind is BuildPlacementKind.Completed or BuildPlacementKind.CompletedReplacement);
		}

		private static bool IsDefinition(GameObject gameObject, BuildingDef def)
		{
			Building building = gameObject?.GetComponent<Building>();
			return building?.Def == def;
		}

		private static void RetireUnavailableOutcome(
			GameObject gameObject, int cell, BuildingDef def)
		{
			if (gameObject == null || gameObject.IsNullOrDestroyed())
				return;
			gameObject.DeleteObject();
			ClearGridReference(cell, def.ObjectLayer, gameObject);
			ClearGridReference(cell, def.TileLayer, gameObject);
			ClearGridReference(cell, def.ReplacementLayer, gameObject);
		}

		private static void ClearGridReference(
			int cell, global::ObjectLayer layer, GameObject gameObject)
		{
			if (layer == global::ObjectLayer.NumLayers || !Grid.IsValidCell(cell))
				return;
			if (ReferenceEquals(Grid.Objects[cell, (int)layer], gameObject))
				Grid.Objects[cell, (int)layer] = null;
		}

		internal static void SetPriority(GameObject gameObject, PrioritySetting priority)
			=> gameObject?.GetComponent<Prioritizable>()?.SetMasterPriority(priority);
	}
}
