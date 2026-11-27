using System.Collections.Generic;

namespace ONI_Together_DedicatedServer.Transports
{
    public abstract class DedicatedTransportServer
    {
        public abstract bool Start();
        public abstract void Update();
        public abstract void Stop();
        public abstract bool IsRunning();
        public abstract Dictionary<ulong, ONI.Player> GetPlayers();
        public virtual void SetWorldInfo(string worldName, int cycle = 1) { }
        /// <summary>Re-read live configuration without restarting the socket or dropping players.</summary>
        public virtual void ReloadConfiguration() { }

        /// <summary>Pause packet processing while empty. Connections are still accepted so a join can resume.</summary>
        public virtual void SetPaused(bool paused) { }
        public virtual bool IsPaused => false;
    }
}
