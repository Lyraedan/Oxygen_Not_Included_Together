using System;
using LiteNetLib;

namespace ONI_Together_DedicatedServer.ONI
{
    public class Player
    {
        public NetPeer Connection { get; private set; }
        public bool IsMaster { get; private set; }
        public ulong ClientID { get; private set; }
        public DateTime ConnectedAtUtc { get; }

        /// <summary>
        /// Round-trip time in milliseconds (LiteNetLib exposes one-way Ping).
        /// </summary>
        public int PingMs => Connection != null ? Connection.Ping * 2 : -1;

        public Player(NetPeer conn, bool isMaster, ulong clientId)
        {
            Connection = conn;
            ClientID = clientId;
            ConnectedAtUtc = DateTime.UtcNow;
            UpdateMasterState(isMaster);
        }

        public void UpdateMasterState(bool state)
        {
            IsMaster = state;
        }
    }
}
