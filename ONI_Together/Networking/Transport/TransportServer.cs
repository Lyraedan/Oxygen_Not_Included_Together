using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ONI_Together.Networking.Transport
{
    public abstract class TransportServer
    {
        public System.Action OnError;

        public abstract void Prepare();

        public abstract void Start();

        public abstract void Stop();

        public abstract void CloseConnections();

        public abstract void Update();

        public abstract void OnMessageRecieved();

        public abstract void KickClient(ulong clientId);

        // Bandwidth tracking (bytes/sec, packets/sec)
        public virtual float IncomingBandwidth => 0f;
        public virtual float OutgoingBandwidth => 0f;
        public virtual int IncomingPps => 0;
        public virtual int OutgoingPps => 0;

        // Tracks clients that intentionally disconnected to load a received save file
        // (needed for transports like LiteNetLib / Riptide that remove the player on disconnect).
        public virtual void MarkClientLoading(ulong clientId, string playerName = null) { }
        public virtual bool ConsumeReconnectFromLoad(ulong clientId, string playerName = null) => false;
        public virtual bool HasPendingLoadingClients() => false;
        public virtual List<string> GetPendingLoadingClientNames() => new List<string>();
    }
}
