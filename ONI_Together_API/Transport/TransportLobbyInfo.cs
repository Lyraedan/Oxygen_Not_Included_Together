using JetBrains.Annotations;
using System.Collections.Generic;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Optional lobby metadata supplied by the host before the transport starts.
    /// The main mod uses this for the shared visibility controls and server browser.
    /// </summary>
    [PublicAPI]
    public sealed class TransportLobbyAdvertisement
    {
        public string Name = string.Empty;
        public string HostName = string.Empty;
        public TransportLobbyVisibility Visibility = TransportLobbyVisibility.Public;
        public bool HasPassword;
        public string PasswordHash = string.Empty;
        public int MaxPlayers = 4;
        public Dictionary<string, string> Data = new Dictionary<string, string>();
    }

    /// <summary>
    /// One lobby returned by an external transport's browser.
    /// </summary>
    [PublicAPI]
    public sealed class TransportLobbyInfo
    {
        public string TransportId = string.Empty;
        public string LobbyId = string.Empty;
        public string Name = string.Empty;
        public string HostName = string.Empty;
        public string ColonyName = string.Empty;
        public int PlayerCount;
        public int MaxPlayers;
        public int PingMs = -1;
        public int Cycle;
        public int DuplicantAlive;
        public int DuplicantTotal;
        public bool HasPassword;
        public TransportLobbyVisibility Visibility = TransportLobbyVisibility.Public;
        public Dictionary<string, string> JoinSettings = new Dictionary<string, string>();
    }
}
