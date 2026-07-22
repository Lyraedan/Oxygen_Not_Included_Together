using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using Shared.Profiling;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	public class CursorManager : MonoBehaviour
	{
		public static CursorManager Instance { get; private set; }

		public static float SendInterval = 0.1f;

		private float timeSinceLastSend = 0f;

		public Color color;

		public CursorState cursorState = CursorState.NONE;

		private void Awake()
		{
			using var _ = Profiler.Scope();

			if (Instance != null)
			{
				Destroy(this);
				return;
			}

			Instance = this;
			DontDestroyOnLoad(gameObject);
		}

		private void Start()
		{
			using var _ = Profiler.Scope();

			AssignColor();
		}

		public void ResetColor()
		{
			using var _ = Profiler.Scope();

			color = Color.white;
		}

		public void AssignColor()
		{
			using var _ = Profiler.Scope();

			bool useRandom = Configuration.GetClientProperty<bool>("UseRandomPlayerColor");
			if (useRandom)
			{
				color = UnityEngine.Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.8f, 1f);
				DebugConsole.Log("[CursorManager] Setting cursor color to random color " + color.ToString());
			}
			else
			{
				Color32 set_color = Configuration.Instance.CursorColor;
				color = set_color;
				DebugConsole.Log("[CursorManager] Setting cursor color from config to " + set_color.ToString() + " | " + color.ToString());
			}
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (!Utils.IsInGame())
				return;

			if (!MultiplayerSession.InActiveSession || !MultiplayerSession.LocalUserID.IsValid())
				return;

			timeSinceLastSend += Time.unscaledDeltaTime;
			if (timeSinceLastSend >= SendInterval)
			{
				SendCursorPosition();
				timeSinceLastSend = 0f;
			}
		}
		private void SendCursorPosition()
		{
			using var _ = Profiler.Scope();

			Vector3 cursorWorldPos = GetCursorWorldPosition();

			// We do not want to lock cursor sending to a threshold as this updates the cursor position relative to the clients viewport

			// Calculate Viewport
			int minX = 0, minY = 0, maxX = 0, maxY = 0;
			if (Camera.main != null)
			{
				Camera cam = Camera.main;
				// Get corners
				Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0, 0, 0));
				Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1, 1, 0));

				minX = Grid.PosToCell(bl);
				maxX = Grid.PosToCell(tr);
				// Grid.PosToCell returns cell index, not XY.
				// We want XY coordinates to define a rectangle.

				Grid.PosToXY(bl, out int x1, out int y1);
				Grid.PosToXY(tr, out int x2, out int y2);

				minX = x1; minY = y1;
				maxX = x2; maxY = y2;
			}

			var interfaceTool = PlayerController.Instance.ActiveTool;
            
			// Building visualizer
            string buildToolPrefabId = string.Empty;
            Orientation buildingOrientation = Orientation.Neutral;
            bool allowedToPlaceBuilding = true;

            // Utility path visualizer
            bool hasUtilityPath = false;
            uint[] utilityPathData = null;
            
			if (interfaceTool is BuildTool buildTool)
			{
				if (buildTool.def != null)
				{
					buildToolPrefabId = buildTool.def.PrefabID;
					buildingOrientation = buildTool.buildingOrientation;
					allowedToPlaceBuilding = buildTool.def.IsValidPlaceLocation(buildTool.visualizer, cursorWorldPos, buildingOrientation, out var _) || buildTool.def.IsValidReplaceLocation(cursorWorldPos, buildingOrientation, buildTool.def.ReplacementLayer, buildTool.def.ObjectLayer);
				}
			}
			else if (interfaceTool is BaseUtilityBuildTool utilityBuildTool)
			{
				if (utilityBuildTool.def != null)
				{
					buildToolPrefabId = utilityBuildTool.def.PrefabID;
					allowedToPlaceBuilding = utilityBuildTool.CheckValidPathPiece(Grid.PosToCell(cursorWorldPos));

					utilityPathData = BuildingUtils.EncodeUtilityPath(utilityBuildTool.path);
					if (utilityPathData != null && utilityPathData.Length > 0)
						hasUtilityPath = true;
				}
			}

			// Area visualizer
			Vector3 areaDownPos = Vector3.zero;
			bool dragging = false;
			DragTool.Mode dragMode = DragTool.Mode.Box;
			Vector2 lengthLimit = Vector2.zero;

			if (interfaceTool is DragTool dragTool and not BuildTool and not BaseUtilityBuildTool)
			{
                dragging = dragTool.Dragging;

                if (dragging)
				{
					dragMode = dragTool.mode;
					areaDownPos = dragTool.downPos;

					if(Input.GetKey((KeyCode)Global.GetInputManager().GetDefaultController().GetInputForAction(Action.DragStraight))) {
						dragMode = DragTool.Mode.Line;
					}

                    if (dragTool is DisconnectTool disconnectTool)
                    {
                        // Disconnect tool uses Line mode
                        dragMode = DragTool.Mode.Line;
						lengthLimit = new Vector2(2, 2);
                    }
                }
			}

			var packet = new PlayerCursorPacket
			{
				PlayerID = MultiplayerSession.LocalUserID,
				Position = cursorWorldPos,
				Color = color,
				CursorState = cursorState,
				ViewMinX = minX,
				ViewMinY = minY,
				ViewMaxX = maxX,
				ViewMaxY = maxY,
				
				BuildingPrefabId = buildToolPrefabId,
				BuildingOrientation = buildingOrientation,
				BuildingAllowed = allowedToPlaceBuilding,

				Dragging = dragging,
                AreaDownPos = areaDownPos,
				DragMode = dragMode,
				LengthLimit = lengthLimit,

				HasUtilityPath = hasUtilityPath,
				UtilityPathData = utilityPathData
            };

			if (MultiplayerSession.IsHost)
			{
				PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
			}
			else
			{
				PacketSender.SendToHost(packet, PacketSendMode.Unreliable);
			}
		}

		

		private Vector3 GetCursorWorldPosition()
		{
			using var _ = Profiler.Scope();

			var camera = GameScreenManager.Instance.GetCamera(GameScreenManager.UIRenderTarget.ScreenSpaceCamera);
			if (camera == null) return Vector3.zero;

			var canvas = GameScreenManager.Instance.ssCameraCanvas?.GetComponent<Canvas>();
			var planeZ = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.planeDistance : 10f; // default fallback

			Vector3 screenPos = Input.mousePosition;
			screenPos.z = planeZ; // match the UI plane

			return camera.ScreenToWorldPoint(screenPos);
		}

	}
}
