using System;
using Riptide;
using Riptide.Utils;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.States;
using Shared.Profiling;
using ONI_Together.Networking.Transfer;
using System.Collections.Generic;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.UI;
using Shared;
using Steamworks;
using static ResearchTypes;
using UnityEngine;

namespace ONI_Together.Networking.Transport.Lan
{
    public class RiptideServer : TransportServer
    {
        private static Server _server;
        private static Client _client; // Server client (Other users will use GameClient)
        private TcpFileTransferServer _tcpTransfer;

        public TcpFileTransferServer TcpTransfer => _tcpTransfer;

        public static Server ServerInstance
        {
            get { return _server; }
        }

        public static Client Client
        {
            get { return _client; }
        }

        public List<ulong> ClientList { get; internal set; } = new();

        public static ulong CLIENT_ID { get; private set; }

        // Bandwidth tracking via server-side Connection.Metrics
        private long _srvLastBytesIn, _srvLastBytesOut;
        private int _srvLastMsgIn, _srvLastMsgOut;
        private float _srvInBw, _srvOutBw;
        private int _srvInPps, _srvOutPps;
        private float _srvLastBwPollTime;

        public override void Prepare()
        {
            using var _ = Profiler.Scope();

            RiptideLogger.Initialize(DebugConsole.Log, false);
        }

        public override void Start()
        {
            using var _ = Profiler.Scope();

            if (_server != null)
                return;

            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STARTED, "Riptide (LAN)"));

            string ip = Configuration.Instance.Host.LanSettings.Ip;
            int port = Configuration.Instance.Host.LanSettings.Port;
            int maxClients = Configuration.Instance.Host.MaxLobbySize;
            RiptideClient.MaxServerCapacity = maxClients;

            _server = new Server("Lan/Riptide");
            _server.TimeoutTime = Configuration.Instance.Host.TimeoutSeconds * 1000;
            _server.MessageReceived += OnServerMessageReceived;
            _server.ConnectionFailed += OnClientConnectionFailed;
            _server.ClientConnected += ServerOnClientConnected;
            _server.ClientDisconnected += ServerOnClientDisconnected;
            _server.Start((ushort)port, (ushort)maxClients, useMessageHandlers: false);
            DebugConsole.Log("[RiptideServer] Riptide server started!");

            try
            {
                _tcpTransfer = new TcpFileTransferServer();
                _tcpTransfer.Start(port);
            }
            catch (Exception ex)
            {
                DebugConsole.LogWarning($"[RiptideServer] TCP file transfer server failed to start: {ex.Message}. Save transfers will use UDP fallback.");
                _tcpTransfer = null;
            }

