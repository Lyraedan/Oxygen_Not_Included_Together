using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Riptide;
using Shared.Profiling;

namespace ONI_Together_DedicatedServer.ONI
{
    public class Player
    {
        public Connection Connection { get; private set; }
        public bool IsMaster { get; private set; }

        public ulong ClientID => Connection?.Id ?? 0uL;

        public Player(Connection conn, bool IsMaster)
        {
            using var _ = Profiler.Scope();

            Connection = conn;
            UpdateMasterState(IsMaster);
        }

        public void UpdateMasterState(bool state)
        {
            using var _ = Profiler.Scope();

            IsMaster = state;
        }
    }
}
