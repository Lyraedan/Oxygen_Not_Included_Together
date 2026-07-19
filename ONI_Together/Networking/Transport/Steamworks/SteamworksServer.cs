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
using ONI_Together.UI;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;

namespace ONI_Together.Networking.Transport.Steam
{
    public class SteamworksServer : TransportServer
    {
        public static HSteamListenSocket ListenSocket { get; private set; }
        public static HSteamNetPollGroup PollGroup { get; private set; }
        private static Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChangedCallback;

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

            ChatScreen.PendingMessage pending = ChatScreen.GeneratePendingMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STARTED, $"Steam"));
            ChatScreen.QueueMessage(pending);

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

            MultiplayerSession.InSession = true;
        }

        public override void Stop()
        {
            using var _ = Profiler.Scope();

            ChatScreen.PendingMessage pending = ChatScreen.GeneratePendingMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STOPPED, $"Steam"));
            ChatScreen.QueueMessage(pending);

            if (PollGroup.m_HSteamNetPollGroup != 0)
                SteamNetworkingSockets.DestroyPollGroup(PollGroup);

            if (ListenSocket.m_HSteamListenSocket != 0)
                SteamNetworkingSockets.CloseListenSocket(ListenSocket);

            MultiplayerSession.InSession = false;
        }

        public override void CloseConnections()
        {
            using var _ = Profiler.Scope();

            // Close all client connections and clean up
            foreach (var player in MultiplayerSession.ConnectedPlayers.Values)
            {
                if (player.Connection != null)
                {
					object connection = player.Connection;
                    if (player.Connection is HSteamNetConnection)
                    {
                        var conn = (HSteamNetConnection) player.Connection;
                        SteamNetworkingSockets.CloseConnection(conn, 0, "Shutdown", false);
                    }
					player.EndConnection(connection, player.ConnectionGeneration);
                }
            }
        }

        public override void Update()
        {
            using var _ = Profiler.Scope();

            SteamAPI.RunCallbacks();
            SteamNetworkingSockets.RunCallbacks();
        }

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
                try
                {
                    var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(messages[i]);
                    totalBytes += msg.m_cbSize;
                    byte[] bytes = new byte[msg.m_cbSize];
                    Marshal.Copy(msg.m_pData, bytes, 0, msg.m_cbSize);

                    ulong senderId = msg.m_identityPeer.GetSteamID64();
					if (!MultiplayerSession.ConnectedPlayers.TryGetValue(senderId, out var player)
					    || !player.IsCurrentConnection(msg.m_conn, player.ConnectionGeneration))
					{
						DebugConsole.LogWarning($"[SteamworksServer] Rejected stale connection message from {senderId}");
						continue;
					}
                    PacketHandler.HandleIncoming(bytes, new DispatchContext(
                        senderId,
						senderId == MultiplayerSession.HostUserID,
						player.ConnectionGeneration));
                }
                catch (Exception ex)
                {
                    DebugConsole.LogWarning($"[SteamworksServer] Rejected malformed message: {ex}");
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(messages[i]);
                }
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

			if (!SteamLobby.IsCurrentLobbyMember(clientId.m_SteamID))
			{
				RejectConnection(conn, clientId, "Steam identity is not a member of the active lobby");
				return;
			}

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

            MultiplayerPlayer player;
            if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId.m_SteamID, out player))
            {
                player = new MultiplayerPlayer(clientId.m_SteamID);
                MultiplayerSession.ConnectedPlayers.Add(clientId.m_SteamID, player);
                //MultiplayerSession.ConnectedPlayers[clientId] = player;
            }
			player.BeginConnection(conn);

            DebugConsole.Log($"[GameServer] Connection to {clientId} fully established!");
            //SaveFileRequestPacket.SendSaveFile(clientId); // Old method
            //GoogleDriveUtils.UploadAndSendToClient(clientId); // Upload to googledrive and send to the client
        }

		private static void OnClientClosed(HSteamNetConnection conn, CSteamID clientId)
        {
            using var _ = Profiler.Scope();

            SteamNetworkingSockets.CloseConnection(conn, 0, null, false);

			bool closedCurrentSession = TryCleanupClientSession(clientId.m_SteamID, conn);

            DebugConsole.Log($"[GameServer] Connection closed for {clientId}");

			if (closedCurrentSession)
				ReadyManager.RefreshReadyState();
            // Do I wanna auto shutdown here? I don't think so
            // if (MultiplayerSession.ConnectedPlayers.Count == 0)
            // {
            //     SetState(ServerState.Stopped);
            //     Shutdown
            // }
        }

		internal static bool TryCleanupClientSession(ulong clientId, object connection)
		{
			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out MultiplayerPlayer player)
			    || !player.EndConnection(connection, player.ConnectionGeneration))
				return false;

			SaveFileTransferManager.CancelTransfers(clientId);
			return true;
		}

        public override void KickClient(ulong clientId)
        {
            if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player))
            {
                DebugConsole.LogWarning($"[GameServer] KickClient: Client {clientId} not found.");
                return;
            }

            if (player.Connection == null)
            {
                DebugConsole.LogWarning($"[GameServer] KickClient: Client {clientId} has no active connection.");
                return;
            }

            if (player.Connection is HSteamNetConnection conn)
            {
                DebugConsole.Log($"[GameServer] Kicking client {clientId}");

                SteamNetworkingSockets.CloseConnection(conn, 0, "Kicked by host", false);
                // The connection closed callback will handle cleanup
            }
            else
            {
                DebugConsole.LogError($"[GameServer] KickClient: Invalid connection type for {clientId}");
            }
        }
    }
}
