using ONI_Together.DebugTools;
using ONI_Together.Misc;
using Rendering;
using Rendering.World;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static ONI_Together.STRINGS.UI.MP_CHATWINDOW;
namespace ONI_Together.Networking
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

		private GameObject _visualizer;
		private string lastPrefabId = string.Empty;

		private Color _color = Color.white;

		public Color Color
		{
			get => _color;
			set
			{
				if (_color == value)
					return;
				visualColor = Color.Lerp(value, Color.white, 0.75f);
				visualColorInvalid = Color.Lerp(value, Color.red, 0.75f);
				_color = value;
			}
		} // Base color

		private VisualizerType _visualizerType = VisualizerType.BUILDING;
		private Orientation CurrentOrientation = Orientation.Neutral;
		private BuildingDef CurrentDef = null;

		private Color currentColor = Color.white; // Color based on if the tile is valid or not
		private Color visualColor = Color.white, visualColorInvalid = Color.red;
		private bool _allowedToPlaceVis;



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

		SpriteRenderer TileSpriteRenderer;
		static Dictionary<BuildingDef, BlockTileRenderer.RenderInfo> _tileInfos = [];
		static Dictionary<BuildingDef, Sprite> _placeSprites = [];
		void SetTileRenderer()
		{
			if (TileSpriteRenderer == null)
				InstantiateTileRenderer();
			UpdateTileTexture(CurrentDef);
		}
		void MoveTileRenderer(int cell)
		{
			var pos = Grid.CellToPosCCC(cell, Grid.SceneLayer.FXFront);
			TileSpriteRenderer.transform.SetPosition(pos);
			TileSpriteRenderer.color = currentColor;
		}

		void UpdateTileTexture(BuildingDef def)
		{
			if (!_placeSprites.TryGetValue(def, out var sprite))
			{
				if (!_tileInfos.TryGetValue(def, out var renderInfo))
				{
					renderInfo = _tileInfos[def] = new BlockTileRenderer.RenderInfo(World.Instance.blockTileRenderer, (int)def.TileLayer, LayerMask.NameToLayer("Place"), def, SimHashes.Void);
				}
				var tex = renderInfo.material.mainTexture as Texture2D;
				var uv = renderInfo.atlasInfo.First().uvBox;
				float uMin = uv.x;
				float vMin = uv.y;
				float uMax = uv.z;
				float vMax = uv.w;

				UnityEngine.Rect rect = new UnityEngine.Rect(
					uMin * tex.width,
					vMin * tex.height,
					(uMax - uMin) * tex.width,
					(vMax - vMin) * tex.height
				);
				sprite = Sprite.Create(tex, rect, new(0.5f, 0.5f), 128); // 128 ppu 
			}
			TileSpriteRenderer.sprite = sprite;
		}

		void InstantiateTileRenderer()
		{
			var textureGO = new GameObject("TileRenderer");
			var renderer = textureGO.AddComponent<SpriteRenderer>();
			var mat = new Material(Shader.Find("TextMeshPro/Sprite"))
			{
				renderQueue = 4501
			};
			mat.SetInt("_ZWrite", 1);
			renderer.material = mat;
			TileSpriteRenderer = renderer;
		}


		VisualizerType DermineBuildingType(string prefabId)
		{
			var def = Assets.GetBuildingDef(prefabId);
			if (def == null || def.BuildingPreview == null)
				return VisualizerType.INVALID;

			if (def.IsTilePiece
				&& def.isKAnimTile
				&& !def.BuildingComplete.TryGetComponent<Door>(out _)
				&& def.TileLayer != ObjectLayer.LadderTile)
			{
				if (def.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>() != null)
				{
					return VisualizerType.UTILITY;
				}
				else if (!def.BuildingComplete.TryGetComponent<KBatchedAnimController>(out _))
				{
					return VisualizerType.TILE;
				}
			}
			return VisualizerType.BUILDING;
		}
		void InstantiateNewVisualizer(Vector3 targetPos)
		{
			int posCell = Grid.PosToCell(targetPos);
			Vector3 pos = Grid.CellToPosCBC(posCell, CurrentDef.SceneLayer);
			//_visualizer = GameUtil.KInstantiate(CurrentDef.BuildingPreview, pos, Grid.SceneLayer.FXFront, "OtherPlayerBuildingVisualizer", LayerMask.NameToLayer("Place"));

			_visualizer = new GameObject();
			_visualizer.SetActive(false);
			var anim = _visualizer.AddComponent<KBatchedAnimController>();
			anim.isMovable = true;
			anim.sceneLayer = Grid.SceneLayer.FXFront;
			anim.AnimFiles = CurrentDef.AnimFiles;
			anim.defaultAnim = "place";
			_visualizer.transform.SetPosition(pos);
			_visualizer.SetActive(true);
			SetSize(CurrentDef.WidthInCells, CurrentDef.HeightInCells);
			UpdateRotation();
			if (_visualizer.TryGetComponent<KBatchedAnimController>(out var kbac))
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
				_visualizer.SetLayerRecursively(LayerMask.NameToLayer("Place"));
			}
			UpdatePosition(targetPos, true);
		}
		void DestroyVisualizer()
		{
			if (!_visualizer.IsNullOrDestroyed())
			{
				Util.KDestroyGameObject(_visualizer); // Destroy the visualiser
				_visualizer = null;
			}
			RemoveTileVisual();
		}

		public void UpdateVisualizer(string buildingPrefabId, Vector3 position, Orientation orientation, Color visualColor, bool allowedToPlaceVis)
		{
			if (visualColor != Color)
			{
				Color = visualColor;
			}
			this.CurrentOrientation = orientation;
			this._allowedToPlaceVis = allowedToPlaceVis;
			UpdateVisualColor();

			if (lastPrefabId.Equals(buildingPrefabId) && !_visualizer.IsNullOrDestroyed())
			{
				UpdatePosition(position); // Instead of updating the visualizer object update its position
				return;
			}
			//determine new vis type
			var newVisType = DermineBuildingType(buildingPrefabId);
			DestroyVisualizer();
			Cell = Grid.InvalidCell;

			if (string.IsNullOrEmpty(buildingPrefabId))
			{
				CurrentDef = null;
				lastPrefabId = string.Empty;
				_visualizerType = VisualizerType.INVALID;
				return;
			}
			_visualizerType = newVisType;

			if (newVisType == VisualizerType.INVALID)
				return;

			BuildingDef def = Assets.GetBuildingDef(buildingPrefabId);
			//if (def == CurrentDef) // Same def somehow leaked through
			//	return;
			CurrentDef = def;
			lastPrefabId = buildingPrefabId;
			if (newVisType == VisualizerType.TILE)
			{
				SetTileRenderer();
				UpdateTileVisual(Grid.PosToCell(position));
			}
			else
				InstantiateNewVisualizer(position);
		}

		private void UpdateBuildingVisual(int cell)
		{
			var pos = Grid.CellToPosCBC(cell, CurrentDef.SceneLayer);
			_visualizer.transform.SetPosition(pos);
			UpdateRotation();
			if (_visualizer.TryGetComponent<KBatchedAnimController>(out var kbac))
			{
				kbac.TintColour = currentColor;
			}
		}

		private void UpdateRotation()
		{
			if (_visualizer.TryGetComponent<KBatchedAnimController>(out var kbac))
			{
				kbac.Pivot = this.GetVisualizerPivot();
				kbac.Rotation = this.GetVisualizerRotation();
				kbac.Offset = this.GetVisualizerOffset();
				kbac.FlipX = this.GetVisualizerFlipX();
				kbac.FlipY = this.GetVisualizerFlipY();
			}
		}
		#region rotatableClone

		int width, height;
		private Vector3 pivot = Vector3.zero;
		private Vector3 visualizerOffset = Vector3.zero;

		public bool GetVisualizerFlipX() => this.CurrentOrientation == Orientation.FlipH;

		public bool GetVisualizerFlipY() => this.CurrentOrientation == Orientation.FlipV;
		public float GetVisualizerRotation()
		{
			switch (CurrentDef.PermittedRotations)
			{
				case PermittedRotations.R90:
				case PermittedRotations.R360:
					return -90f * (float)this.CurrentOrientation;
				default:
					return 0.0f;
			}
		}
		public Vector3 GetVisualizerPivot()
		{
			Vector3 pivot = this.pivot;
			switch (this.CurrentOrientation)
			{
				case Orientation.FlipH:
					pivot.x = -this.pivot.x;
					break;
			}
			return pivot;
		}
		private Vector3 GetVisualizerOffset()
		{
			Vector3 visualizerOffset;
			switch (this.CurrentOrientation)
			{
				case Orientation.FlipH:
					visualizerOffset = new Vector3(-this.visualizerOffset.x, this.visualizerOffset.y, this.visualizerOffset.z);
					break;
				case Orientation.FlipV:
					visualizerOffset = new Vector3(this.visualizerOffset.x, 1f, this.visualizerOffset.z);
					break;
				default:
					visualizerOffset = this.visualizerOffset;
					break;
			}
			return visualizerOffset;
		}

		public void SetSize(int width, int height)
		{
			this.width = width;
			this.height = height;
			if (width % 2 == 0)
			{
				this.pivot = new Vector3(-0.5f, 0.5f, 0.0f);
				this.visualizerOffset = new Vector3(0.5f, 0.0f, 0.0f);
			}
			else
			{
				this.pivot = new Vector3(0.0f, 0.5f, 0.0f);
				this.visualizerOffset = Vector3.zero;
			}
		}

		#endregion

		private void RemoveTileVisual()
		{
			if (TileSpriteRenderer == null)
				return;
			TileSpriteRenderer.DeleteObject();
			TileSpriteRenderer = null;
		}

		private void UpdateTileVisual(int cell) => MoveTileRenderer(cell);

		public void UpdatePosition(Vector3 positionTarget, bool force = false)
		{
			int cell = Grid.PosToCell(positionTarget);
			if (force || cell != Grid.InvalidCell
				//&& cell != Cell
				)
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

		public void UpdateVisualColor()
		{
			if (_allowedToPlaceVis)
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
