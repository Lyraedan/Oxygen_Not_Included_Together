using ONI_MP.DebugTools;
using ONI_MP.Misc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
namespace ONI_MP.Networking
{
	/// <summary>
	/// KNOWN ISSUE: If the simulation is paused, other player visualizers act strange
	/// KNOWN ISSUE: Ladders visual seems to act inconsistently, sometimes it'll appear, sometimes it won't
	/// </summary>
	public class PlayerBuildingVisualizer
	{
		public enum VisualizerType
		{
			BUILDING,
			UTILITY,
			TILE,

			INVALID = -1
		}

		private GameObject visualizer;
		private string lastPrefabId = string.Empty;

		private Color _color = Color.white;

		public Color Color
		{
			get => _color;
			set
			{
				if (_color != value)
				{
					visualColor = Color.Lerp(value, Color.white, 0.75f);
					visualColorInvalid = Color.Lerp(value, Color.red, 0.75f);
				}
				_color = value;
			}
		} // Base color

		private VisualizerType _visualizerType = VisualizerType.BUILDING;
		private Orientation CurrentOrientation;
		private BuildingDef CurrentDef;

		private Color currentColor = Color.white; // Color based on if the tile is valid or not
		private Color visualColor, visualColorInvalid;



		private int _cell = Grid.InvalidCell;
		public int Cell
		{
			set
			{
				if (_cell == value)
					return;
				OnCellChanged?.Invoke(value);
				_cell = value;
			}
			get
			{
				return _cell;
			}
		}

		public System.Action<int> OnCellChanged; // Leave this incase we want to do something with it later


		VisualizerType DermineBuildingType(string prefabId)
		{
			var def = Assets.GetBuildingDef(prefabId);
			if (def == null || def.BuildingPreview == null)
				return VisualizerType.INVALID;

			if (def.IsTilePiece
				&& !def.BuildingComplete.TryGetComponent<Door>(out _)
				&& def.TileLayer != ObjectLayer.LadderTile)
			{
				if (def.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>() != null)
				{
					return VisualizerType.UTILITY;
				}
				else
				{
					return VisualizerType.TILE;
				}
			}
			else
			{
				return VisualizerType.BUILDING;
			}
		}
		void InstantiateNewVisualizer(Vector3 targetPos)
		{
			int posCell = Grid.PosToCell(targetPos);
			Vector3 pos = Grid.CellToPosCBC(posCell, CurrentDef.SceneLayer);
			visualizer = GameUtil.KInstantiate(CurrentDef.BuildingPreview, pos, Grid.SceneLayer.Front, "OtherPlayerBuildingVisualizer", LayerMask.NameToLayer("Place"));
			visualizer.transform.SetPosition(pos);
			visualizer.SetActive(true);

			if (visualizer.TryGetComponent<Rotatable>(out var rotatable))
			{
				rotatable.SetOrientation(CurrentOrientation);
			}
			if (visualizer.TryGetComponent<KBatchedAnimController>(out var kbac))
			{
				kbac.visibilityType = KAnimControllerBase.VisibilityType.Always;
				kbac.isMovable = true;
				kbac.Offset = Vector3.zero;
				kbac.TintColour = visualColor;
				kbac.SetLayer(LayerMask.NameToLayer("Place"));
				if (CurrentDef.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>() != null)
					kbac.Play("None_place"); //default non-connected pipe
				else
					kbac.Play("place");
			}
			else
			{
				visualizer.SetLayerRecursively(LayerMask.NameToLayer("Place"));
			}
			UpdatePosition(targetPos);
		}

		public void UpdateVisualizer(string buildingPrefabId, Vector3 position, Orientation orientation, Color visualColor)
		{
			if (visualColor != Color)
			{
				Color = visualColor;
			}
			this.CurrentOrientation = orientation;

			if (lastPrefabId.Equals(buildingPrefabId) && !visualizer.IsNullOrDestroyed())
			{
				UpdatePosition(position); // Instead of updating the visualizer object update its position
				return;
			}
			//determine new vis type
			var newVisType = DermineBuildingType(buildingPrefabId);
			//cleanup tile visual of old tile
			if (newVisType != VisualizerType.TILE && _visualizerType == VisualizerType.TILE)
			{
				CleanTileVisual();
			}
			// Destroy the visualiser if nothing is selected
			if (string.IsNullOrEmpty(buildingPrefabId) || !lastPrefabId.Equals(buildingPrefabId))
			{
				if (!visualizer.IsNullOrDestroyed())
				{
					Util.KDestroyGameObject(visualizer); // Destroy the visualiser
					visualizer = null;
				}
				CurrentDef = null;
				_visualizerType = VisualizerType.INVALID;
				return;
			}

			if (_visualizerType == VisualizerType.INVALID)
				return;

			Cell = Grid.InvalidCell;
			_visualizerType = newVisType;
			BuildingDef def = Assets.GetBuildingDef(buildingPrefabId);
			if (def == CurrentDef) // Same def somehow leaked through
				return;
			CurrentDef = def;
			lastPrefabId = buildingPrefabId;
			InstantiateNewVisualizer(position);
		}

