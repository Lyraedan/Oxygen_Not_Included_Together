using System;
using Riptide;
using Riptide.Utils;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking.Packets.Architecture;
using Shared.Profiling;
using ONI_MP.Networking.Transfer;
using System.Collections.Generic;
using ONI_MP.UI;
using Steamworks;
using ONI_MP.Menus;
using ONI_MP.Networking;
using static ResearchTypes;

namespace ONI_MP.Networking.Transport.Riptide
{
    public class RiptideServer : TransportServer
    {
        private static Server _server;
        private static Client _client; // Server client (Other users will use GameClient)
        private TcpFileTransferServer _tcpTransfer;
        private Dictionary<ulong, float> _loadingClients = new Dictionary<ulong, float>();
        private List<ulong> _expiredLoadingClients = new List<ulong>();
        private HashSet<ulong> _reconnectedFromLoad = new HashSet<ulong>();
        private const float LOADING_TIMEOUT = 30f;

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

            ChatScreen.PendingMessage pending = ChatScreen.GeneratePendingMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STARTED, $"LAN"));
            ChatScreen.QueueMessage(pending);

            string ip = Configuration.Instance.Host.LanSettings.Ip;
            int port = Configuration.Instance.Host.LanSettings.Port;
            int maxClients = Configuration.Instance.Host.MaxLobbySize;

            _server = new Server("Lan/Riptide");
            _server.TimeoutTime = 30000;
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
        }

        private void OnClientConnectionFailed(object sender, ServerConnectionFailedEventArgs e)
        {
            using var _ = Profiler.Scope();

            int id = e.Client.Id;
            DebugConsole.Log("[RiptideServer] A client failed to connect to the server.");
            ChatScreen.PendingMessage pending = ChatScreen.GeneratePendingMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_FAILED, "A client"));
            ChatScreen.QueueMessage(pending);
        }

        private void OnLocalClientConnected(object sender, EventArgs e)
        {
            using var _ = Profiler.Scope();

            CLIENT_ID = _client.Id;
            //AddClientToList(CLIENT_ID);
            DebugConsole.Log("[RiptideServer] Host client connected to server!");
            MultiplayerSession.SetHost(GetClientID());
            MultiplayerSession.InSession = true;

            string hostName = Utils.GetLocalPlayerName();
            ChatScreen.PendingMessage pending = ChatScreen.GeneratePendingMessage(
                string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, hostName));
            ChatScreen.QueueMessage(pending);
        }

        private void OnLocalClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            using var _ = Profiler.Scope();

            CLIENT_ID = Utils.NilUlong();
            //RemoveClientFromList(CLIENT_ID);
            DebugConsole.Log("[RiptideServer] Host client disconnected from server!");
            MultiplayerSession.HostUserID = Utils.NilUlong();
            MultiplayerSession.InSession = false;
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

            AddClientToList(e.Client.Id);
            DebugConsole.Log($"New client connected: {clientId}");
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

            DebugConsole.Log(
                $"[Riptide] Server received packet from {clientId}, " +
                $"PacketType={packetType}, Size={size} bytes"
            );

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

        public override void Stop()
        {
            using var _ = Profiler.Scope();

            if (_server == null)
                return;

            if (!_server.IsRunning)
                return;

            ChatScreen.PendingMessage pending = ChatScreen.GeneratePendingMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STOPPED, $"LAN"));
            ChatScreen.QueueMessage(pending);

            if (!_client.IsNotConnected)
            {
                _client.Disconnect();
                _client = null;
            }

            _tcpTransfer?.Stop();
            _tcpTransfer = null;

            _server.Stop();
            _server = null;
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

            if (_loadingClients.Count > 0)
            {
                float now = UnityEngine.Time.unscaledTime;
                _expiredLoadingClients.Clear();
                foreach (var kvp in _loadingClients)
                {
                    if (now - kvp.Value > LOADING_TIMEOUT)
                    {
                        _expiredLoadingClients.Add(kvp.Key);
                    }
                }
                foreach (var id in _expiredLoadingClients)
                {
                    _loadingClients.Remove(id);
                }
            }
        }

        public bool ConsumeReconnectFromLoad(ulong id)
        {
            return _reconnectedFromLoad.Remove(id);
        }

        public void MarkClientLoading(ulong id)
        {
            _loadingClients[id] = UnityEngine.Time.unscaledTime;
        }

        public void AddClientToList(ulong id)
        {
            using var _ = Profiler.Scope();

            if (ClientList.Contains(id))
                return;

            ClientList.Add(id);

            // A loading client reconnects with a new Riptide ID, so we consume one loading entry
            if (_loadingClients.Count > 0)
            {
                var enumerator = _loadingClients.GetEnumerator();
                enumerator.MoveNext();
                _loadingClients.Remove(enumerator.Current.Key);
                _reconnectedFromLoad.Add(id);
            }
            Game.Instance?.Trigger(MP_HASHES.OnPlayerJoined);
        }

        public void RemoveClientFromList(ulong id)
        {
            using var _ = Profiler.Scope();

            if (!ClientList.Contains(id))
                return;

            ClientList.Remove(id);

            if (!_loadingClients.ContainsKey(id))
            {
                string name = MultiplayerSession.GetPlayer(id)?.PlayerName ?? $"Player {id}";
                ChatScreen.PendingMessage pending = ChatScreen.GeneratePendingMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_LEFT, name));
                ChatScreen.QueueMessage(pending);
            }
            Game.Instance?.Trigger(MP_HASHES.OnPlayerLeft);
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
