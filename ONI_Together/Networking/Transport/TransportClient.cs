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

        public abstract void Prepare();

        public abstract void ConnectToHost(string ip, int port);

        public abstract void Disconnect();

        public abstract bool TryReconnectToSession();

        public abstract void Update();

        public abstract void OnMessageRecieved();

        // Network health functions

        public abstract int GetPing();

        public abstract NetworkState GetJitterState();

        public abstract NetworkState GetLatencyState();

        public abstract NetworkState GetPacketlossState();

        public abstract NetworkState GetServerPerformanceState();
    }
}
