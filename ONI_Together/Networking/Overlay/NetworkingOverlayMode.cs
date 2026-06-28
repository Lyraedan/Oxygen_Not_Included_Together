using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking.Overlay
{
	public class NetworkingOverlayMode : OverlayModes.Mode
	{
		public static readonly HashedString ID = "NetworkActivity";
		private static Sprite _overlayIcon;
		public static Sprite OverlayIcon
		{
			get
			{
				if (_overlayIcon == null)
				{
					var tex = Misc.ResourceLoader.LoadEmbeddedTexture(
						"ONI_Together.Assets.network_overlay_icon.png");
					if (tex != null)
					{
						var small = ResizeTexture(tex, 36, 36);
						UnityEngine.Object.Destroy(tex);
						small.filterMode = FilterMode.Bilinear;
						_overlayIcon = Sprite.Create(small,
							new Rect(0, 0, small.width, small.height),
							new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
					}
				}
				return _overlayIcon;
			}
		}

		private static Texture2D ResizeTexture(Texture2D source, int newWidth, int newHeight)
		{
			var rt = RenderTexture.GetTemporary(newWidth, newHeight, 0,
				RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
			Graphics.Blit(source, rt);
			var prev = RenderTexture.active;
			RenderTexture.active = rt;
			var result = new Texture2D(newWidth, newHeight, TextureFormat.ARGB32, false);
			result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
			result.Apply();
			RenderTexture.active = prev;
			RenderTexture.ReleaseTemporary(rt);
			return result;
		}
		public const string OVERLAY_ACTION = "action_overlay_network";

		public const string FILTER_OBJECTS = "OBJECTS";
		public const string FILTER_GAS_SYNC = "GASSYNC";
		public const string FILTER_LIQUID_SYNC = "LIQUIDSYNC";
		public const string FILTER_ANIM_SYNC = "ANIMSYNC";
		public const string FILTER_VIEWPORTS = "VIEWPORTS";

		private const float HIGH_ACTIVITY_THRESHOLD = 500f;
		private const float MEDIUM_ACTIVITY_THRESHOLD = 100f;

		private UniformGrid<NetworkIdentity> partition;
		private readonly HashSet<NetworkIdentity> layerTargets = new HashSet<NetworkIdentity>();
		private readonly List<NetworkIdentity> intersecting = new List<NetworkIdentity>();

		private int targetLayer;
		private int cameraLayerMask;
		private int selectionMask;

		private NetIdActivityTracker tracker;

		private static float[] _cellActivity;
		private static bool[] _cellInViewport;

		private bool showObjects = true;
		private bool showGasSync = true;
		private bool showLiquidSync = true;
		private bool showAnimSync = true;
		private bool showViewports = true;

		private readonly HashSet<int> _animSyncNetIds = new HashSet<int>();
		private bool _animSyncDataValid;

		public NetworkingOverlayMode()
		{
			targetLayer = LayerMask.NameToLayer("MaskedOverlay");
			cameraLayerMask = LayerMask.GetMask("MaskedOverlay", "MaskedOverlayBG");
			selectionMask = LayerMask.GetMask("MaskedOverlay");
		}

		public override HashedString ViewMode()
		{
			return ID;
		}

		public override string GetSoundName()
		{
			return "Logic";
		}

		public override void Enable()
		{
			legendFilters = CreateDefaultFilters();
			tracker = NetIdActivityTracker.Instance;
			RegisterSaveLoadListeners();
			partition = OverlayModes.Mode.PopulatePartition<NetworkIdentity>(new List<Tag>());
			if (_cellActivity == null || _cellActivity.Length != Grid.CellCount)
				_cellActivity = new float[Grid.CellCount];
			if (_cellInViewport == null || _cellInViewport.Length != Grid.CellCount)
				_cellInViewport = new bool[Grid.CellCount];
			CameraController.Instance.ToggleColouredOverlayView(true);
			Camera.main.cullingMask |= cameraLayerMask;
			SelectTool.Instance.SetLayerMask(selectionMask);
		}

		public override void Disable()
		{
			DisableHighlightTypeOverlay(layerTargets);
			CameraController.Instance.ToggleColouredOverlayView(false);
			Camera.main.cullingMask &= ~cameraLayerMask;
			SelectTool.Instance.ClearLayerMask();
			UnregisterSaveLoadListeners();
			if (partition != null)
			{
				partition.Clear();
				partition = null;
			}
			layerTargets.Clear();
		}

		public override void Update()
		{
			Grid.GetVisibleExtents(out var min, out var max);

			OverlayModes.Mode.RemoveOffscreenTargets(layerTargets, min, max);

			intersecting.Clear();
			if (partition != null)
			{
				partition.GetAllIntersecting(new Vector2(min.x, min.y), new Vector2(max.x, max.y), intersecting);
				foreach (var identity in intersecting)
				{
					if (identity != null && ShouldShowObject(identity))
						AddTargetIfVisible(identity, min, max, layerTargets, targetLayer);
				}
			}

			foreach (var identity in NetworkIdentityRegistry.AllIdentities)
			{
				if (identity == null || identity.NetId == 0) continue;
				if (layerTargets.Contains(identity)) continue;

				int cell = Grid.PosToCell(identity.transform.GetPosition());
				if (cell < 0 || cell >= Grid.CellCount) continue;
				Grid.CellToXY(cell, out int cx, out int cy);
				if (cx < min.x || cx > max.x || cy < min.y || cy > max.y) continue;

				if (ShouldShowObject(identity))
					layerTargets.Add(identity);
			}

			var highlights = new List<OverlayModes.ColorHighlightCondition>();
			if (showObjects)
			{
				highlights.Add(new OverlayModes.ColorHighlightCondition(
					(_) => Color.gray,
					(kmb) => kmb == null
				));
				highlights.Add(new OverlayModes.ColorHighlightCondition(
					(_) => GlobalAssets.Instance.colorSet.cropGrown,
					(kmb) => GetActivityLevel(kmb as NetworkIdentity) == ActivityLevel.Low
				));
				highlights.Add(new OverlayModes.ColorHighlightCondition(
					(_) => GlobalAssets.Instance.colorSet.cropGrowing,
					(kmb) => GetActivityLevel(kmb as NetworkIdentity) == ActivityLevel.Medium
				));
				highlights.Add(new OverlayModes.ColorHighlightCondition(
					(_) => GlobalAssets.Instance.colorSet.cropHalted,
					(kmb) => GetActivityLevel(kmb as NetworkIdentity) == ActivityLevel.High
				));
			}

			if (showAnimSync && _animSyncDataValid)
			{
				highlights.Add(new OverlayModes.ColorHighlightCondition(
					(_) => new Color(1f, 0.6f, 0f),
					(kmb) =>
					{
						var ni = kmb as NetworkIdentity;
						return ni != null && _animSyncNetIds.Contains(ni.NetId);
					}
				));
			}

			var emptyTags = new HashSet<Tag>();
			UpdateHighlightTypeOverlay(min, max, layerTargets, emptyTags,
				highlights.ToArray(), OverlayModes.BringToFrontLayerSetting.Constant, targetLayer);

			RefreshAnimSyncData();
			UpdateCellActivity(min, max);
			UpdateViewportOverlay(min, max);
		}

		private void RefreshAnimSyncData()
		{
			_animSyncNetIds.Clear();
			if (!showAnimSync || AnimSyncCoordinator.Instance == null)
			{
				_animSyncDataValid = false;
				return;
			}

			var syncers = AnimSyncCoordinator.GetTrackedSyncers();
			foreach (var syncer in syncers)
			{
				if (syncer != null && syncer.NetId != 0)
					_animSyncNetIds.Add(syncer.NetId);
			}
			_animSyncDataValid = true;
		}

		private void UpdateCellActivity(Vector2I min, Vector2I max)
		{
			if (_cellActivity == null || tracker == null)
				return;

			int layerCount = (int)ObjectLayer.NumLayers;
			for (int y = min.y; y <= max.y; y++)
			{
				for (int x = min.x; x <= max.x; x++)
				{
					int cell = Grid.XYToCell(x, y);
					if (!Grid.IsValidCell(cell))
						continue;

					float bps = 0f;
					for (int i = 0; i < layerCount; i++)
					{
						var go = Grid.Objects[cell, i];
						if (go != null)
						{
							var identity = go.GetComponent<NetworkIdentity>();
							if (identity != null)
							{
								float objBps = tracker.GetBytesPerSecond(identity.NetId);
								if (objBps > bps)
									bps = objBps;
							}
						}
					}
					_cellActivity[cell] = bps;
				}
			}
		}

		private void UpdateViewportOverlay(Vector2I min, Vector2I max)
		{
			if (!showViewports || !MultiplayerSession.IsHost || WorldStateSyncer.Instance == null)
			{
				if (_cellInViewport != null)
					Array.Clear(_cellInViewport, 0, _cellInViewport.Length);
				return;
			}

			if (_cellInViewport == null || _cellInViewport.Length != Grid.CellCount)
				_cellInViewport = new bool[Grid.CellCount];

			var viewports = WorldStateSyncer.Instance.ClientViewports;
			if (viewports.Count == 0)
				return;

			for (int y = min.y; y <= max.y; y++)
			{
				for (int x = min.x; x <= max.x; x++)
				{
					int cell = Grid.XYToCell(x, y);
					_cellInViewport[cell] = false;
					if (!Grid.IsValidCell(cell) || Grid.Solid[cell])
						continue;

					foreach (var kvp in viewports)
					{
						var rect = kvp.Value;
						if (x >= rect.xMin && x < rect.xMax && y >= rect.yMin && y < rect.yMax)
						{
							_cellInViewport[cell] = true;
							break;
						}
					}
				}
			}
		}

		private enum ActivityLevel { None, Low, Medium, High }

		private ActivityLevel GetActivityLevel(NetworkIdentity identity)
		{
			if (identity == null || tracker == null) return ActivityLevel.None;
			float bps = tracker.GetBytesPerSecond(identity.NetId);
			if (bps > HIGH_ACTIVITY_THRESHOLD) return ActivityLevel.High;
			if (bps > MEDIUM_ACTIVITY_THRESHOLD) return ActivityLevel.Medium;
			if (bps > 0f) return ActivityLevel.Low;
			return ActivityLevel.None;
		}

		private bool ShouldShowObject(NetworkIdentity identity)
		{
			if (!showObjects) return false;

			if (!showGasSync || !showLiquidSync)
			{
				var prefab = identity.GetComponent<KPrefabID>();
				if (prefab != null)
				{
					var tag = prefab.PrefabTag;
					if (!showGasSync && OverlayScreen.GasVentIDs.Contains(tag))
						return false;
					if (!showLiquidSync && OverlayScreen.LiquidVentIDs.Contains(tag))
						return false;
				}
			}
			return true;
		}

		public override void OnSaveLoadRootRegistered(SaveLoadRoot root)
		{
			if (root != null)
			{
				var identity = root.GetComponent<NetworkIdentity>();
				if (identity != null)
					partition?.Add(identity);
			}
		}

		public override void OnSaveLoadRootUnregistered(SaveLoadRoot root)
		{
			if (root != null && root.gameObject != null)
			{
				var identity = root.GetComponent<NetworkIdentity>();
				if (identity != null)
				{
					layerTargets.Remove(identity);
					partition?.Remove(identity);
				}
			}
		}

		public override List<LegendEntry> GetCustomLegendData()
		{
			int totalNetworked = NetworkIdentityRegistry.Count;
			int activeNow = tracker?.ActiveCount ?? 0;
			var entries = new List<LegendEntry>
			{
				new LegendEntry("NETWORK ACTIVITY OVERLAY", null,
					Color.white, null, null, displaySprite: false),
				new LegendEntry(string.Format("Objects: {0} networked, {1} active/sec",
					totalNetworked, activeNow), null,
					Color.white, null, null, displaySprite: false),
			};

			if (MultiplayerSession.IsClient && NetworkConfig.TransportClient != null)
			{
				int ping = NetworkConfig.TransportClient.GetPing();
				entries.Add(new LegendEntry(ping >= 0 ? string.Format("Ping: {0}ms", ping) : "Ping: --",
					null, Color.white, null, null, displaySprite: false));
			}

			if (showObjects)
			{
				entries.Add(new LegendEntry("HIGH ACTIVITY",
					string.Format("> {0} B/s", HIGH_ACTIVITY_THRESHOLD),
					GlobalAssets.Instance.colorSet.cropHalted));
				entries.Add(new LegendEntry("MEDIUM ACTIVITY",
					string.Format("> {0} B/s", MEDIUM_ACTIVITY_THRESHOLD),
					GlobalAssets.Instance.colorSet.cropGrowing));
				entries.Add(new LegendEntry("LOW ACTIVITY", "> 0 B/s",
					GlobalAssets.Instance.colorSet.cropGrown));
				entries.Add(new LegendEntry("IDLE", "No activity",
					Color.gray));
			}

			if (showAnimSync && _animSyncDataValid)
			{
				entries.Add(new LegendEntry("", null, Color.white, null, null, displaySprite: false));
				entries.Add(new LegendEntry("ANIM SYNC", null,
					new Color(1f, 0.6f, 0f), null, null, displaySprite: false));
				entries.Add(new LegendEntry(string.Format("Active: {0} objects",
					_animSyncNetIds.Count), null,
					Color.white, null, null, displaySprite: false));
			}

			if (showViewports && MultiplayerSession.IsHost && WorldStateSyncer.Instance != null)
			{
				var viewports = WorldStateSyncer.Instance.ClientViewports;
				if (viewports.Count > 0)
				{
					entries.Add(new LegendEntry("", null, Color.white, null, null, displaySprite: false));
					entries.Add(new LegendEntry("PLAYER VIEWPORTS", null,
						new Color(0f, 0.4f, 1f), null, null, displaySprite: false));
					foreach (var kvp in viewports)
					{
						var player = MultiplayerSession.GetPlayer(kvp.Key);
						string name = player?.PlayerName ?? kvp.Key.ToString();
						var r = kvp.Value;
						entries.Add(new LegendEntry(string.Format("{0}: ({1},{2})-({3},{4})",
							name, r.xMin, r.yMin, r.xMax, r.yMax),
							null, new Color(0f, 0.4f, 1f), null, null, displaySprite: false));
					}
				}
			}

			entries.Add(new LegendEntry("", null, Color.white, null, null, displaySprite: false));

			bool hasStats = false;
			foreach (var metric in SyncStats.AllMetrics)
			{
				if (metric.LastSyncTime > 0f)
				{
					if (!hasStats)
					{
						entries.Add(new LegendEntry("SYNC STATS", null,
							Color.white, null, null, displaySprite: false));
						hasStats = true;
					}
					entries.Add(new LegendEntry(
						string.Format("{0}: {1}B, {2:F1}ms", metric.Name,
							metric.LastPacketBytes, metric.LastDurationMs),
						null, Color.white, null, null, displaySprite: false));
				}
			}

			return entries;
		}

		public override ToolParameterMenu.ToggleData[] CreateDefaultFilters()
		{
			return new[]
			{
				new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.ALL, ToolParameterMenu.ToggleState.On),
				new ToolParameterMenu.ToggleData(FILTER_OBJECTS, ToolParameterMenu.ToggleState.On),
				new ToolParameterMenu.ToggleData(FILTER_GAS_SYNC, ToolParameterMenu.ToggleState.On),
				new ToolParameterMenu.ToggleData(FILTER_LIQUID_SYNC, ToolParameterMenu.ToggleState.On),
				new ToolParameterMenu.ToggleData(FILTER_ANIM_SYNC, ToolParameterMenu.ToggleState.On),
				new ToolParameterMenu.ToggleData(FILTER_VIEWPORTS, ToolParameterMenu.ToggleState.On),
			};
		}

		public override void OnFiltersChanged()
		{
			showObjects = InFilter(FILTER_OBJECTS, legendFilters);
			showGasSync = InFilter(FILTER_GAS_SYNC, legendFilters);
			showLiquidSync = InFilter(FILTER_LIQUID_SYNC, legendFilters);
			showAnimSync = InFilter(FILTER_ANIM_SYNC, legendFilters);
			showViewports = InFilter(FILTER_VIEWPORTS, legendFilters);
		}

		public static Color GetCellColor(SimDebugView _, int cell)
		{
			if (_cellActivity == null || !Grid.IsValidCell(cell))
				return Color.black;
			if (Grid.Solid[cell])
				return Color.black;

			float bps = _cellActivity[cell];
			bool inViewport = _cellInViewport != null && _cellInViewport[cell];

			if (bps <= 0f && !inViewport)
				return Color.black;

			Color result = Color.black;

			if (bps > 0f)
			{
				float intensity = Mathf.Clamp01(bps / HIGH_ACTIVITY_THRESHOLD);
				result = Color.Lerp(Color.green, Color.red, intensity);
			}

			if (inViewport)
			{
				Color viewportColor = new Color(0f, 0.4f, 1f);
				if (result == Color.black)
					result = viewportColor;
				else
					result = Color.Lerp(result, viewportColor, 0.4f);
			}

			return result;
		}
	}
}
