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

        /// <summary>The host's own player id, when the transport can provide one.</summary>
        public virtual ulong LocalPlayerId => 0UL;

        /// <summary>All connected player ids, including the host when applicable.</summary>
        public virtual IReadOnlyList<ulong> GetConnectedPlayerIds() => System.Array.Empty<ulong>();

        public virtual void MarkClientLoading(ulong clientId) { }

        public virtual bool ConsumeReconnectFromLoad(ulong clientId) => false;

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
    }
}
