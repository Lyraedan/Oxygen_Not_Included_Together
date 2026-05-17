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
    }
}
