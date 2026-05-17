using Newtonsoft.Json;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Steamworks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Profiling;
using UnityEngine;
using static TUNING.BUILDINGS.UPGRADES;

namespace ONI_Together.Networking.Packets.Tools.Build
{
	public class UtilityBuildPacket : IPacket
	{
		private const int MaxPathNodeCount = 8192;
		private const int MaxMaterialTagCount = 64;

		/// <summary>
		/// Gets a value indicating whether incoming messages are currently being processed.
		/// Use in patches to prevent recursion when applying tool changes.
		/// </summary>
		public static bool ProcessingIncoming { get; private set; } = false;

		public List<BaseUtilityBuildTool.PathNode> path = [];
		public List<string> MaterialTags = [];
		public string PrefabID, FacadeID;
		public PrioritySetting Priority;

		static void SerializePathNode(ref BinaryWriter writer, ref BaseUtilityBuildTool.PathNode node)
		{
			using var _ = Profiler.Scope();

			writer.Write(node.cell);
			writer.Write(node.valid);
		}
		void DeserializePathNode(ref BinaryReader reader, ref List<BaseUtilityBuildTool.PathNode> toAdd)
		{
			using var _ = Profiler.Scope();

			var node = new BaseUtilityBuildTool.PathNode
			{
				cell = reader.ReadInt32(),
				valid = reader.ReadBoolean()
			};
			toAdd.Add(node);
		}

		public UtilityBuildPacket() { }

