using KSerialization;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using Shared.Profiling;
using TMPro;
using UnityEngine;

namespace ONI_Together.Misc
{
	public static class Utils
	{
		/// <summary>
		/// Max size of a single message that we can SEND.
		/// <para/>
		/// Note: We might be wiling to receive larger messages, and our peer might, too.
		/// </summary>
		public static int MaxSteamNetworkingSocketsMessageSizeSend = 512 * 1024;

		/// <summary>
		/// Force quites the game without the Klei metrics that can cause crashes
		/// </summary>
		public static void ForceQuitGame()
		{
			using var _ = Profiler.Scope();

            Game.Instance.SetIsLoading();
            Grid.CellCount = 0;
            Sim.Shutdown();
        }
		public static void LogHierarchy(Transform root, string prefix = "")
		{
			using var _ = Profiler.Scope();

			if (root == null)
			{
				DebugConsole.LogWarning("LogHierarchy called with null root.");
				return;
			}

			DebugConsole.Log($"{prefix}{root.name}");

			foreach (Transform child in root)
			{
				LogHierarchy(child, prefix + "  ");
			}
		}

        public static GameObject FindChild(this GameObject root, string path)
        {
	        using var _ = Profiler.Scope();

            var t = root.transform.Find(path);
            return t != null ? t.gameObject : null;
        }

        public static string NetworkStateToString(NetworkIndicatorsScreen.NetworkState state)
        {
	        using var _ = Profiler.Scope();

            switch (state)
            {
                case NetworkIndicatorsScreen.NetworkState.GOOD:
                    return "Fine";
                case NetworkIndicatorsScreen.NetworkState.DEGRADED:
                    return "Degraded";
                case NetworkIndicatorsScreen.NetworkState.BAD:
                    return "Poor";
                default:
                    return "Unknown";
            }
        }

        public static void Inject<T>(GameObject prefab) where T : KMonoBehaviour
		{
			using var _ = Profiler.Scope();

			if (prefab.GetComponent<T>() == null)
			{
				DebugConsole.Log($"Added {typeof(T).Name} to {prefab.name}");
				prefab.AddOrGet<T>();
			}
		}

		public static void InjectAll(GameObject prefab, params Type[] types)
		{
			using var _ = Profiler.Scope();

			foreach (var type in types)
			{
				if (!typeof(KMonoBehaviour).IsAssignableFrom(type)) continue;
				if (prefab.GetComponent(type) != null) continue;

				DebugConsole.Log($"Added {type.Name} to {prefab.name}");
				prefab.AddComponent(type);
			}
		}


		public static void ListAllTMPFonts()
		{
			using var _ = Profiler.Scope();

			var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
			DebugConsole.Log($"Found {fonts.Length} TMP_FontAsset(s):");

			foreach (var font in fonts)
			{
				DebugConsole.Log($" - {font.name} (path: {font.name}, instance ID: {font.GetInstanceID()})");
			}

			if (fonts.Length == 0)
			{
				DebugConsole.Log("No TMP_FontAsset found in memory.");
			}
		}

		public static TMP_FontAsset GetDefaultTMPFont()
		{
			using var _ = Profiler.Scope();

			return Localization.FontAsset;
		}

