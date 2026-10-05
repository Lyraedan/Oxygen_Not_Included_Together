using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using ONI_Together.Networking.States;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.UI;
using Steamworks;

namespace ONI_Together.Networking.Transport.Steam
{
    public class SteamworksServer : TransportServer
    {
        public static HSteamListenSocket ListenSocket { get; private set; }
        public static HSteamNetPollGroup PollGroup { get; private set; }
        private static Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChangedCallback;

        // Bandwidth tracking via per-connection GetConnectionRealTimeStatus
        private float _srvInBw, _srvOutBw;
        private int _srvInPps, _srvOutPps;

        public override void Prepare()
        {
            using var _ = Profiler.Scope();

            if (!SteamManager.Initialized)
            {
                OnError.Invoke();
                DebugConsole.LogError("[GameServer] SteamManager not initialized! Cannot start listen server.");
                return;
            }
        }

        public override void Start()
        {
            using var _ = Profiler.Scope();

            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STARTED, "Steam"));

            // Create listen socket for P2P
            var options = new SteamNetworkingConfigValue_t[2];
            options[0].m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutInitial;
            options[0].m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32;
            options[0].m_val.m_int32 = Configuration.Instance.Host.TimeoutSeconds * 1000;

            options[1].m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutConnected;
            options[1].m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32;
            options[1].m_val.m_int32 = Configuration.Instance.Host.TimeoutSeconds * 1000;

            ListenSocket = SteamNetworkingSockets.CreateListenSocketP2P(
                    0, // Virtual port
                    options.Length,
                    options
            );

            if (ListenSocket.m_HSteamListenSocket == 0)
            {
                OnError.Invoke();
                DebugConsole.LogError("[GameServer] Failed to create ListenSocket!");
                return;
            }

            PollGroup = SteamNetworkingSockets.CreatePollGroup();

            if (PollGroup.m_HSteamNetPollGroup == 0)
            {
                OnError.Invoke();
                DebugConsole.LogError("[GameServer] Failed to create PollGroup!");
                SteamNetworkingSockets.CloseListenSocket(ListenSocket);
                return;
            }

