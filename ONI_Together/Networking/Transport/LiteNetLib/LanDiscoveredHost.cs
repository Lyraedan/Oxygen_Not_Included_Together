using System.Net;

namespace ONI_Together.Networking.Transport.Lan
{
    public struct LanDiscoveredHost
    {
        public IPEndPoint EndPoint;
        public string HostName;
        public string WorldName;
        public int Cycle;
        public int PlayerCount;
        public int MaxPlayers;
        public int Port;
        public float LastSeenTime;

        public string DisplayName => $"{HostName}'s Colony ({WorldName} - Cycle {Cycle}) [{PlayerCount}/{MaxPlayers}]";
        public string AddressString => $"{EndPoint.Address}:{Port}";
    }
}
