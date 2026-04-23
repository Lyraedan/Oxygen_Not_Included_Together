using ONI_MP.Misc;
using ONI_MP.Networking.Packets.Social;
using Shared.Profiling;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Networking.Components
{
	public class PingManager : MonoBehaviour
	{
		public static PingManager Instance { get; private set; }

		private const float PingCooldownSeconds = 1f;
		private const float TrailSendInterval = 0.1f;
		private const float TrailPointInterval = 0.03f;
		private const float DragThreshold = 0.3f;
		private const float TrailHighlightProximity = 0.8f;

		private float lastPingTime;
		private float lastTrailSendTime;
		private float lastTrailPointTime;
		private bool isDrawingTrail;
		private bool isFirstTrailBatch;
		private bool rmbDown;
		private Vector2 rmbDownWorldPos;
		private readonly List<Vector2> trailBuffer = new List<Vector2>();
		private readonly List<TrailLine> allTrails = new List<TrailLine>();
		private readonly Dictionary<ulong, TrailLine> remoteActiveStrokes = new Dictionary<ulong, TrailLine>();
		private TrailLine currentTrail;
		private TrailLine highlightedTrail;

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

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (!Utils.IsInGame())
				return;

			if (!MultiplayerSession.InSession || !MultiplayerSession.LocalUserID.IsValid())
				return;

			bool ctrlHeld = Input.GetKey(KeyCode.LeftControl);

			UpdateTrailHighlight(ctrlHeld && !isDrawingTrail && !rmbDown);

			if (ctrlHeld && Input.GetMouseButtonDown(0))
			{
				TryDeleteTrailAtCursor();
				return;
			}

			if (ctrlHeld && Input.GetMouseButtonDown(1))
			{
				rmbDown = true;
				Vector3 wp = GetWorldMousePosition();
				rmbDownWorldPos = new Vector2(wp.x, wp.y);
			}

			if (rmbDown && ctrlHeld && Input.GetMouseButton(1))
			{
				Vector3 worldPos = GetWorldMousePosition();
				Vector2 currentPos = new Vector2(worldPos.x, worldPos.y);

				if (!isDrawingTrail && Vector2.Distance(currentPos, rmbDownWorldPos) > DragThreshold)
				{
					isDrawingTrail = true;
					isFirstTrailBatch = true;
					trailBuffer.Clear();
					currentTrail = CreateNewTrail(MultiplayerSession.LocalUserID);
					trailBuffer.Add(rmbDownWorldPos);
					currentTrail.AddPoints(new List<Vector2> { rmbDownWorldPos }, CursorManager.Instance.color);
				}

				if (isDrawingTrail && Time.unscaledTime - lastTrailPointTime >= TrailPointInterval)
				{
					trailBuffer.Add(currentPos);
					currentTrail.AddPoints(new List<Vector2> { currentPos }, CursorManager.Instance.color);
					lastTrailPointTime = Time.unscaledTime;
				}

				if (isDrawingTrail && Time.unscaledTime - lastTrailSendTime >= TrailSendInterval && trailBuffer.Count > 0)
				{
					var packet = new TrailPointsPacket(new List<Vector2>(trailBuffer), isFirstTrailBatch);
					isFirstTrailBatch = false;
					trailBuffer.Clear();

					if (MultiplayerSession.IsHost)
						PacketSender.SendToAllClients(packet);
					else
						PacketSender.SendToHost(packet);

					lastTrailSendTime = Time.unscaledTime;
				}
			}

			if (rmbDown && (!Input.GetMouseButton(1) || !ctrlHeld))
			{
				if (!isDrawingTrail && Time.unscaledTime - lastPingTime >= PingCooldownSeconds)
				{
					SendPingAt(rmbDownWorldPos);
					lastPingTime = Time.unscaledTime;
				}
				else if (isDrawingTrail && trailBuffer.Count > 0)
				{
					var packet = new TrailPointsPacket(new List<Vector2>(trailBuffer), isFirstTrailBatch);
					trailBuffer.Clear();

					if (MultiplayerSession.IsHost)
						PacketSender.SendToAllClients(packet);
					else
						PacketSender.SendToHost(packet);
				}

				isDrawingTrail = false;
				currentTrail = null;
				rmbDown = false;
			}
		}

		private void UpdateTrailHighlight(bool active)
		{
			using var _ = Profiler.Scope();

			TrailLine nearest = null;

			if (active)
			{
				Vector3 worldPos = GetWorldMousePosition();
				Vector2 cursorPos = new Vector2(worldPos.x, worldPos.y);
				float bestDist = float.MaxValue;

				for (int i = allTrails.Count - 1; i >= 0; i--)
				{
					if (allTrails[i] == null)
					{
						allTrails.RemoveAt(i);
						continue;
					}

					float dist = allTrails[i].GetDistanceToLine(cursorPos);
					if (dist <= TrailHighlightProximity && dist < bestDist)
					{
						bestDist = dist;
						nearest = allTrails[i];
					}
				}
			}

			if (highlightedTrail != nearest)
			{
				if (highlightedTrail != null)
					highlightedTrail.SetHighlight(false);
				highlightedTrail = nearest;
				if (highlightedTrail != null)
					highlightedTrail.SetHighlight(true);
			}
		}

		private void TryDeleteTrailAtCursor()
		{
			using var _ = Profiler.Scope();

			Vector3 worldPos = GetWorldMousePosition();
			Vector2 clickPos = new Vector2(worldPos.x, worldPos.y);

			if (DeleteTrailAtPosition(clickPos))
			{
				var packet = new TrailDeletePacket(clickPos);
				if (MultiplayerSession.IsHost)
					PacketSender.SendToAllClients(packet);
				else
					PacketSender.SendToHost(packet);
			}
		}

		public bool DeleteTrailAtPosition(Vector2 worldPos)
		{
			using var _ = Profiler.Scope();

			for (int i = allTrails.Count - 1; i >= 0; i--)
			{
				if (allTrails[i] == null)
				{
					allTrails.RemoveAt(i);
					continue;
				}

				if (allTrails[i].IsNearWorldPosition(worldPos))
				{
					if (highlightedTrail == allTrails[i])
						highlightedTrail = null;
					Destroy(allTrails[i].gameObject);
					allTrails.RemoveAt(i);
					return true;
				}
			}
			return false;
		}

		private void SendPingAt(Vector2 pos)
		{
			using var _ = Profiler.Scope();

			Vector3 worldPos = new Vector3(pos.x, pos.y, 0f);
			var packet = new PingPacket(worldPos);

			ShowPing(MultiplayerSession.LocalUserID, worldPos, CursorManager.Instance.color);

			if (MultiplayerSession.IsHost)
				PacketSender.SendToAllClients(packet);
			else
				PacketSender.SendToHost(packet);
		}

		public void ShowPing(ulong playerID, Vector3 worldPos, Color color)
		{
			using var _ = Profiler.Scope();

			var canvasGO = GameScreenManager.Instance.ssCameraCanvas;
			if (canvasGO == null)
				return;

			KMonoBehaviour.PlaySound(GlobalAssets.GetSound("Warning"));

			string playerName = ResolvePlayerName(playerID);

			var markerGO = new GameObject($"PingMarker_{playerID}");
			markerGO.transform.SetParent(canvasGO.transform, false);
			markerGO.layer = LayerMask.NameToLayer("UI");

			var marker = markerGO.AddComponent<PingMarker>();
			marker.Init(worldPos, color, playerName);
		}

		public void AddRemoteTrailPoints(ulong playerID, List<Vector2> points, Color color, bool isNewStroke)
		{
			using var _ = Profiler.Scope();

			if (isNewStroke || !remoteActiveStrokes.TryGetValue(playerID, out var trail) || trail == null)
			{
				trail = CreateNewTrail(playerID);
				remoteActiveStrokes[playerID] = trail;
			}

			trail.AddPoints(points, color);
		}

		private TrailLine CreateNewTrail(ulong playerID)
		{
			using var _ = Profiler.Scope();

			var canvasGO = GameScreenManager.Instance.ssCameraCanvas;
			if (canvasGO == null)
				return null;

			var trailGO = new GameObject($"Trail_{playerID}");
			trailGO.transform.SetParent(canvasGO.transform, false);
			trailGO.layer = LayerMask.NameToLayer("UI");

			var trail = trailGO.AddComponent<TrailLine>();
			trail.raycastTarget = false;
			allTrails.Add(trail);
			return trail;
		}

		private string ResolvePlayerName(ulong playerID)
		{
			using var _ = Profiler.Scope();

			if (NetworkConfig.IsSteamConfig() && SteamFriends.HasFriend(playerID.AsCSteamID(), EFriendFlags.k_EFriendFlagImmediate))
				return SteamFriends.GetFriendPersonaName(playerID.AsCSteamID());

			var player = MultiplayerSession.GetPlayer(playerID);
			if (player != null)
				return player.PlayerName;

			return $"Player {playerID}";
		}

		private Vector3 GetWorldMousePosition()
		{
			using var _ = Profiler.Scope();

			Vector3 mousePos = Input.mousePosition;
			mousePos.z = 0f;
			if (Camera.main != null)
				return Camera.main.ScreenToWorldPoint(mousePos);
			return Vector3.zero;
		}
	}
}
