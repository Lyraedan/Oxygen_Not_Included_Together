using ONI_Together.DebugTools;
using ONI_Together.Misc;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

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

		public static ulong LocalUserID => NetworkConfig.GetLocalID();

		[System.Obsolete] //Keep for api compatibility
		public static ulong LocalSteamID => LocalUserID;
		[System.Obsolete] //Keep for api compatibility
		public static ulong HostSteamID => HostUserID;

		public static ulong HostUserID { get; set; } = Utils.NilUlong();

		public static string ServerIp { get; set; } = "127.0.0.1";
		public static int ServerPort { get; set; } = 7777;

		private static bool transportConnected;
		private static bool retainClientWorldLoad;

		public static bool InSession
		{
			get => ResolveLogicalSession(transportConnected, retainClientWorldLoad);
			set
			{
				transportConnected = value;
				if (!value)
					RemoveAllPlayerCursors();
			}
		}

		internal static bool IsClientWorldLoadRetained => retainClientWorldLoad;
		internal static bool IsTransportConnected => transportConnected;
		public static bool SessionHasPlayers => InSession && ConnectedPlayers.Count > 1;
		public static bool NotInSession => !InSession;

		public static bool IsHost { get; set; } //HostUserID == LocalUserID;

		public static bool IsClient => ResolveClientRole(IsHost, InSession);

		public static bool IsHostInSession => IsHost && InSession;

		public static readonly Dictionary<ulong, PlayerCursor> PlayerCursors = new Dictionary<ulong, PlayerCursor>();

		public static readonly Dictionary<ulong, string> KnownPlayerNames = new Dictionary<ulong, string>();

		internal static bool ResolveLogicalSession(
			bool transportConnected, bool retainClientWorldLoad)
			=> transportConnected || retainClientWorldLoad;

		internal static bool ResolveClientRole(bool isHost, bool logicalSession)
			=> logicalSession && !isHost;

		internal static bool TryRetainClientWorldLoad()
		{
			if (IsHost || !InSession)
				return false;
			retainClientWorldLoad = true;
			return true;
		}

		internal static void ReleaseClientWorldLoad()
		{
			retainClientWorldLoad = false;
		}

		public static void Clear()
		{
			using var _ = Profiler.Scope();

			RemoveAllPlayerCursors();
			ConnectedPlayers.Clear();
			KnownPlayerNames.Clear();
			HostUserID = Utils.NilUlong();
			transportConnected = false;
			retainClientWorldLoad = false;
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

		internal static HashSet<ulong> ResolveConnectedRemotePlayerIds(
			IEnumerable<ulong> transportPlayerIds,
			IEnumerable<ulong> sessionPlayerIds,
			ulong localUserId,
			bool transportIsConnected)
		{
			var remotes = new HashSet<ulong>();
			if (!transportIsConnected)
				return remotes;

			foreach (ulong playerId in transportPlayerIds)
				if (playerId.IsValid() && playerId != localUserId)
					remotes.Add(playerId);
			foreach (ulong playerId in sessionPlayerIds)
				if (playerId.IsValid() && playerId != localUserId)
					remotes.Add(playerId);

			return remotes;
		}

		internal static HashSet<ulong> GetConnectedRemotePlayerIds()
			=> ResolveConnectedRemotePlayerIds(
				NetworkConfig.GetConnectedClients(), ConnectedPlayers.Keys, LocalUserID, IsTransportConnected);

		internal static bool IsConnectedRemotePlayer(ulong playerId)
			=> GetConnectedRemotePlayerIds().Contains(playerId);

		// New player cursors are created automatically if one doesn't exist
		public static void CreateNewPlayerCursor(ulong steamID)
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

			PlayerCursors[steamID] = playerCursor;
			DebugConsole.Log($"[MultiplayerSession] Created new cursor for {steamID}");
		}

		public static void CreateConnectedPlayerCursors()
		{
			using var _ = Profiler.Scope();

			var members = GetConnectedRemotePlayerIds();
			foreach (var playerId in members)
			{
				if (!PlayerCursors.ContainsKey(playerId))
				{
					CreateNewPlayerCursor(playerId);
				}
			}
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
