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

        /// <summary>
        /// Called by the host when a client disconnects to load the save file. The client is
        /// expected to reconnect. Used by transports to suppress leave/join messages.
        /// </summary>
        public virtual void MarkClientLoading(ulong clientId) { }

        /// <summary>
        /// Consumes the "reconnecting from load" mark for a client. Returns true if the mark
        /// existed (i.e. this is an expected reconnect, not a fresh join).
        /// </summary>
        public virtual bool ConsumeReconnectFromLoad(ulong clientId) => false;

        // Bandwidth tracking (bytes/sec, packets/sec)
        public virtual float IncomingBandwidth => 0f;
        public virtual float OutgoingBandwidth => 0f;
        public virtual int IncomingPps => 0;
        public virtual int OutgoingPps => 0;
    }
}