            _connectionStatusChangedCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            MultiplayerSession.RegisterAsHost();
        }

        public override void Stop()
        {
            using var _ = Profiler.Scope();

            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STOPPED, "Steam"));

            if (PollGroup.m_HSteamNetPollGroup != 0)
                SteamNetworkingSockets.DestroyPollGroup(PollGroup);

            if (ListenSocket.m_HSteamListenSocket != 0)
                SteamNetworkingSockets.CloseListenSocket(ListenSocket);
        }

        public override void CloseConnections()
        {
            MultiplayerSession.UnRegisterAsHost(CreateCloseFunc("Shutdown"));
        }

        public override void Update()
        {
            using var _ = Profiler.Scope();

            SteamAPI.RunCallbacks();
            SteamNetworkingSockets.RunCallbacks();
            UpdateServerBandwidth();
        }

        private void UpdateServerBandwidth()
        {
            float totalIn = 0f, totalOut = 0f;
            float totalPpsIn = 0f, totalPpsOut = 0f;

            foreach (var kvp in MultiplayerSession.ConnectedPlayers)
            {
                if (kvp.Value.Connection is HSteamNetConnection conn)
                {
                    SteamNetConnectionRealTimeStatus_t status = default;
                    SteamNetConnectionRealTimeLaneStatus_t laneStatus = default;
                    var res = SteamNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref laneStatus);
                    if (res == EResult.k_EResultOK)
                    {
                        totalIn += status.m_flInBytesPerSec;
                        totalOut += status.m_flOutBytesPerSec;
                        totalPpsIn += status.m_flInPacketsPerSec;
                        totalPpsOut += status.m_flOutPacketsPerSec;
                    }
                }
            }

            _srvInBw = totalIn;
            _srvOutBw = totalOut;
            _srvInPps = (int)totalPpsIn;
            _srvOutPps = (int)totalPpsOut;
        }

        public override float IncomingBandwidth => _srvInBw;
        public override float OutgoingBandwidth => _srvOutBw;
        public override int IncomingPps => _srvInPps;
        public override int OutgoingPps => _srvOutPps;

        public override void OnMessageRecieved()
        {
            using var _ = Profiler.Scope();

            var scope = Profiler.Scope();
            int totalBytes = 0;

            int maxMessagesPerPoll = Configuration.GetHostProperty<int>("MaxMessagesPerPoll");
            var messages = new IntPtr[maxMessagesPerPoll];
            int msgCount = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(PollGroup, messages, maxMessagesPerPoll);

            for (int i = 0; i < msgCount; i++)
            {
                var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(messages[i]);
                totalBytes += msg.m_cbSize;
                byte[] bytes = new byte[msg.m_cbSize];
                Marshal.Copy(msg.m_pData, bytes, 0, msg.m_cbSize);

                try
                {
                    PacketHandler.HandleIncoming(bytes);
                }
                catch (Exception ex)
                {
                    DebugConsole.LogWarning($"[GameServer] Rejected invalid incoming packet: {ex}");
                }

                SteamNetworkingMessage_t.Release(messages[i]);
            }
            scope.End(msgCount, totalBytes);
        }

        private static void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
        {
            using var _ = Profiler.Scope();

            var conn = data.m_hConn;
            var clientId = data.m_info.m_identityRemote.GetSteamID();
            var state = data.m_info.m_eState;

            DebugConsole.Log($"[GameServer] OnConnectionStatusChanged: state={state} from {clientId}");

            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                    TryAcceptConnection(conn, clientId);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    OnClientConnected(conn, clientId);
                    break;

                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    OnClientClosed(conn, clientId);
                    break;
            }
        }

        private static void TryAcceptConnection(HSteamNetConnection conn, CSteamID clientId)
        {
            using var _ = Profiler.Scope();

            // Get connection info to check actual state
            SteamNetConnectionInfo_t info = default;
            if (!SteamNetworkingSockets.GetConnectionInfo(conn, out info))
            {
                DebugConsole.LogWarning($"[GameServer] TryAcceptConnection: Could not get connection info for {clientId}");
            }
            else
            {
                DebugConsole.Log($"[GameServer] TryAcceptConnection: Connection state for {clientId} is {info.m_eState}");
            }

            // Only accept if in Connecting state
            if (info.m_eState != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                DebugConsole.LogWarning($"[GameServer] TryAcceptConnection: Connection {clientId} is not in Connecting state (actual: {info.m_eState}), skipping accept");
                return;
            }

            var result = SteamNetworkingSockets.AcceptConnection(conn);
            if (result == EResult.k_EResultOK)
            {
                SteamNetworkingSockets.SetConnectionPollGroup(conn, PollGroup);
                DebugConsole.Log($"[GameServer] Connection accepted from {clientId}");
            }
            else
            {
                // k_EResultInvalidState means the connection has already transitioned away
                if (result == EResult.k_EResultInvalidState)
                {
                    DebugConsole.LogWarning($"[GameServer] AcceptConnection returned InvalidState for {clientId} - connection may have already been handled or closed");
                }
                else
                {
                    RejectConnection(conn, clientId, $"Accept failed ({result})");
                }
            }
        }

        private static void RejectConnection(HSteamNetConnection conn, CSteamID clientId, string reason)
        {
            using var _ = Profiler.Scope();

            DebugConsole.LogError($"[GameServer] Rejecting connection from {clientId}: {reason}", false);
            SteamNetworkingSockets.CloseConnection(conn, 0, reason, false);
        }

        private static void OnClientConnected(HSteamNetConnection conn, CSteamID clientId)
        {
            using var _ = Profiler.Scope();

            MultiplayerSession.RegisterClient(clientId.m_SteamID, conn);

            DebugConsole.Log($"[GameServer] Connection to {clientId} fully established!");
        }

        private static void OnClientClosed(HSteamNetConnection conn, CSteamID clientId)
        {
            using var _ = Profiler.Scope();

            SteamNetworkingSockets.CloseConnection(conn, 0, null, false);

            MultiplayerSession.UnregisterClient(clientId.m_SteamID, null, conn);

            DebugConsole.Log($"[GameServer] Connection closed for {clientId}");
        }

        public override void KickClient(ulong clientId)
        {
            DebugConsole.Log($"[GameServer] Kicking client {clientId}");

            MultiplayerSession.UnregisterClient(clientId, CreateCloseFunc("Kicked by host"));
        }

        private Action<object> CreateCloseFunc(string resaon)
        {
            return (conn) =>
            {
                if (conn == null) return;
                SteamNetworkingSockets.CloseConnection((HSteamNetConnection)conn, 0, resaon, false);
            };
        }
    }
}