		public UtilityBuildPacket(string prefabId, List<BaseUtilityBuildTool.PathNode> nodes, List<string> mats, string skin)
		{
			using var _ = Profiler.Scope();

			PrefabID = prefabId ?? string.Empty;
			path = nodes ?? [];
			MaterialTags = mats ?? [];
			FacadeID = skin ?? string.Empty;

			if (PlanScreen.Instance)
				Priority = PlanScreen.Instance.GetBuildingPriority();
		}
		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(PrefabID);
			writer.Write(FacadeID);
			writer.Write(path.Count);
			if (path.Any())
			{
				for(int i = 0; i < path.Count; i++)
				{
					var node = path[i];
					SerializePathNode(ref writer, ref node);
				}
			}
			writer.Write(MaterialTags.Count);
			if (MaterialTags.Any())
			{
				foreach (var tag in MaterialTags)
				{
					writer.Write(tag);
				}
			}
			writer.Write((int)Priority.priority_class);
			writer.Write(Priority.priority_value);
		}


		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			//DebugConsole.Log("[UtilityBuildPacket] Deserializing UtilityBuildPacket");
			//DebugConsole.Log("[UtilityBuildPacket] Reading PrefabID...");
			PrefabID = reader.ReadString();
			//DebugConsole.Log("[UtilityBuildPacket] PrefabID read successfully: "+PrefabID);
			//DebugConsole.Log("[UtilityBuildPacket] Reading FacadeID...");
			FacadeID = reader.ReadString();
			//DebugConsole.Log("[UtilityBuildPacket] FacadeID read successfully: " + FacadeID);
			//DebugConsole.Log("[UtilityBuildPacket] Reading path Count...");
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxPathNodeCount)
			{
				DebugConsole.LogWarning($"[UtilityBuildPacket] Invalid path node count: {count}");
				path = [];
				MaterialTags = [];
				return;
			}
			//DebugConsole.Log("[UtilityBuildPacket] path Count read successfully: " + count);
			path = new List<BaseUtilityBuildTool.PathNode>(count);
			for (int i = 0; i < count; i++)
			{
				//DebugConsole.Log("[UtilityBuildPacket] Reading node at index "+i);
				DeserializePathNode(ref reader, ref path);
			}
			//DebugConsole.Log("[UtilityBuildPacket] Reading matCount...");
			int matCount = reader.ReadInt32();
			if (matCount < 0 || matCount > MaxMaterialTagCount)
			{
				DebugConsole.LogWarning($"[UtilityBuildPacket] Invalid material tag count: {matCount}");
				path = [];
				MaterialTags = [];
				return;
			}
			//DebugConsole.Log("[UtilityBuildPacket] matCount read successfully: " + matCount);
			MaterialTags = new List<string>(matCount);
			if (matCount > 0)
			{
				for (int i = 0; i < matCount; i++)
				{
					//DebugConsole.Log("[UtilityBuildPacket] Reading material at index " + i);
					MaterialTags.Add(reader.ReadString());
				}
			}
			//DebugConsole.Log("[UtilityBuildPacket] Reading Priority...");
			Priority = new PrioritySetting(
					(PriorityScreen.PriorityClass)reader.ReadInt32(),
					reader.ReadInt32());
			//DebugConsole.Log("[UtilityBuildPacket] Priority read successfully: " + Priority.priority_class+" - "+Priority.priority_value);
		}

		public void OnDispatched()
		{
			using var scope = Profiler.Scope();

			DebugConsole.Log("[UtilityBuildPacket] OnDispatched");
			if (path.Count == 0)
			{
				DebugConsole.LogWarning("[UtilityBuildPacket] Received empty path, ignoring.");
				return;
			}


			var def = Assets.GetBuildingDef(PrefabID);
			if (def == null)
			{
				DebugConsole.LogError($"[UtilityBuildPacket] Unknown PrefabID: {PrefabID}");
				return;
			}

			var selected_elements = MaterialTags.Select(t => TagManager.Create(t)).ToList();
			if (selected_elements.Count == 0)
			{
				selected_elements.AddRange(def.DefaultElements());
			}
			///mirrored from BuildMenu OnRecipeElementsFullySelected
			BaseUtilityBuildTool tool = def.BuildingComplete.TryGetComponent<Wire>(out _) ? WireBuildTool.Instance : UtilityBuildTool.Instance;

			if(PlanScreen.Instance?.ProductInfoScreen?.materialSelectionPanel?.PriorityScreen == null)
			{
				//DebugConsole.LogWarning("[UtilityBuildPacket] PlanScreen or PriorityScreen is null, opening PlanScreen to initialize.");
				PlanScreen.Instance.CopyBuildingOrder(def,FacadeID);
				//DebugConsole.LogWarning("[UtilityBuildPacket] Planscreen initialized, closing it again");
				PlanScreen.Instance.OnActiveToolChanged(SelectTool.Instance);
			}

			///caching existing stuff on the tool
			var cachedDef = tool.def;
			List<BaseUtilityBuildTool.PathNode> cachedPath = tool.path != null ? [.. tool.path] : [];
			IList<Tag> cachedMaterials = tool.selectedElements != null ? [.. tool.selectedElements] : [];
			var cachedMgr = tool.conduitMgr;

			IHaveUtilityNetworkMgr conduitManagerHaver = def.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>();

			tool.def = def;
			tool.path = this.path;
			tool.selectedElements = selected_elements;
			tool.conduitMgr = conduitManagerHaver.GetNetworkManager();

			ProcessingIncoming = true;
			try
			{
				//DebugConsole.Log($"[UtilityBuildPacket] Building path with {path.Count} nodes of prefab {def.PrefabID}");
				tool.BuildPath();

				foreach (BaseUtilityBuildTool.PathNode node in path)
				{
                    GameObject gameObject = Grid.Objects[node.cell, (int)def.TileLayer];
                    if (gameObject == null)
                        continue;
                    if (gameObject.TryGetComponent<Prioritizable>(out var prioritizable))
                        prioritizable?.SetMasterPriority(Priority);
                    if (gameObject.TryGetComponent<KAnimGraphTileVisualizer>(out var vis))
                    {
                        vis.UpdateConnections(vis.Connections);
                        vis.Refresh();
                    }
                }
			}
			finally
			{
				ProcessingIncoming = false;
				tool.def = cachedDef;
				tool.path = cachedPath;
				tool.selectedElements = cachedMaterials;
				tool.conduitMgr = cachedMgr;
			}
		}
	}
}
