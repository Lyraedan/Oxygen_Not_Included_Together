using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking.Packets.Tools.Build
{
	public class BuildCompletePacket : IPacket
	{
		private const int MaxMaterialTagCount = 64;

		public int Cell;
		public string PrefabID;
		public Orientation Orientation;
		public List<string> MaterialTags = new List<string>();
		public float Temperature;
		public string FacadeID = "DEFAULT_FACADE";
		public ObjectLayer ObjectLayer;

		// Connection direction flags for wires/pipes (like UtilityBuildPacket)
		public bool ConnectsUp;
		public bool ConnectsDown;
		public bool ConnectsLeft;
		public bool ConnectsRight;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Cell);
			writer.Write(PrefabID);
			writer.Write((int)Orientation);
			writer.Write(Temperature);
			writer.Write(FacadeID);

			writer.Write(MaterialTags.Count);
			foreach (var tag in MaterialTags)
				writer.Write(tag);

			// Write connection flags
			writer.Write(ConnectsUp);
			writer.Write(ConnectsDown);
			writer.Write(ConnectsLeft);
			writer.Write(ConnectsRight);

			writer.Write((int)ObjectLayer);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Cell = reader.ReadInt32();
			PrefabID = reader.ReadString();
			Orientation = (Orientation)reader.ReadInt32();
			Temperature = reader.ReadSingle();
			FacadeID = reader.ReadString();

			int count = reader.ReadInt32();
			if (count < 0 || count > MaxMaterialTagCount)
			{
				DebugConsole.LogWarning($"[BuildCompletePacket] Invalid material tag count: {count}");
				Cell = Grid.InvalidCell;
				MaterialTags = [];
				return;
			}
			MaterialTags = new List<string>(count);
			for (int i = 0; i < count; i++)
				MaterialTags.Add(reader.ReadString());

			// Read connection flags
			ConnectsUp = reader.ReadBoolean();
			ConnectsDown = reader.ReadBoolean();
			ConnectsLeft = reader.ReadBoolean();
			ConnectsRight = reader.ReadBoolean();

			ObjectLayer = (ObjectLayer) reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!Grid.IsValidCell(Cell))
			{
				DebugConsole.LogWarning($"[BuildCompletePacket] Invalid cell: {Cell}");
				return;
			}

			var def = Assets.GetBuildingDef(PrefabID);
			if (def == null)
			{
				DebugConsole.LogWarning($"[BuildCompletePacket] Unknown building def: {PrefabID}");
				return;
			}

			var tags = MaterialTags.Select(t => new Tag(t)).ToList();

			if (tags.Count == 0)
			{
				DebugConsole.LogWarning($"[BuildCompletePacket] No materials provided for {PrefabID} at cell {Cell}, using SandStone as fallback.");
				tags.Add(SimHashes.SandStone.CreateTag());
			}

			// Destroy ghost/constructable if it still exists
			GameObject obj = Grid.Objects[Cell, (int)def.ObjectLayer];
			if (obj != null && obj.GetComponent<Constructable>() != null)
				Util.KDestroyGameObject(obj);

			var builtObj = def.Build(
					Cell,
					Orientation,
					null,
					tags,
					Temperature,
					FacadeID,
					playsound: false,
					GameClock.Instance.GetTime()
			);

			// Apply wire/pipe connections for utility buildings
			if (builtObj != null)
			{
				ApplyUtilityConnections(builtObj, Cell);
			}

			DebugConsole.Log($"[BuildCompletePacket] Finalized {PrefabID} at cell {Cell}");
		}

		/// <summary>
		/// Applies the connection directions to the built utility object and refreshes neighbors.
		/// </summary>
		private void ApplyUtilityConnections(GameObject builtObj, int cell)
		{
			using var _ = Profiler.Scope();

			// Apply connection state to the built object
			var tileVis = builtObj.GetComponent<KAnimGraphTileVisualizer>();
			if (tileVis != null)
			{
				// Build the UtilityConnections bitmask: Left=1, Right=2, Up=4, Down=8
				UtilityConnections newConnections = (UtilityConnections)0;
				if (ConnectsLeft) newConnections |= UtilityConnections.Left;
				if (ConnectsRight) newConnections |= UtilityConnections.Right;
				if (ConnectsUp) newConnections |= UtilityConnections.Up;
				if (ConnectsDown) newConnections |= UtilityConnections.Down;

				tileVis.Connections = newConnections;
				tileVis.Refresh();

				DebugConsole.Log($"[BuildCompletePacket] Applied connections: Up={ConnectsUp}, Down={ConnectsDown}, Left={ConnectsLeft}, Right={ConnectsRight}");
			}

			// Also refresh neighboring cells to update their connections to this new building
			int[] neighborCells = new int[]
			{
				Grid.CellLeft(cell),
				Grid.CellRight(cell),
				Grid.CellAbove(cell),
				Grid.CellBelow(cell)
			};

			foreach (int neighborCell in neighborCells)
			{
				if (!Grid.IsValidCell(neighborCell)) continue;

				// Check all object layers for utility buildings
				for (int layer = 0; layer < (int)Grid.SceneLayer.SceneMAX; layer++)
				{
					var neighborObj = Grid.Objects[neighborCell, layer];
					if (neighborObj != null)
					{
						var neighborTileVis = neighborObj.GetComponent<KAnimGraphTileVisualizer>();
						if (neighborTileVis != null)
						{
							neighborTileVis.Refresh();
						}
					}
				}
			}
		}
	}
}

