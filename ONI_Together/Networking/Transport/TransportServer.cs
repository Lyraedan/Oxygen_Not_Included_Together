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
        /// HOST - true while one or more clients are mid "disconnect-to-load-then-reconnect"
        /// and have therefore dropped off the live roster but are NOT gone (they reconnect
        /// with a fresh transport id). The resume gate and the ready screen must keep
        /// treating them as unready until they return. Transports that instead keep a
        /// Connection==null placeholder in ConnectedPlayers during load (Steamworks) have
        /// nothing extra to report and leave this false.
        /// </summary>
        public virtual bool HasPendingLoadingClients => false;
    }
}
