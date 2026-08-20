using System;
using LiteNetLib;
using Shared.Profiling;

namespace ONI_Together_DedicatedServer.ONI
{
    public class Player
    {
        public NetPeer Connection { get; private set; }
        public bool IsMaster { get; private set; }
        public ulong ClientID { get; set; }

        public Player(NetPeer conn, bool isMaster, ulong clientId)
        {
            using var _ = Profiler.Scope();

            Connection = conn;
            ClientID = clientId;
            UpdateMasterState(isMaster);
        }

        public void UpdateMasterState(bool state)
        {
            using var _ = Profiler.Scope();

            IsMaster = state;
        }
    }
}
