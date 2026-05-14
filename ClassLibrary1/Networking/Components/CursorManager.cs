using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking.Packets.Core;
using ONI_MP.Networking.States;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking.Components
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

			if (!MultiplayerSession.InSession || !MultiplayerSession.LocalUserID.IsValid())
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

			string buildToolPrefabId = string.Empty;
			Orientation buildingOrientation = Orientation.Neutral;

			var interfaceTool = PlayerController.Instance.ActiveTool;
			if (interfaceTool is BuildTool buildTool)
			{
				if (buildTool.def != null)
				{
					buildToolPrefabId = buildTool.def.PrefabID;
					buildingOrientation = buildTool.buildingOrientation;
				}
			}
			else if (interfaceTool is BaseUtilityBuildTool utilityBuildTool)
			{
				if (utilityBuildTool.def != null)
				{
					buildToolPrefabId = utilityBuildTool.def.PrefabID;
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