		private void UpdateBuildingVisual(int cell)
		{
			visualizer.transform.SetPosition(Grid.CellToPosCBC(cell, CurrentDef.SceneLayer));
			if (visualizer.TryGetComponent<Rotatable>(out var rotatable))
			{
				rotatable.SetOrientation(CurrentOrientation);
			}

			if (visualizer.TryGetComponent<KBatchedAnimController>(out var kbac))
			{
				UpdateVisualColor(cell);
				kbac.TintColour = visualColor;
			}
		}

		void CleanTileVisual()
		{
			if (!Grid.IsValidBuildingCell(Cell))
			{
				bool hasReplacementLayer = CurrentDef.ReplacementLayer != ObjectLayer.NumLayers;
				if (CurrentDef.isKAnimTile)
				{
					GameObject tileLayerObject = Grid.Objects[Cell, (int)CurrentDef.TileLayer];
					if (tileLayerObject == null || !tileLayerObject.TryGetComponent<Constructable>(out _))
					{
						World.Instance.blockTileRenderer.RemoveBlock(CurrentDef, false, SimHashes.Void, Cell);
					}
					GameObject replacementLayerObject = hasReplacementLayer ? null : Grid.Objects[Cell, (int)CurrentDef.ReplacementLayer];
					if (replacementLayerObject == null || replacementLayerObject == visualizer)
					{
						World.Instance.blockTileRenderer.RemoveBlock(CurrentDef, true, SimHashes.Void, Cell);
					}
				}
				if (Grid.Objects[Cell, (int)CurrentDef.TileLayer] == visualizer)
				{
					Grid.Objects[Cell, (int)CurrentDef.TileLayer] = null;
				}
				if (hasReplacementLayer && Grid.Objects[Cell, (int)CurrentDef.ReplacementLayer] == visualizer)
				{
					Grid.Objects[Cell, (int)CurrentDef.ReplacementLayer] = null;
				}
				TileVisualizer.RefreshCell(Cell, CurrentDef.TileLayer, CurrentDef.ReplacementLayer);
			}
		}
		private bool CanReplace(int cell)
		{
			if (!Grid.IsValidBuildingCell(cell) || Grid.Objects[cell, (int)CurrentDef.ObjectLayer] == null || Grid.Objects[cell, (int)CurrentDef.ReplacementLayer] != null)
			{
				return false;
			}
			return true;
		}
		void SeatTileVisual(int targetCell)
		{
			visualizer.transform.SetPosition(Grid.CellToPosCBC(targetCell, CurrentDef.SceneLayer));
			if (targetCell != -1 && Grid.IsValidBuildingCell(targetCell))
			{
				bool visualizerSeated = false;
				bool hasReplacementLayer = CurrentDef.ReplacementLayer != ObjectLayer.NumLayers;

				if (Grid.Objects[targetCell, (int)CurrentDef.TileLayer] == null)
				{
					Grid.Objects[targetCell, (int)CurrentDef.TileLayer] = visualizer;
					visualizerSeated = true;
				}

				if (CurrentDef.isKAnimTile)
				{
					GameObject tileLayerObject = Grid.Objects[targetCell, (int)CurrentDef.TileLayer];
					GameObject replacementLayerObject = hasReplacementLayer ? Grid.Objects[targetCell, (int)CurrentDef.ReplacementLayer] : null;

					if (tileLayerObject == null || tileLayerObject.GetComponent<Constructable>() == null && replacementLayerObject == null)
					{
						if (CurrentDef.BlockTileAtlas != null)
						{
							bool replacing = hasReplacementLayer && CanReplace(targetCell);
							if (Grid.Objects[targetCell, (int)CurrentDef.ReplacementLayer] == null)
							{
								World.Instance.blockTileRenderer.AddBlock(LayerMask.NameToLayer("Overlay"), CurrentDef, replacing, SimHashes.Void, targetCell);
								if (replacing && !visualizerSeated && Grid.Objects[targetCell, (int)CurrentDef.ReplacementLayer] == null)
								{
									Grid.Objects[targetCell, (int)CurrentDef.ReplacementLayer] = visualizer;
								}
							}
						}
					}
				}
				if(visualizerSeated)
					TileVisualizer.RefreshCell(targetCell, CurrentDef.TileLayer, CurrentDef.ReplacementLayer);
			}
		}

		private void UpdateTileVisual(int cell)
		{
			CleanTileVisual();
			SeatTileVisual(cell);
		}

		public void UpdatePosition(Vector3 positionTarget)
		{
			int cell = Grid.PosToCell(positionTarget);
			if (cell != Grid.InvalidCell && cell != Cell)
			{
				switch (_visualizerType)
				{
					case VisualizerType.BUILDING:
					case VisualizerType.UTILITY:
						UpdateBuildingVisual(cell);
						break;
					case VisualizerType.TILE:
						UpdateTileVisual(cell);
						break;
				}
				Cell = cell;
			}
		}

		public void UpdateVisualColor(int cell)
		{
			if (BuildingUtils.ValidCell(visualizer, CurrentDef, cell, CurrentOrientation))
			{
				currentColor = visualColor;
			}
			else
			{
				currentColor = visualColorInvalid;
			}
		}
	}
}
