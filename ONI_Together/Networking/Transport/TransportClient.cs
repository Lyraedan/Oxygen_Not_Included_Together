using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ONI_Together.Menus.NetworkIndicatorsScreen;

namespace ONI_Together.Networking.Transport
{
    public abstract class TransportClient
    {
        /// <summary>
        /// When the client is connected to the server
        /// </summary>
        public System.Action OnClientConnected;
        /// <summary>
        /// When the client is disconnected from the server
        /// </summary>
        public System.Action OnClientDisconnected;

        /// <summary>
        /// Continue the connection flow
        /// </summary>
        public System.Action OnContinueConnectionFlow;

        /// <summary>
        /// Request the game state or return to the menu
        /// </summary>
        public System.Action OnRequestStateOrReturn;
        /// <summary>
        /// Request the client to return to the menu
        /// </summary>
        public System.Action<string, string> OnReturnToMenu;

        /// <summary>The local player's transport id, when the transport can provide one.</summary>
        public virtual ulong LocalPlayerId => 0UL;

        /// <summary>The host's transport id, when known.</summary>
        public virtual ulong HostPlayerId => 0UL;

        public virtual bool IsConnected => false;

        public virtual bool IsLoadingReconnect { get; set; }

        public abstract void Prepare();

        public abstract void ConnectToHost(string ip, int port);

        public abstract void Disconnect();

        public abstract void ReconnectToSession();

        public abstract void Update();

        public abstract void OnMessageRecieved();

        // Network health functions

        public abstract int GetPing();

        public abstract NetworkState GetJitterState();

        public abstract NetworkState GetLatencyState();

        public abstract NetworkState GetPacketlossState();

        public abstract NetworkState GetServerPerformanceState();

        // Bandwidth tracking (bytes/sec, packets/sec)
        public virtual float IncomingBandwidth => 0f;
        public virtual float OutgoingBandwidth => 0f;
        public virtual int IncomingPps => 0;
        public virtual int OutgoingPps => 0;
    }
}
