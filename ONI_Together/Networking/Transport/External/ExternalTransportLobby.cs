using System.Collections.Generic;

namespace ONI_Together.Networking.Transport.External
{
    public sealed class ExternalTransportLobbyAdvertisement
    {
        public string Name = string.Empty;
        public string HostName = string.Empty;
        public ExternalTransportLobbyVisibility Visibility = ExternalTransportLobbyVisibility.Public;
        public bool HasPassword;
        public string PasswordHash = string.Empty;
        public int MaxPlayers = 4;
        public Dictionary<string, string> Data = new Dictionary<string, string>();
    }

    public sealed class ExternalTransportLobbyInfo
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
        public ExternalTransportLobbyVisibility Visibility = ExternalTransportLobbyVisibility.Public;
        public Dictionary<string, string> JoinSettings = new Dictionary<string, string>();
    }
}