		public static string ColorText(string text, Color color)
		{
			using var _ = Profiler.Scope();

			return ColorText(text, Util.ToHexString(color));
		}
		public static string ColorText(string text, string hex)
		{
			using var _ = Profiler.Scope();

			hex = hex.Replace("#", string.Empty);
			return "<color=#" + hex + ">" + text + "</color>";
		}
		public static List<ChunkData> CollectChunks(int startX, int startY, int chunkSize, int numChunksX, int numChunksY)
		{
			using var _ = Profiler.Scope();

			var chunks = new List<ChunkData>();
			for (int cx = 0; cx < numChunksX; cx++)
				for (int cy = 0; cy < numChunksY; cy++)
				{
					int x0 = startX + cx * chunkSize;
					int y0 = startY + cy * chunkSize;
					chunks.Add(CreateChunk(x0, y0, chunkSize, chunkSize));
				}
			return chunks;
		}
		/// <summary>
		/// Checks if the mono behavior sits on a duplicant in a host game
		/// </summary>
		/// <param name="behavior"></param>
		/// <returns></returns>
		public static bool IsHostMinion(MonoBehaviour behavior)
		{
			using var _ = Profiler.Scope();

			if (!IsHostEntity(behavior))
				return false;
			if(!behavior.TryGetComponent<KPrefabID>(out var kprefab) ||  !kprefab.HasTag(GameTags.BaseMinion))
				return false;
			return true;
		}
		/// <summary>
		/// Gate for host-originated broadcasts that key off NetId on the wire.
		/// True only if: in session, is host, behavior alive, attached GameObject has a
		/// NetworkIdentity with NetId != 0. Rejects ghost/preview/particle GameObjects
		/// that use shared Klei components (e.g. SymbolOverrideController) but are not
		/// registered network entities — sending for them would be wasted bandwidth
		/// (receiver lookup would fail anyway).
		/// </summary>
		public static bool IsHostEntityWithNetId(MonoBehaviour behavior, out int netId)
		{
			using var _ = Profiler.Scope();
			netId = 0;
			if (!IsHostEntity(behavior))
				return false;
			netId = behavior.GetNetId();
			return netId != 0;
		}
		public static bool IsHostEntity(MonoBehaviour behavior)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
				return false;
			if (behavior.IsNullOrDestroyed() || behavior.gameObject.IsNullOrDestroyed())
				return false;
			return true;
		}
		public static void RefreshIfSelected(MonoBehaviour behavior)
		{
			using var _ = Profiler.Scope();

			if (behavior.IsNullOrDestroyed() || !behavior.TryGetComponent<KSelectable>(out var selectable))
				return;

			if(SelectTool.Instance?.selected == selectable)
			{
				SelectTool.Instance.Select(null);
				SelectTool.Instance.Select(selectable);
			}
		}

		private static ChunkData CreateChunk(int x0, int y0, int width, int height)
		{
			using var _ = Profiler.Scope();

			var chunk = new ChunkData
			{
				TileX = x0,
				TileY = y0,
				Width = width,
				Height = height,
				Tiles = new ushort[width * height],
				Temperatures = new float[width * height],
				Masses = new float[width * height],
				DiseaseIdx = new byte[width * height],
				DiseaseCount = new int[width * height],
			};

			for (int i = 0; i < width; i++)
				for (int j = 0; j < height; j++)
				{
					int x = x0 + i, y = y0 + j;
					int idx = i + j * width;
					int cell = Grid.XYToCell(x, y);

					if (!Grid.IsValidCell(cell)) continue;

					chunk.Tiles[idx] = Grid.ElementIdx[cell];
					chunk.Temperatures[idx] = Grid.Temperature[cell];
					chunk.Masses[idx] = Grid.Mass[cell];
					chunk.DiseaseIdx[idx] = Grid.DiseaseIdx[cell];
					chunk.DiseaseCount[idx] = Grid.DiseaseCount[cell];
				}

			return chunk;
		}

		[Obsolete("Use new FormatBytes instead!")]
		public static string FormatBytesOld(long bytes)
		{
			using var _ = Profiler.Scope();

			if (bytes < 1024) return $"{bytes} B";
			if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
			return $"{bytes / 1024f / 1024f:F2} MB";
		}

		public static string FormatBytes(long bytes)
		{
			using var _ = Profiler.Scope();

			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			double len = bytes;
			int order = 0;
			while (len >= 1024 && order < sizes.Length - 1)
			{
				order++;
				len /= 1024;
			}
			return $"{len:0.##} {sizes[order]}";
		}

		/// <summary>
		/// Formats a timespan nicely:
		/// 45s, 3m 12s, 1h 15m 20s
		/// </summary>
		public static string FormatTime(double seconds)
		{
			using var _ = Profiler.Scope();

			var ts = TimeSpan.FromSeconds(seconds);

			string result = "";

			if (ts.Hours > 0)
				result += $"{ts.Hours}h ";

			if (ts.Minutes > 0 || ts.Hours > 0)
				result += $"{ts.Minutes}m ";

			result += $"{ts.Seconds}s";

			return result.Trim();
		}

		public static bool IsInMenu()
		{
			using var _ = Profiler.Scope();

			return App.GetCurrentSceneName() == "frontend";
		}

