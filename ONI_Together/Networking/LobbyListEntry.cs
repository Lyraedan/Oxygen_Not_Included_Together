using ONI_Together.Networking.Transport.External;
using Shared.Profiling;
using Steamworks;
using System;
using System.Collections.Generic;

namespace ONI_Together.Networking
{
	/// <summary>
	/// Represents a lobby entry in the lobby browser list.
	/// </summary>
	public class LobbyListEntry
	{
		public ulong LobbyId { get; set; }
		public ulong HostSteamId { get; set; }
		public string LobbyName { get; set; }
		public string HostName { get; set; }
		public int PlayerCount { get; set; }
		public int MaxPlayers { get; set; }
		public int PingMs { get; set; } = -1;
		public bool HasPassword { get; set; }
		public string LobbyCode { get; set; }
		public bool IsFriend { get; set; } = false;

		public bool IsLan { get; set; } = false;
		public string LanAddress { get; set; } = "127.0.0.1";

		public bool IsPrivate { get; set; } = false;

		// External transport data. Steam entries leave TransportId as "steamworks".
		public string TransportId { get; set; } = "steamworks";
		public string ExternalLobbyId { get; set; } = "";
		public ExternalTransportLobbyVisibility Visibility { get; set; } = ExternalTransportLobbyVisibility.Public;
		public Dictionary<string, string> JoinSettings { get; set; } = new Dictionary<string, string>();

		// Game info
		public string ColonyName { get; set; } = "";
		public int Cycle { get; set; } = 0;
		public int DuplicantAlive { get; set; } = 0;
		public int DuplicantTotal { get; set; } = 0;

		/// <summary>
		/// Returns a formatted player count string (e.g., "2/4").
		/// </summary>
		public string PlayerCountDisplay => $"{PlayerCount}/{MaxPlayers}";

		/// <summary>
		/// Returns a formatted ping display (e.g., "45ms" or "---").
		/// </summary>
		public string PingDisplay => PingMs >= 0 ? $"{PingMs}ms" : "---";

		/// <summary>
		/// Returns the cycle display (e.g., "42" or "---").
		/// </summary>
		public string CycleDisplay => Cycle > 0 ? Cycle.ToString() : "---";

		/// <summary>
		/// Returns the duplicant count display (e.g., "7/9" or "7" or "---").
		/// Shows alive/total if deaths occurred, otherwise just alive count.
		/// </summary>
		public string DuplicantDisplay
		{
			get
			{
				if (DuplicantAlive <= 0 && DuplicantTotal <= 0) return "---";
				if (DuplicantAlive < DuplicantTotal && DuplicantTotal > 0)
					return $"{DuplicantAlive}/{DuplicantTotal}";
				if (DuplicantAlive > 0)
					return DuplicantAlive.ToString();
				if (DuplicantTotal > 0)
					return DuplicantTotal.ToString();
				return "---";
			}
		}

		/// <summary>
		/// Returns the colony name or fallback.
		/// </summary>
		public string ColonyDisplay => !string.IsNullOrEmpty(ColonyName) && ColonyName != "---" ? ColonyName : "Unknown";

		/// <summary>
		/// Returns the host name with friend indicator if applicable.
		/// </summary>
		public string HostDisplayWithBadge => IsFriend ? $"★ {HostName}" : HostName;

		public bool LobbyFull => MaxPlayers <= PlayerCount;


		/// <summary>
		/// override the equality comparer to make use of hashset/dict. "Contains" function
		/// </summary>
		/// <param name="other"></param>
		/// <returns></returns>
		/// ------------------
		public bool Equals(LobbyListEntry other)
		{
			using var _ = Profiler.Scope();

			if (other == null)
				return false;

			if (!string.Equals(TransportId, other.TransportId, StringComparison.Ordinal))
				return false;

			if (!string.IsNullOrEmpty(ExternalLobbyId) || !string.IsNullOrEmpty(other.ExternalLobbyId))
				return string.Equals(ExternalLobbyId, other.ExternalLobbyId, StringComparison.Ordinal);

			return other.HostSteamId == HostSteamId && other.LobbyId == LobbyId;
		}
		public override bool Equals(object obj) => obj is LobbyListEntry other && Equals(other);

		public static bool operator ==(LobbyListEntry a, LobbyListEntry b)
		{
			if (ReferenceEquals(a, b))
				return true;
			if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
				return false;
			return a.Equals(b);
		}
		public static bool operator !=(LobbyListEntry a, LobbyListEntry b) => !(a == b);
		public override int GetHashCode()
		{
			using var _ = Profiler.Scope();

			return (TransportId?.GetHashCode() ?? 0)
				^ (ExternalLobbyId?.GetHashCode() ?? 0)
				^ LobbyId.GetHashCode()
				^ HostSteamId.GetHashCode();
		}
	}
}
