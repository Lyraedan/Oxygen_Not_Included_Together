namespace ONI_Together_DedicatedServer.Transports
{
    public abstract class DedicatedTransportServer
    {
        public abstract void Start();

        public abstract void Update();
        public abstract void Stop();

        public abstract bool IsRunning();

        public abstract Dictionary<ulong, ONI.Player> GetPlayers();
    }
}
