using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Build
{
	public sealed class UtilityBuildPacket : IPacket, IClientRelayable
	{
		public static bool ProcessingIncoming { get; private set; }

		public string PrefabID = string.Empty;
		public string FacadeID = BuildAuthority.DefaultFacade;
		public List<int> Cells = [];
		public List<string> MaterialTags = [];
		public int PriorityClass;
		public int PriorityValue;
		public int ObjectLayer;

		public UtilityBuildPacket()
		{
		}

		internal UtilityBuildPacket(
			BuildingDef def,
			IEnumerable<BaseUtilityBuildTool.PathNode> nodes,
			IEnumerable<Tag> materials,
			PrioritySetting priority,
			string facadeId)
		{
			PrefabID = def?.PrefabID ?? string.Empty;
			FacadeID = BuildAuthority.NormalizeFacade(facadeId);
			Cells = nodes?.Select(node => node.cell).ToList() ?? [];
			MaterialTags = materials?.Select(tag => tag.ToString()).ToList() ?? [];
			PriorityClass = (int)priority.priority_class;
			PriorityValue = priority.priority_value;
			ObjectLayer = (int)(def?.ObjectLayer ?? global::ObjectLayer.NumLayers);
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			ValidateWire();
			UtilityBuildWire.WriteRequest(writer, this);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			UtilityBuildWire.ReadRequest(reader, this);
			ValidateWire();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			DispatchContext context = PacketHandler.CurrentContext;
			bool verified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, verified))
				return;

			bool instantBuild = BuildAuthority.GetHostInstantBuildPolicy();
			if (!UtilityBuildAuthority.TryExecuteHost(this, instantBuild,
				    state => PacketSender.SendToAllClients(state), out string error))
				throw new InvalidDataException("Rejected utility build request: " + error);
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;

		internal void ValidateWire()
		{
			if (!BuildAuthority.IsBoundedId(PrefabID) || !BuildAuthority.IsBoundedFacade(FacadeID) ||
			    !BuildAuthority.AreMaterialTagsWireValid(MaterialTags) ||
			    !BuildAuthority.IsPriorityAllowed(PriorityClass, PriorityValue) ||
			    ObjectLayer < 0 || ObjectLayer >= (int)global::ObjectLayer.NumLayers ||
			    !UtilityBuildAuthority.IsPathWireValid(Cells))
				throw new InvalidDataException("Invalid utility build request payload");
		}

		internal static void RunProcessing(System.Action action)
		{
			bool previous = ProcessingIncoming;
			ProcessingIncoming = true;
			try
			{
				action();
			}
			finally
			{
				ProcessingIncoming = previous;
			}
		}
	}

	internal static class UtilityBuildWire
	{
		internal static void WriteRequest(BinaryWriter writer, UtilityBuildPacket packet)
		{
			writer.Write(packet.PrefabID);
			writer.Write(BuildAuthority.NormalizeFacade(packet.FacadeID));
			writer.Write(packet.Cells.Count);
			foreach (int cell in packet.Cells)
				writer.Write(cell);
			writer.Write(packet.MaterialTags.Count);
			foreach (string tag in packet.MaterialTags)
				writer.Write(tag);
			writer.Write(packet.PriorityClass);
			writer.Write(packet.PriorityValue);
			writer.Write(packet.ObjectLayer);
		}

		internal static void ReadRequest(BinaryReader reader, UtilityBuildPacket packet)
		{
			packet.PrefabID = BuildAuthority.ReadBoundedString(reader, BuildAuthority.MaxIdLength);
			packet.FacadeID = BuildAuthority.ReadBoundedString(reader, BuildAuthority.MaxIdLength);
			int count = reader.ReadInt32();
			if (count <= 0 || count > UtilityBuildAuthority.MaxPathNodeCount)
				throw new InvalidDataException("Invalid utility path count");
			packet.Cells = new List<int>(count);
			for (int index = 0; index < count; index++)
				packet.Cells.Add(reader.ReadInt32());
			packet.MaterialTags = BuildAuthority.ReadMaterialTags(reader);
			packet.PriorityClass = reader.ReadInt32();
			packet.PriorityValue = reader.ReadInt32();
			packet.ObjectLayer = reader.ReadInt32();
		}
	}

	internal sealed class UtilityBuildCapture
	{
		internal UtilityBuildPacket Request;
		internal Dictionary<int, GameObject> TilesBefore = [];
		internal Dictionary<int, GameObject> ReplacementsBefore = [];
	}

	internal static partial class UtilityBuildAuthority
	{
		internal const int MaxPathNodeCount = 8192;

		internal static bool IsPathWireValid(IReadOnlyList<int> cells)
		{
			if (cells == null || cells.Count == 0 || cells.Count > MaxPathNodeCount)
				return false;
			var seen = new HashSet<int>();
			foreach (int cell in cells)
			{
				if (!BuildAuthority.IsWireCell(cell) || !seen.Add(cell))
					return false;
			}
			return true;
		}

		internal static bool IsPathShapeValid(
			IReadOnlyList<int> cells,
			int width,
			int cellCount)
		{
			if (!IsPathWireValid(cells) || width <= 0 || cellCount <= 0)
				return false;
			for (int index = 0; index < cells.Count; index++)
			{
				int cell = cells[index];
				if (cell >= cellCount)
					return false;
				if (index == 0)
					continue;
				int previous = cells[index - 1];
				int deltaX = Math.Abs(cell % width - previous % width);
				int deltaY = Math.Abs(cell / width - previous / width);
				if (deltaX + deltaY != 1)
					return false;
			}
			return true;
		}

		internal static bool TryResolve(
			UtilityBuildPacket packet,
			out BuildingDef def,
			out List<Tag> materials,
			out PrioritySetting priority,
			out string error)
		{
			def = null;
			materials = null;
			priority = default;
			error = string.Empty;
			try
			{
				packet.ValidateWire();
			}
			catch (InvalidDataException exception)
			{
				error = exception.Message;
				return false;
			}
			if (!IsPathShapeValid(packet.Cells, Grid.WidthInCells, Grid.CellCount))
				return Fail("path is outside the current world or is not cardinally adjacent", ref error);
			def = Assets.GetBuildingDef(packet.PrefabID);
			if (def == null || packet.ObjectLayer != (int)def.ObjectLayer ||
			    def.TileLayer == global::ObjectLayer.NumLayers ||
			    def.BuildingComplete?.GetComponent<IHaveUtilityNetworkMgr>() == null)
				return Fail("unknown or non-utility prefab, or object-layer mismatch", ref error);
			if (!BuildAuthority.TryResolveMaterials(def, packet.MaterialTags, out materials))
				return Fail("material selection does not match recipe categories", ref error);
			if (!BuildAuthority.TryValidateFacade(def, packet.FacadeID))
				return Fail("facade is unavailable for prefab", ref error);
			foreach (int cell in packet.Cells)
			{
				if (!IsCellAllowed(def, cell))
					return Fail("path contains a hidden or invalid build cell", ref error);
			}
			priority = new PrioritySetting((PriorityScreen.PriorityClass)packet.PriorityClass, packet.PriorityValue);
			return true;
		}

		private static bool IsCellAllowed(BuildingDef def, int cell)
		{
			if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell))
				return false;
			GameObject objectLayer = Grid.Objects[cell, (int)def.ObjectLayer];
			if (objectLayer != null && objectLayer.GetComponent<KAnimGraphTileVisualizer>() == null)
				return false;
			GameObject tile = Grid.Objects[cell, (int)def.TileLayer];
			if (tile != null)
				return tile.GetComponent<KAnimGraphTileVisualizer>() != null;

			Vector3 position = Grid.CellToPosCBC(cell, Grid.SceneLayer.Building);
			if (def.IsValidBuildLocation(null, position, Orientation.Neutral) &&
			    def.IsValidPlaceLocation(null, position, Orientation.Neutral, out _))
				return true;
			return def.ReplacementLayer != global::ObjectLayer.NumLayers &&
			       def.GetReplacementCandidate(cell) != null &&
			       !def.IsReplacementLayerOccupied(cell) &&
			       def.IsValidBuildLocation(null, position, Orientation.Neutral, replace_tile: true) &&
			       def.IsValidPlaceLocation(null, position, Orientation.Neutral, replace_tile: true, out _);
		}

		internal static bool TryExecuteHost(
			UtilityBuildPacket request,
			bool instantBuild,
			Action<UtilityBuildStatePacket> publish,
			out string error)
		{
			if (!TryResolve(request, out BuildingDef def, out List<Tag> materials,
				    out PrioritySetting priority, out error))
				return false;
			UtilityBuildCapture capture = Capture(request, def);
			if (!RunBuildPath(def, request.Cells, materials, instantBuild, request.FacadeID, out error))
				return false;
			List<UtilityBuildOutcome> outcomes = CaptureOutcomes(capture, def, priority);
			publish(UtilityBuildStatePacket.FromRequest(request, instantBuild, outcomes));
			return true;
		}

		private static bool RunBuildPath(
			BuildingDef def,
			IReadOnlyList<int> cells,
			IList<Tag> materials,
			bool instantBuild,
			string facadeId,
			out string error)
		{
			error = string.Empty;
			BaseUtilityBuildTool tool = def.BuildingComplete.TryGetComponent<Wire>(out _)
				? WireBuildTool.Instance
				: UtilityBuildTool.Instance;
			IHaveUtilityNetworkMgr managerOwner = def.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>();
			if (tool == null || managerOwner == null)
				return Fail("utility build tool or manager is unavailable", ref error);

			BuildingDef previousDef = tool.def;
			List<BaseUtilityBuildTool.PathNode> previousPath = tool.path == null ? [] : [.. tool.path];
			IList<Tag> previousMaterials = tool.selectedElements == null ? [] : [.. tool.selectedElements];
			IUtilityNetworkMgr previousManager = tool.conduitMgr;
			string previousFacade = tool.facadeID;
			bool previousDebug = DebugHandler.InstantBuildMode;
			bool? previousSandbox = SandboxToolParameterMenu.instance?.settings == null
				? null
				: SandboxToolParameterMenu.instance.settings.InstantBuild;
			try
			{
				tool.def = def;
				tool.path = cells.Select(cell => new BaseUtilityBuildTool.PathNode { cell = cell, valid = true }).ToList();
				tool.selectedElements = materials;
				tool.conduitMgr = managerOwner.GetNetworkManager();
				tool.facadeID = BuildAuthority.NormalizeFacade(facadeId);
				DebugHandler.InstantBuildMode = instantBuild;
				if (SandboxToolParameterMenu.instance?.settings != null)
					SandboxToolParameterMenu.instance.settings.InstantBuild = instantBuild;
				UtilityBuildPacket.RunProcessing(() => tool.BuildPath());
				return true;
			}
			finally
			{
				DebugHandler.InstantBuildMode = previousDebug;
				if (previousSandbox.HasValue && SandboxToolParameterMenu.instance?.settings != null)
					SandboxToolParameterMenu.instance.settings.InstantBuild = previousSandbox.Value;
				tool.def = previousDef;
				tool.path = previousPath;
				tool.selectedElements = previousMaterials;
				tool.conduitMgr = previousManager;
				tool.facadeID = previousFacade;
			}
		}

		private static bool Fail(string message, ref string error)
		{
			error = message;
			return false;
		}
	}
}