		public static bool IsInGame()
		{
			using var _ = Profiler.Scope();

			return App.GetCurrentSceneName() == "backend";
		}

		public static GameObject FindNearbyWorkable(Vector3 position, float radius, Predicate<GameObject> predicate)
		{
			using var _ = Profiler.Scope();

			foreach (Workable workable in UnityEngine.Object.FindObjectsByType<Workable>(FindObjectsSortMode.None))
			{
				if (workable == null) continue;

				var go = workable.gameObject;
				float dist = Vector3.Distance(go.transform.position, position);

				if (dist <= radius && predicate(go))
					return go;
			}

			return null;
		}

		public static GameObject FindClosestGameObjectWithTag(Vector3 position, Tag tag, float radius)
		{
			using var _ = Profiler.Scope();

			GameObject closest = null;
			float closestDistSq = radius * radius;

			foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
			{
				if (!go.HasTag(tag))
					continue;

				float distSq = (go.transform.position - position).sqrMagnitude;
				if (distSq < closestDistSq)
				{
					closest = go;
					closestDistSq = distSq;
				}
			}

			return closest;
		}

		public static GameObject FindEntityInRadius(Vector3 origin, float radius, Predicate<GameObject> predicate)
		{
			using var _ = Profiler.Scope();

			foreach (var go in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
			{
				if (go == null) continue;

				float dist = (go.transform.position - origin).sqrMagnitude;
				if (dist <= radius * radius && predicate(go))
					return go;
			}

			return null;
		}

        public static string TrucateName(string name, int len = 24)
        {
	        using var _ = Profiler.Scope();

            if (name.Length > len)
            {
                return name.Substring(0, len) + "...";
            }
            else
            {
                return name;
            }
        }

        public static string GetLocalPlayerName()
        {
	        using var _ = Profiler.Scope();

            if (SteamManager.Initialized)
            {
                return Steamworks.SteamFriends.GetPersonaName();
            }
            return $"Player {NetworkConfig.GetLocalID()}";
        }

        public static ulong NilUlong()
        {
	        using var _ = Profiler.Scope();

            return 0uL;
        }

        public static LanSettings DecodeHashedAddress(string encoded)
        {
	        using var _ = Profiler.Scope();

            byte[] bytes = Convert.FromBase64String(encoded);
            string decoded = Encoding.UTF8.GetString(bytes);

            string[] parts = decoded.Split(':');
            if (parts.Length != 2)
                throw new FormatException("Invalid LAN address encoding");

            return new LanSettings
            {
                Ip = parts[0],
                Port = int.Parse(parts[1])
            };
        }

        /// <summary>
        /// Get a deterministic client id based off a remotes IP and PORT
        /// </summary>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        public static ulong GetClientId(IPEndPoint endpoint)
        {
	        using var _ = Profiler.Scope();

            unchecked
            {
				//https://gist.github.com/RevenantX/9817785b5a0741f124bc2a12ede85f9d
				ulong hash = 14695981039346656037UL; // FNV-1a 64-bit offset
                const ulong prime = 1099511628211UL;

                // IP bytes
                byte[] ipBytes = endpoint.Address.GetAddressBytes();
                for (int i = 0; i < ipBytes.Length; i++)
                {
                    hash ^= ipBytes[i];
                    hash *= prime;
                }

                // Port
                hash ^= (ulong)endpoint.Port;
                hash *= prime;

                return hash;
            }
        }

        public static void PauseSimOnPlayerLeft()
        {
	        // Host check first: this also runs on clients (e.g. RiptideClient peer-left), and
	        // must bail before reading the host-only PauseSimOnPlayerDisconnect config.
	        if (!MultiplayerSession.IsHost) return;
	        if (!Configuration.Instance.PauseSimOnPlayerDisconnect) return;

	        PauseSimIfRunning();
        }

        /// <summary>
        /// HOST ONLY - Pause the sim when a client joins / starts the ready sync so the
        /// ready screen reflects a frozen world for everyone. Unlike PauseSimOnPlayerLeft
        /// this is intentionally unconditional (not gated by PauseSimOnPlayerDisconnect):
        /// the ready screen must freeze the world for correctness regardless of host prefs.
        ///
        /// Note: this only broadcasts a pause when the host is currently *running* (see the
        /// !IsPaused guard in PauseSimIfRunning). If the host is already paused, no pause
        /// packet is sent — the "freeze everyone" guarantee therefore assumes clients already
        /// mirror the host's paused state (true in normal flow, since client speed tracks the
        /// host via SpeedChangePacket); it does not retroactively correct a pre-existing
        /// client/host speed desync.
        /// </summary>
        public static void PauseSimForReadyScreen()
        {
	        PauseSimIfRunning();
        }

        /// <summary>
        /// HOST ONLY - Pause the sim if it is currently running. Pausing via TogglePause
        /// reuses SpeedControlPatch.TogglePause_Postfix, which broadcasts the pause to all
        /// peers. The !IsPaused guard keeps repeated calls (e.g. reconnect-after-load) a
        /// no-op, and pausing is never blocked by the resume gate.
        /// </summary>
        private static void PauseSimIfRunning()
        {
	        if (!MultiplayerSession.IsHost) return;
	        if (SpeedControlScreen.Instance == null) return;

	        if (!SpeedControlScreen.Instance.IsPaused)
				SpeedControlScreen.Instance.TogglePause(false);
        }

        #region SaveLoadRoot Extensions
        private static readonly FieldInfo optionalComponentListField =
				typeof(SaveLoadRoot).GetField("m_optionalComponentTypeNames", BindingFlags.NonPublic | BindingFlags.Instance);

		public static void TryDeclareOptionalComponent<T>(this SaveLoadRoot root) where T : KMonoBehaviour
		{
			using var _ = Profiler.Scope();

			if (optionalComponentListField?.GetValue(root) is List<string> list)
			{
				string typeName = typeof(T).ToString();

				if (!list.Contains(typeName))
				{
					root.DeclareOptionalComponent<T>();
				}
			}
			else
			{
				DebugConsole.LogWarning("Could not access m_optionalComponentTypeNames via reflection.");
			}
		}
		#endregion

		#region KBatchedAnimEventToggler Extensions
		public static void Trigger(this KBatchedAnimEventToggler toggler, int eventHash, bool enable)
		{
			using var _ = Profiler.Scope();

			if (enable)
				toggler.SendMessage("Enable", null, SendMessageOptions.DontRequireReceiver);
			else
				toggler.SendMessage("Disable", null, SendMessageOptions.DontRequireReceiver);
		}
		#endregion

		#region Grid Extensions
		public static bool IsWalkableCell(int cell)
		{
			using var _ = Profiler.Scope();

			return Grid.IsValidCell(cell)
					&& !Grid.Solid[cell]
					&& !Grid.DupeImpassable[cell]
					&& Grid.DupePassable[cell];
		}
		#endregion

		#region Schedule Extensions
		public static int GetScheduleIndex(this Schedule schedule)
		{
			using var _ = Profiler.Scope();

            var schedules = ScheduleManager.Instance.schedules;
            if (schedules == null) return -1;

            int scheduleIndex = schedules.IndexOf(schedule);
			return scheduleIndex;
        }
        #endregion

        #region BinaryReader / BinaryWriter Extensions
        public static void Write(this BinaryWriter writer, Color c)
        {
	        using var _ = Profiler.Scope();

            writer.WriteSingleFast(c.r);
            writer.WriteSingleFast(c.g);
            writer.WriteSingleFast(c.b);
			writer.WriteSingleFast(c.a);
        }

        public static void Write(this BinaryWriter writer, ColorRGB c)
        {
	        using var _ = Profiler.Scope();

            writer.WriteSingleFast(c.R);
            writer.WriteSingleFast(c.G);
            writer.WriteSingleFast(c.B);
        }

        public static Color ReadColor(this BinaryReader reader)
        {
	        using var _ = Profiler.Scope();

            Color result = default(Color);
            result.r = reader.ReadSingle();
            result.g = reader.ReadSingle();
            result.b = reader.ReadSingle();
			result.a = reader.ReadSingle();
            return result;
        }

        public static ColorRGB ReadColorRGB(this BinaryReader reader)
        {
	        using var _ = Profiler.Scope();

            ColorRGB result = default(ColorRGB);
            result.R = reader.ReadByte();
            result.G = reader.ReadByte();
            result.B = reader.ReadByte();
            return result;
        }
        #endregion
    }
}
