using ONI_Together.DebugTools;
using ONI_Together.Misc;
using Shared.Profiling;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ONI_Together.Networking.Transport.Steamworks
{
	public static class SteamRichPresence
	{
		// Steam limits rich presence values to 255 bytes (UTF-8).
		private const int MaxStatusBytes = 255;
		private const int MaxAsteroidChars = 32;

		private static readonly Dictionary<string, string> LastValues = new Dictionary<string, string>();
		private static string _asteroidName = "";
		private static bool _cleared = true;

		/// <summary>
		/// Cached asteroid name, supplied by <see cref="SteamPresenceUpdater"/>.
		/// </summary>
		public static void SetAsteroid(string name)
		{
			_asteroidName = name ?? "";
		}

		/// <summary>
		/// Writes the current game state to Steam. <paramref name="force"/> writes every key
		/// even when the cached value did not change.
		/// </summary>
		public static void Refresh(bool force = false)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (!SteamManager.Initialized)
					return;

				if (!Configuration.Instance.UseSteamRichPresence)
				{
					Clear();
					return;
				}

				PresenceInfo info = PresenceInfo.Capture(_asteroidName);
				string status = BuildStatus(info);

				bool inLobby = info.InSession && SteamLobby.InLobby;
				string connect = "";
				string group = "";
				string groupSize = "";

				if (inLobby)
				{
					// Standard Steam join string: "+connect_lobby <steamID>".
					connect = "+connect_lobby " + SteamLobby.CurrentLobby.m_SteamID.ToString();
					group = SteamLobby.CurrentLobby.m_SteamID.ToString();
					groupSize = SteamMatchmaking.GetNumLobbyMembers(SteamLobby.CurrentLobby).ToString();
				}

				Write("status", status, force);
				Write("connect", connect, force);
				Write("steam_player_group", group, force);
				Write("steam_player_group_size", groupSize, force);
				_cleared = false;
			}
			catch (Exception ex)
			{
				DebugConsole.LogWarning($"[SteamRichPresence] Refresh failed: {ex.Message}");
			}
		}

		public static void Clear()
		{
			using var _ = Profiler.Scope();

			LastValues.Clear();

			if (!SteamManager.Initialized || _cleared)
				return;

			try
			{
				SteamFriends.ClearRichPresence();
				_cleared = true;
				DebugConsole.Log("[SteamRichPresence] Cleared.");
			}
			catch (Exception ex)
			{
				DebugConsole.LogWarning($"[SteamRichPresence] Failed to clear: {ex.Message}");
			}
		}

		private static string BuildStatus(in PresenceInfo info)
		{
			string line = info.Details ?? "";

			if (!string.IsNullOrEmpty(info.State))
				line = line.Length > 0 ? line + " · " + info.State : info.State;

			if (info.InGame && !string.IsNullOrEmpty(info.Asteroid))
			{
				string asteroid = info.Asteroid.Length > MaxAsteroidChars
					? info.Asteroid.Substring(0, MaxAsteroidChars - 3) + "..."
					: info.Asteroid;

				string candidate = line + " · " + asteroid;
				if (Encoding.UTF8.GetByteCount(candidate) <= MaxStatusBytes)
					line = candidate;
			}

			return CapBytes(line, MaxStatusBytes);
		}

		private static string CapBytes(string value, int maxBytes)
		{
			if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) <= maxBytes)
				return value;

			var builder = new StringBuilder(value.Length);
			int bytes = 0;

			TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(value);
			while (enumerator.MoveNext())
			{
				string element = enumerator.GetTextElement();
				int size = Encoding.UTF8.GetByteCount(element);
				if (bytes + size > maxBytes)
					break;

				builder.Append(element);
				bytes += size;
			}

			return builder.ToString();
		}

		private static void Write(string key, string value, bool force)
		{
			value ??= "";

			if (!force && LastValues.TryGetValue(key, out string previous) && previous == value)
				return;

			try
			{
				if (!SteamFriends.SetRichPresence(key, value))
				{
					DebugConsole.LogWarning($"[SteamRichPresence] Steam refused key '{key}'");
					return;
				}

				LastValues[key] = value;

				if (key == "status")
					DebugConsole.Log($"[SteamRichPresence] Status: {value}");
			}
			catch (Exception ex)
			{
				DebugConsole.LogWarning($"[SteamRichPresence] Failed to set '{key}': {ex.Message}");
			}
		}
	}
}