            _client = new Client("Lan/Riptide/HostClient");
            _client.Connected += OnLocalClientConnected;
            _client.Disconnected += OnLocalClientDisconnected;
            DebugConsole.Log("[RiptideServer] Connecting host client!");
            _client.Connect($"{ip}:{port}", useMessageHandlers: false);
            _client.TimeoutTime = Configuration.Instance.HostTimeoutSeconds * 1000;
        }

        private void OnClientConnectionFailed(object sender, ServerConnectionFailedEventArgs e)
        {
            using var _ = Profiler.Scope();

            int id = e.Client.Id;
            DebugConsole.Log("[RiptideServer] A client failed to connect to the server.");
            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_FAILED, "A client"));
        }

        private void OnLocalClientConnected(object sender, EventArgs e)
        {
            using var _ = Profiler.Scope();

            CLIENT_ID = _client.Id;
            //AddClientToList(CLIENT_ID);
            DebugConsole.Log("[RiptideServer] Host client connected to server!");
            MultiplayerSession.SetHost(GetClientID());
            MultiplayerSession.InActiveSession = true;

            string hostName = Utils.GetLocalPlayerName();
            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, hostName));
        }

        private void OnLocalClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            using var _ = Profiler.Scope();

            CLIENT_ID = Utils.NilUlong();
            //RemoveClientFromList(CLIENT_ID);
            DebugConsole.Log("[RiptideServer] Host client disconnected from server!");
            MultiplayerSession.HostUserID = Utils.NilUlong();
            MultiplayerSession.InActiveSession = false;
        }

        private void ServerOnClientConnected(object sender, ServerConnectedEventArgs e)
        {
            using var _ = Profiler.Scope();

            ulong clientId = e.Client.Id;
            MultiplayerPlayer player;
            if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out player))
            {
                player = new MultiplayerPlayer(clientId);
                MultiplayerSession.ConnectedPlayers.Add(clientId, player);
            }
            player.Connection = e.Client;

            e.Client.CanQualityDisconnect = false;
            e.Client.MaxSendAttempts = 30;
            e.Client.MaxAvgSendAttempts = 12;
            e.Client.AvgSendAttemptsResilience = 128;

            if (clientId == CLIENT_ID)
            {
                player.PlayerName = Utils.GetLocalPlayerName();
            }

            // Authority: a (re)connecting client is loading and must be forced Unready the
            // moment it begins connecting — not just at object creation. This keeps the
            // host's all-ready check from transiently passing while the client loads.
            // SetPlayerReadyState safely no-ops for the host's own entry.
            ReadyManager.SetPlayerReadyState(player, ClientReadyState.Unready);

            AddClientToList(e.Client.Id);
            DebugConsole.Log($"New client connected: {clientId}");

            ReadyManager.HandleClientConnected();
        }

        private void ServerOnClientDisconnected(object sender, ServerDisconnectedEventArgs e)
        {
            using var _ = Profiler.Scope();

            ulong clientId = e.Client.Id;

            RemoveClientFromList(clientId);

            if (MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out MultiplayerPlayer player))
            {
                player.Connection = null;
                MultiplayerSession.ConnectedPlayers.Remove(clientId);
                DebugConsole.Log($"Player {clientId} disconnected.");
            }
            else
            {
                DebugConsole.LogWarning($"Disconnected client {clientId} was not found in ConnectedPlayers.");
            }
            ReadyManager.RefreshReadyState();
            MultiplayerSession.RefreshAllPlayerCursors();
        }

        private void OnServerMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using var _ = Profiler.Scope();

            ulong clientId = e.FromConnection.Id;
            byte[] rawData = e.Message.GetBytes();
            int size = rawData.Length;

            int packetType = 0;
            if (rawData.Length >= 4)
                packetType = BitConverter.ToInt32(rawData, 0);

            //DebugConsole.Log(
            //    $"[Riptide] Server received packet from {clientId}, " +
            //    $"PacketType={packetType}, Size={size} bytes"
            //);

            var scope = Profiler.Scope();

            try
            {
                PacketHandler.HandleIncoming(rawData);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LanServer] Failed to handle packet {packetType}: {ex}");
            }

            scope.End(1, size);
        }

        private void UpdateServerBandwidth()
        {
            if (_server == null || !_server.IsRunning)
            {
                _srvInBw = 0f;
                _srvOutBw = 0f;
                _srvInPps = 0;
                _srvOutPps = 0;
                return;
            }

            float now = Time.realtimeSinceStartup;
            float dt = now - _srvLastBwPollTime;
            if (dt < 1f) return;

            long totalIn = 0, totalOut = 0;
            int totalMsgIn = 0, totalMsgOut = 0;

            foreach (Connection client in _server.Clients)
            {
                if (client == null || client.IsNotConnected) continue;
                if (client.Id == CLIENT_ID) continue; // exclude loopback client

                var m = client.Metrics;
                if (m == null) continue;

                totalIn += m.BytesIn;
                totalOut += m.BytesOut;
                totalMsgIn += (int)m.MessagesIn;
                totalMsgOut += (int)m.MessagesOut;
            }

            _srvInBw = (totalIn - _srvLastBytesIn) / dt;
            _srvOutBw = (totalOut - _srvLastBytesOut) / dt;
            _srvInPps = (int)((totalMsgIn - _srvLastMsgIn) / dt);
            _srvOutPps = (int)((totalMsgOut - _srvLastMsgOut) / dt);

            _srvLastBytesIn = totalIn;
            _srvLastBytesOut = totalOut;
            _srvLastMsgIn = totalMsgIn;
            _srvLastMsgOut = totalMsgOut;
            _srvLastBwPollTime = now;
        }

        public override float IncomingBandwidth => _srvInBw;
        public override float OutgoingBandwidth => _srvOutBw;
        public override int IncomingPps => _srvInPps;
        public override int OutgoingPps => _srvOutPps;

        public override void Stop()
        {
            using var _ = Profiler.Scope();

            if (_server == null)
                return;

            if (!_server.IsRunning)
                return;

            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STOPPED, "LAN"));

            if (!_client.IsNotConnected)
            {
                _client.Disconnect();
                _client = null;
            }

            _tcpTransfer?.Stop();
            _tcpTransfer = null;

            _server.Stop();
            _server = null;

            ClearLoadTracking();
        }

        // The server is shutting down so disconnect everyone
        public override void CloseConnections()
        {
            using var _ = Profiler.Scope();

            if (_server == null || !_server.IsRunning)
                return;

            // Disconnect all clients
            foreach (Connection client in _server.Clients)
            {
                if (!client.IsNotConnected)
                {
                    DebugConsole.Log($"Client {client.Id} disconnected by server shutdown.");
                    _server.DisconnectClient(client);
                }
            }

            // Clear our session player list
            MultiplayerSession.ConnectedPlayers.Clear();
        }

        public override void OnMessageRecieved()
        {
            // Riptide uses its own OnServerMessageReceived function
        }

        public override void Update()
        {
            using var _ = Profiler.Scope();

            _server?.Update();
            _client?.Update();
            UpdateServerBandwidth();

            ExpireStaleLoadingClients();
        }

        /// <summary>
        /// Riptide hands a reconnecting client a brand-new id, so an exact match is impossible
        /// and the choice is between two wrong answers. Leaving the entry pending keeps the
        /// resume gate closed for the full LOAD_RECONNECT_TIMEOUT after everyone is already
        /// back; consuming the oldest pending entry can instead open the gate early if the
        /// connect is a *new* player who joined during someone else's load. The stall is the
        /// common case and the mis-guess needs two clients moving at once, so we take the
        /// guess. Sending a persistent id in Riptide's connect payload the way LiteNetLib does
        /// would remove the choice entirely.
        /// </summary>
        public override bool ClaimLoadingReconnect(ulong clientId)
        {
            if (base.ClaimLoadingReconnect(clientId))
                return true;

            ClaimOldestLoadingReconnect(clientId);
            return true;
        }

        public void AddClientToList(ulong id)
        {
            using var _ = Profiler.Scope();

            if (ClientList.Contains(id))
                return;

            ClientList.Add(id);

            ClaimLoadingReconnect(id);

			var boxedId = Boxed<ulong>.Get(id);
			Game.Instance?.Trigger(MP_HASHES.OnPlayerJoined,boxedId);
            boxedId.Release();
        }

        public void RemoveClientFromList(ulong id)
        {
            using var _ = Profiler.Scope();

            if (!ClientList.Contains(id))
                return;

            ClientList.Remove(id);

            if (!IsClientLoading(id))
            {
                string name = MultiplayerSession.GetPlayer(id)?.PlayerName ?? $"Player {id}";
                OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_LEFT, name));
                Utils.PauseSimOnPlayerLeft();
			}
			var boxedId = Boxed<ulong>.Get(id);
			Game.Instance?.Trigger(MP_HASHES.OnPlayerLeft, boxedId);
            boxedId.Release();
        }
        public ulong GetClientID()
        {
            using var _ = Profiler.Scope();

            if (_client == null || _client.IsNotConnected)
                return Utils.NilUlong();

            return _client.Id;
        }

        public override void KickClient(ulong clientId)
        {
            if (_server == null || !_server.IsRunning)
            {
                DebugConsole.LogWarning("[RiptideServer] KickClient: Server is not running.");
                return;
            }

            if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player))
            {
                DebugConsole.LogWarning($"[RiptideServer] KickClient: Client {clientId} not found.");
                return;
            }

            if (player.Connection is Connection conn)
            {
                if (conn.IsNotConnected)
                {
                    DebugConsole.LogWarning($"[RiptideServer] KickClient: Client {clientId} already disconnected.");
                    return;
                }

                DebugConsole.Log($"[RiptideServer] Kicking client {clientId}");

                // A kicked client is not coming back, so its pending load must not keep
                // holding the gate. The disconnect event below carries the refresh.
                ForgetClientLoading(clientId);
                _server.DisconnectClient(conn);

                // OnClientDisconnected should disconnect so we shouldn't need to cleanup here
            }
            else
            {
                DebugConsole.LogError($"[RiptideServer] KickClient: Invalid connection type for {clientId}");
            }
        }
    }
}
