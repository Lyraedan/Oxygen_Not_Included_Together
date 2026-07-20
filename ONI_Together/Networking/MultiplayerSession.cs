using System;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.States;
using Shared.Profiling;
using System.Collections.Generic;
using Shared;
using UnityEngine;
using YamlDotNet.Core;
using Object = UnityEngine.Object;

namespace ONI_Together.Networking
{
	public static class MultiplayerSession
	{

		public static bool ShouldHostAfterLoad = false;

        /// <summary>
        /// HOST ONLY - Returns a list of connected players
		/// <para>For clients use NetworkConfig.GetConnectedClients() instead</para>
        /// </summary>
        public static readonly Dictionary<ulong, MultiplayerPlayer> ConnectedPlayers = new Dictionary<ulong, MultiplayerPlayer>();

		[API_Method]
		public static ulong LocalUserID => NetworkConfig.GetLocalID();

		[System.Obsolete] //Keep for api compatibility
		public static ulong LocalSteamID => LocalUserID;
		[System.Obsolete] //Keep for api compatibility
		public static ulong HostSteamID => HostUserID;

		[API_Method]
		public static ulong HostUserID { get; set; } = Utils.NilUlong();

		public static string ServerIp { get; set; } = "127.0.0.1";
		public static int ServerPort { get; set; } = 7777;

		private static bool _inActiveSession = false;
		public static bool InActiveSession
		{
			get => _inActiveSession;
			set
			{
				_inActiveSession = value;
				InSession = value;
				Game.Instance?.Trigger(MP_HASHES.OnInSessionChanged, value ? BoxedBools.True : BoxedBools.False);
			}
		}
		//[Obsolete] 
		[API_Method] private static bool InSession = false;
		
		public static bool SessionHasPlayers => InActiveSession && ConnectedPlayers.Count > 1;
		public static bool NotInSession => !InActiveSession;

		[API_Method]
		public static bool IsHost { get; set; } //HostUserID == LocalUserID;

		[API_Method]
		public static bool IsClient => InActiveSession && !IsHost;

		public static bool IsHostInSession => IsHost && InActiveSession;

		public static readonly Dictionary<ulong, PlayerCursor> PlayerCursors = new Dictionary<ulong, PlayerCursor>();

		public static readonly Dictionary<ulong, string> KnownPlayerNames = new Dictionary<ulong, string>();

		[API_Method]
		public static bool TryGetPlayerCursorPos(ulong playerId, out Vector3 cursorPos)
		{
			cursorPos = default;
			if(!PlayerCursors.TryGetValue(playerId, out var cursor)) 
				return false;
			cursorPos = cursor.transform.position;
			return true;
		}
		[API_Method]
		public static bool TryGetPlayerColor(ulong playerId, out Color color)
		{
			color = default;
			if (!PlayerCursors.TryGetValue(playerId, out var cursor))
				return false;
			color = cursor.PlayerColor;
			return true;
		}
		

		public static void Clear()
		{
			using var _ = Profiler.Scope();

			ConnectedPlayers.Clear();
			KnownPlayerNames.Clear();
			HostUserID = Utils.NilUlong();
			WorkProgressPatch.ClearTracking();
			RemoteProgressRegistry.ClearAll();
			DebugConsole.Log("[MultiplayerSession] Session cleared.");
		}

		public static void SetHost(ulong host)
		{
			using var _ = Profiler.Scope();

			HostUserID = host;
			DebugConsole.Log($"[MultiplayerSession] Host set to: {host}");
		}

        /// <summary>
        /// HOST ONLY - Get the multiplayer instance of the player with the given ID. Returns null if not found
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static MultiplayerPlayer GetPlayer(ulong id)
		{
			using var _ = Profiler.Scope();

			return ConnectedPlayers.TryGetValue(id, out var player) ? player : null;
		}

		public static MultiplayerPlayer LocalPlayer => GetPlayer(LocalUserID);

		public static IEnumerable<MultiplayerPlayer> AllPlayers => ConnectedPlayers.Values;

		// New player cursors are created automatically if one doesn't exist
		public static void CreateNewPlayerCursor(ulong steamID, States.CursorState cursorState, Color color)
		{
			using var _ = Profiler.Scope();

			if (PlayerCursors.ContainsKey(steamID))
				return;

			var canvasGO = GameScreenManager.Instance.ssCameraCanvas;
			if (canvasGO == null)
			{
				DebugConsole.LogError("[MultiplayerSession] ssCameraCanvas is null, cannot create cursor.");
				return;
			}

			var cursorGO = new GameObject($"Cursor_{steamID}");
			cursorGO.transform.SetParent(canvasGO.transform, false);
			cursorGO.layer = LayerMask.NameToLayer("UI");

			var playerCursor = cursorGO.AddComponent<PlayerCursor>();

			playerCursor.AssignPlayer(steamID);
			playerCursor.Init();
			playerCursor.SetState(cursorState);
			playerCursor.SetColor(color);

			PlayerCursors[steamID] = playerCursor;
			DebugConsole.Log($"[MultiplayerSession] Created new cursor for {steamID}");

			Game.Instance?.Trigger(MP_HASHES.OnPlayerCursorCreated, Boxed<ulong>.Get(steamID));
		}

		public static void CreateConnectedPlayerCursors()
		{
			//using var _ = Profiler.Scope();

			//var members = NetworkConfig.GetConnectedClients();
			//foreach (var playerId in members)
			//{
			//	if (playerId == LocalUserID)
			//		continue;

			//	if (!PlayerCursors.ContainsKey(playerId))
			//	{
			//		CreateNewPlayerCursor(playerId);
			//	}
			//}
		}

		public static void RemovePlayerCursor(ulong playerId)
		{
			using var _ = Profiler.Scope();

			if (!PlayerCursors.TryGetValue(playerId, out var cursor))
				return;

			if (cursor != null && cursor.gameObject != null)
			{
				cursor.RemoveBuildingVisualizer();
				cursor.StopAllCoroutines();
				Object.Destroy(cursor.gameObject);
			}

			PlayerCursors.Remove(playerId);
			DebugConsole.Log($"[MultiplayerSession] Removed player cursor for {playerId}");
		}

		public static void RemoveAllPlayerCursors()
		{
			using var _ = Profiler.Scope();

			foreach (var kvp in PlayerCursors)
			{
				var cursor = kvp.Value;
				if (cursor != null && cursor.gameObject != null)
				{
					cursor.RemoveBuildingVisualizer(); // Remove the building visualizer if there is one
					cursor.StopAllCoroutines();
					Object.Destroy(cursor.gameObject);
				}
			}

			PlayerCursors.Clear();
			DebugConsole.Log("[MultiplayerSession] Removed all player cursors.");
		}

		public static void RefreshAllPlayerCursors()
		{
			using var _ = Profiler.Scope();
			if(Utils.IsInGame())
			{
				RemoveAllPlayerCursors();
				CreateConnectedPlayerCursors();
			}
		}

		public static bool TryGetCursorObject(ulong steamID, out PlayerCursor cursorGO)
		{
			using var _ = Profiler.Scope();

			if (PlayerCursors.TryGetValue(steamID, out var cursor) && cursor != null)
			{
				cursorGO = cursor;
				return true;
			}

			cursorGO = null;
			return false;
		}


	}
}
