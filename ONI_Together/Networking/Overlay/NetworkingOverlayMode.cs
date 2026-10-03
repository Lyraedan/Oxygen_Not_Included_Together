using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync;
using ONI_Together.Networking.OxySync.Components;
using Shared.OxySync;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
		public const string FILTER_VIEWPORTS = "VIEWPORTS";
		public const string FILTER_GROUPS = "GROUPS";

		public const float HIGH_ACTIVITY_THRESHOLD = 500f;
		public const float MEDIUM_ACTIVITY_THRESHOLD = 250f;
		private const float LEGEND_REFRESH_INTERVAL = 1f;

		private UniformGrid<NetworkIdentity> partition;
		private readonly HashSet<NetworkIdentity> layerTargets = new HashSet<NetworkIdentity>();
		private readonly List<NetworkIdentity> intersecting = new List<NetworkIdentity>();

		private int targetLayer;
		private int cameraLayerMask;
		private int selectionMask;

		private NetIdActivityTracker tracker;

		private static Color[] _cellInViewport;

		private bool showObjects = true;
		private bool showViewports = true;
		private static bool _showGroups = true;

		private GameObject _groupLabelContainer;
		private List<TextMeshProUGUI> _groupLabels = new List<TextMeshProUGUI>();
		private Camera _groupLabelCamera;
		private Canvas _groupLabelCanvas;
		private const int MAX_GROUP_LABELS = 256;

		private readonly Dictionary<int, GameObject> _syncIcons = new();
		private float _lastLegendRefresh;

		private static Sprite _syncIconSprite;
		private static Sprite SyncSprite
		{
			get
			{
				if (_syncIconSprite == null)
				{
					var tex = Misc.ResourceLoader.LoadEmbeddedTexture("ONI_Together.Assets.sync_icon.png");
					if (tex != null)
					{
						tex.filterMode = FilterMode.Bilinear;
						_syncIconSprite = Sprite.Create(tex,
							new Rect(0, 0, tex.width, tex.height),
							new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
					}
				}
				return _syncIconSprite;
			}
		}

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
			_lastLegendRefresh = Time.unscaledTime;
			RegisterSaveLoadListeners();
			partition = OverlayModes.Mode.PopulatePartition<NetworkIdentity>(new List<Tag>());
			if (_cellInViewport == null || _cellInViewport.Length != Grid.CellCount)
				_cellInViewport = new Color[Grid.CellCount];
			CameraController.Instance.ToggleColouredOverlayView(true);
			Camera.main.cullingMask |= cameraLayerMask;
			SelectTool.Instance.SetLayerMask(selectionMask);

			_groupLabelCamera = GameScreenManager.Instance.GetCamera(GameScreenManager.UIRenderTarget.ScreenSpaceCamera);
			_groupLabelCanvas = GameScreenManager.Instance.ssCameraCanvas?.GetComponent<Canvas>();
			_groupLabelContainer = new GameObject("GroupLabels");
			_groupLabelContainer.transform.SetParent(_groupLabelCanvas?.transform, false);

			for (int i = 0; i < MAX_GROUP_LABELS; i++)
			{
				var go = new GameObject($"GroupLabel_{i}");
				go.transform.SetParent(_groupLabelContainer.transform, false);
				var rect = go.AddComponent<RectTransform>();
				rect.sizeDelta = new Vector2(80, 16);
				var tmp = go.AddComponent<TextMeshProUGUI>();
				tmp.font = Localization.FontAsset;
				tmp.fontSize = 10;
				tmp.alignment = TextAlignmentOptions.Center;
				tmp.color = Color.white;
				tmp.raycastTarget = false;
				_groupLabels.Add(tmp);
			}
		}

		public override void Disable()
		{
			if (_groupLabelContainer != null)
			{
				UnityEngine.Object.Destroy(_groupLabelContainer);
				_groupLabelContainer = null;
			}
			_groupLabels.Clear();
			_groupLabelCamera = null;
			_groupLabelCanvas = null;

			foreach (var go in _syncIcons.Values)
				UnityEngine.Object.Destroy(go);
			_syncIcons.Clear();

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
				var green = GlobalAssets.Instance.colorSet.cropGrown;
				var yellow = GlobalAssets.Instance.colorSet.cropGrowing;
				var red = GlobalAssets.Instance.colorSet.cropHalted;

				highlights.Add(new OverlayModes.ColorHighlightCondition(
					(kmb) =>
					{
						var ni = kmb as NetworkIdentity;
						if (ni == null || tracker == null) return Color.gray;
						float bps = tracker.GetBytesPerSecond(ni.NetId);
						if (bps < 1f) return Color.gray;
						float t = Mathf.Clamp01(bps / HIGH_ACTIVITY_THRESHOLD);
						if (t < 0.5f)
							return Color.Lerp(green, yellow, t * 2f);
						return Color.Lerp(yellow, red, (t - 0.5f) * 2f);
					},
					(kmb) => kmb != null
				));
			}

			var emptyTags = new HashSet<Tag>();
			UpdateHighlightTypeOverlay(min, max, layerTargets, emptyTags,
				highlights.ToArray(), OverlayModes.BringToFrontLayerSetting.Constant, targetLayer);
			
			UpdateViewportOverlay(min, max);
			UpdateSyncIcons();
			UpdateGroupLabels(min, max);

			PacketTracker.Instance?.CalculatePps();

			if (Time.unscaledTime - _lastLegendRefresh >= LEGEND_REFRESH_INTERVAL)
			{
				_lastLegendRefresh = Time.unscaledTime;
				var legend = OverlayLegend.Instance;
				if (legend != null)
				{
					var info = legend.GetOverlayInfo(this);
					if (info != null)
					{
						legend.PopulateGeneratedLegend(info, true);
						Game.Instance.ForceOverlayUpdate();
					}
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
				_cellInViewport = new Color[Grid.CellCount];

			var viewports = WorldStateSyncer.Instance.ClientViewports;
			if (viewports.Count == 0)
				return;

			for (int y = min.y; y <= max.y; y++)
			{
				for (int x = min.x; x <= max.x; x++)
				{
					int cell = Grid.XYToCell(x, y);
					_cellInViewport[cell] = Color.clear;
					if (!Grid.IsValidCell(cell) || Grid.Solid[cell])
						continue;

					Color accumulated = Color.black;
					int count = 0;
					foreach (var kvp in viewports)
					{
						if (!MultiplayerSession.ConnectedPlayers.ContainsKey(kvp.Key))
							continue;

						var rect = kvp.Value;
						if (x >= rect.xMin && x < rect.xMax && y >= rect.yMin && y < rect.yMax)
						{
							Color cursorColor = Color.white;
							if (MultiplayerSession.PlayerCursors.TryGetValue(kvp.Key, out var cursor))
								cursorColor = cursor.CursorColor;

							accumulated += Color.Lerp(new Color(0f, 0.4f, 1f), cursorColor, 0.5f);
							count++;
						}
					}

					if (count > 0)
						_cellInViewport[cell] = accumulated / count;
				}
			}
		}

		private bool ShouldShowObject(NetworkIdentity identity)
		{
			return showObjects;
		}

		private void UpdateSyncIcons()
		{
			if (!MultiplayerSession.IsHost) return;
			if (!showObjects) return;

			var canvas = GameScreenManager.Instance?.worldSpaceCanvas;
			if (canvas == null) return;

			if (SyncSprite == null) return;

			var activeSet = new HashSet<int>();

			foreach (var identity in layerTargets)
			{
				if (identity == null || identity.NetId == 0) continue;

				var behaviours = identity.GetComponents<NetworkBehaviour>();
				bool isSyncing = behaviours.Any(b => b != null && (Time.unscaledTime - b._lastActiveSyncTime) <= 2f);

				if (!isSyncing)
					isSyncing = (NetIdActivityTracker.Instance?.GetBytesPerSecond(identity.NetId) ?? 0f) >= 1f;

				if (!isSyncing) continue;

				activeSet.Add(identity.NetId);

				int cell = Grid.PosToCell(identity.transform.GetPosition());
				if (cell < 0 || cell >= Grid.CellCount) continue;

				// Bind to grid
				//Vector3 targetPos = Grid.CellToPosCCC(cell, Grid.SceneLayer.Building) + new Vector3(0f, Grid.CellSizeInMeters * 0.35f, -0.5f);

				Vector3 targetPos = identity.transform.position + new Vector3(0f, Grid.CellSizeInMeters * 0.35f, -0.5f);
				if (_syncIcons.TryGetValue(identity.NetId, out var existingIcon))
				{
					existingIcon.transform.position = targetPos;
					continue;
				}

				var go = new GameObject($"SyncIcon_{identity.NetId}");
				go.transform.SetParent(canvas.transform, false);

				var image = go.AddComponent<Image>();
				image.sprite = SyncSprite;
				image.raycastTarget = false;

				var rect = go.GetComponent<RectTransform>();
				rect.sizeDelta = new Vector2(0.35f, 0.35f);

				go.transform.position = targetPos;

				_syncIcons[identity.NetId] = go;
			}

			var toRemove = new List<int>();
			foreach (var kvp in _syncIcons)
			{
				if (!activeSet.Contains(kvp.Key))
				{
					UnityEngine.Object.Destroy(kvp.Value);
					toRemove.Add(kvp.Key);
				}
			}
			foreach (var key in toRemove)
				_syncIcons.Remove(key);
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

            if (MultiplayerSession.IsHost)
            {
                var server = NetworkConfig.TransportServer;
                if (server != null)
                {
                    int inPps = server.IncomingPps;
                    int outPps = server.OutgoingPps;
                    float inBw = server.IncomingBandwidth;
                    float outBw = server.OutgoingBandwidth;
                    entries.Add(new LegendEntry(
                        string.Format("Packets: \u2193{0}/s  \u2191{1}/s", inPps, outPps),
                        null, Color.white, null, null, displaySprite: false));
                    entries.Add(new LegendEntry(
                        string.Format("Bandwidth: \u2193{0}/s  \u2191{1}/s",
                            Utils.FormatBytes((long)inBw), Utils.FormatBytes((long)outBw)),
                        null, Color.white, null, null, displaySprite: false));
                }
            }
            else
            {
                var client = NetworkConfig.TransportClient;
                if (client != null)
                {
                    int inPps = client.IncomingPps;
                    int outPps = client.OutgoingPps;
                    float inBw = client.IncomingBandwidth;
                    float outBw = client.OutgoingBandwidth;
                    entries.Add(new LegendEntry(
                        string.Format("Packets: \u2193{0}/s  \u2191{1}/s", inPps, outPps),
                        null, Color.white, null, null, displaySprite: false));
                    entries.Add(new LegendEntry(
                        string.Format("Bandwidth: \u2193{0}/s  \u2191{1}/s",
                            Utils.FormatBytes((long)inBw), Utils.FormatBytes((long)outBw)),
                        null, Color.white, null, null, displaySprite: false));
                }
            }

			if (showObjects)
			{
				entries.Add(new LegendEntry(string.Format("  High (≥{0}/s)", Utils.FormatBytes((long) HIGH_ACTIVITY_THRESHOLD)),
					null, GlobalAssets.Instance.colorSet.cropHalted));
				entries.Add(new LegendEntry(string.Format("  Medium (≥{0}/s <{1}/s)", Utils.FormatBytes((long) MEDIUM_ACTIVITY_THRESHOLD), Utils.FormatBytes((long) HIGH_ACTIVITY_THRESHOLD)), null,
					GlobalAssets.Instance.colorSet.cropGrowing));
				entries.Add(new LegendEntry("  Low (> 0 B/s)", null,
					GlobalAssets.Instance.colorSet.cropGrown));
				entries.Add(new LegendEntry("  Idle (No activity)", null,
					Color.gray));

				entries.Add(new LegendEntry("  Syncing (last 2s)", "OxySync actively syncing",
					Color.white, sprite: SyncSprite));
			}

			if (showViewports && MultiplayerSession.IsHost && WorldStateSyncer.Instance != null)
			{
				var viewports = WorldStateSyncer.Instance.ClientViewports;
				if (viewports.Count > 0)
				{
					entries.Add(new LegendEntry("PLAYER VIEWPORTS", null,
						new Color(0f, 0.4f, 1f), null, null, displaySprite: false));
					foreach (var kvp in viewports)
					{
						if (!MultiplayerSession.ConnectedPlayers.ContainsKey(kvp.Key))
							continue;

						var player = MultiplayerSession.GetPlayer(kvp.Key);
						string name = player?.PlayerName ?? kvp.Key.ToString();
						var r = kvp.Value;

						Color cursorColor = Color.white;
						if (MultiplayerSession.PlayerCursors.TryGetValue(kvp.Key, out var cursor))
							cursorColor = cursor.CursorColor;
						Color playerViewportColor = Color.Lerp(new Color(0f, 0.4f, 1f), cursorColor, 0.5f);

						entries.Add(new LegendEntry(string.Format("{0}: ({1},{2})-({3},{4})",
							name, r.xMin, r.yMin, r.xMax, r.yMax),
							null, playerViewportColor, null, null, displaySprite: false));
					}
				}
			}
			
			/* Don't need this atm
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
						string.Format("{0}: {1}, {2:F1}ms", metric.Name,
							Utils.FormatBytes(metric.LastPacketBytes), metric.LastDurationMs),
						null, Color.white, null, null, displaySprite: false));
				}
			}*/

			return entries;
		}

		public override ToolParameterMenu.ToggleData[] CreateDefaultFilters()
		{
			return new[]
			{
				new ToolParameterMenu.ToggleData(ToolParameterMenu.FILTERLAYERS.ALL, ToolParameterMenu.ToggleState.On),
				new ToolParameterMenu.ToggleData(FILTER_OBJECTS, ToolParameterMenu.ToggleState.On),
				new ToolParameterMenu.ToggleData(FILTER_VIEWPORTS, ToolParameterMenu.ToggleState.On),
				new ToolParameterMenu.ToggleData(FILTER_GROUPS, ToolParameterMenu.ToggleState.On),
			};
		}

		public override void OnFiltersChanged()
		{
			showObjects = InFilter(FILTER_OBJECTS, legendFilters);
			showViewports = InFilter(FILTER_VIEWPORTS, legendFilters);
			_showGroups = InFilter(FILTER_GROUPS, legendFilters);
		}

		private void UpdateGroupLabels(Vector2 min, Vector2 max)
		{
			if (_groupLabelContainer == null || _groupLabelCamera == null) return;
			if (ClusterManager.Instance == null) return;

			float planeZ = _groupLabelCanvas?.planeDistance ?? 10f;
			int worldId = ClusterManager.Instance.activeWorldId;
			if (worldId < 0) return;

			int cs = WorldChunkHelper.ChunkSize;
			int scx = (int)min.x / cs;
			int scy = (int)min.y / cs;
			int ecx = (int)max.x / cs;
			int ecy = (int)max.y / cs;

			int idx = 0;
			for (int cy = scy; cy <= ecy; cy++)
			{
				for (int cx = scx; cx <= ecx; cx++)
				{
					if (idx >= _groupLabels.Count) break;

					int groupId = WorldChunkHelper.GetGroupId(worldId, cx, cy);

					int centerX = cx * cs + cs / 2;
					int centerY = cy * cs + cs / 2;
					int cell = Grid.XYToCell(centerX, centerY);
					if (!Grid.IsValidCell(cell)) continue;

					Vector3 worldPos = Grid.CellToPos2D(cell);
					Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
					if (screenPos.z < 0f) continue;
					screenPos.z = planeZ;

					var tmp = _groupLabels[idx++];
					tmp.gameObject.SetActive(true);
					tmp.transform.position = _groupLabelCamera.ScreenToWorldPoint(screenPos);
					tmp.text = $"Interest Group:\n{groupId}"; // : {OxySyncManager.GetBehaviourCountInGroup(groupId)}
				}
			}
			for (int i = idx; i < _groupLabels.Count; i++)
				_groupLabels[i].gameObject.SetActive(false);
		}

		private static Color GetChunkColor(int groupId)
		{
			int h = groupId * 1234567 + 7654321;
			int h2 = h >> 8;
			float r = ((h >> 16) & 0xFF) / 255f * 0.25f + 0.05f;
			float g = ((h2 >> 16) & 0xFF) / 255f * 0.25f + 0.05f;
			float b = ((h >> 0) & 0xFF) / 255f * 0.25f + 0.05f;
			return new Color(r, g, b, 0.2f);
		}

		public static Color GetCellColor(SimDebugView _, int cell)
		{
			if (!Grid.IsValidCell(cell))
				return Color.black;
			if (Grid.Solid[cell])
				return Color.black;
			if (!Grid.IsVisible(cell))
				return Color.black;

			Color viewportColor = _cellInViewport != null ? _cellInViewport[cell] : Color.clear;
			bool inViewport = viewportColor.a > 0f;

			Color result = Color.black;

			if (_showGroups)
			{
				byte worldId = Grid.WorldIdx[cell];
				if (worldId != byte.MaxValue)
					result = GetChunkColor(WorldChunkHelper.GetGroupId(worldId, cell));
			}

			if (inViewport)
			{
				result = Color.Lerp(result, viewportColor, 0.4f);
				result.a = 0.4f;
			}

			return result;
		}
	}
}
