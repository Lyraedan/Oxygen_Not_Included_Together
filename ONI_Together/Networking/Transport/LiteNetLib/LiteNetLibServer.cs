using LiteNetLib;
using LiteNetLib.Utils;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Misc.World;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transfer;
using ONI_Together.UI;
using Shared;
using Shared.Profiling;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace ONI_Together.Networking.Transport.Lan
{
    public class LiteNetLibServer : TransportServer
    {
        private static NetManager _server;
        private static EventBasedNetListener _listener;

        private TcpFileTransferServer _tcpTransfer;
        private readonly Dictionary<ulong, NetPeer> _peersByClientId = new Dictionary<ulong, NetPeer>();
        private readonly Dictionary<int, ulong> _clientIdByPeerId = new Dictionary<int, ulong>();
        private readonly ConcurrentQueue<(ulong clientId, byte[] data)> _incomingPackets = new ConcurrentQueue<(ulong, byte[])>();

        public static NetManager ServerInstance => _server;
        public static ulong CLIENT_ID { get; private set; } = 1;

        public bool IsRunning => _server != null && _server.IsRunning;
        public int ConnectedClientCount => _server != null ? _server.ConnectedPeersCount : 0;
        public TcpFileTransferServer TcpTransfer => _tcpTransfer;

        public void MarkClientLoading(ulong clientId) { }
        public bool ConsumeReconnectFromLoad(ulong clientId) { return false; }

        public List<ulong> ClientList { get; internal set; } = new List<ulong>();

        // Bandwidth and PPS tracking
        private long _srvLastBytesIn, _srvLastBytesOut;
        private int _srvLastMsgIn, _srvLastMsgOut;
        private float _srvInBw, _srvOutBw;
        private int _srvInPps, _srvOutPps;
        private float _srvLastBwPollTime;

        public override float IncomingBandwidth => _srvInBw;
        public override float OutgoingBandwidth => _srvOutBw;
        public override int IncomingPps => _srvInPps;
        public override int OutgoingPps => _srvOutPps;

        public override void Prepare()
        {
            using var _ = Profiler.Scope();
        }

        public override void Start()
        {
            using var _ = Profiler.Scope();

            if (_server != null)
                return;

            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STARTED, "LiteNetLib (LAN)"));

            string ip = Configuration.Instance.Host.LanSettings.Ip;
            int port = Configuration.Instance.Host.LanSettings.Port;
            int maxClients = Configuration.Instance.Host.MaxLobbySize;

            _listener = new EventBasedNetListener();
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;
            _listener.NetworkErrorEvent += OnNetworkError;
            _listener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;

            // UnsyncedEvents = false ensures ALL events fire on Unity Main Thread inside PollEvents()
            _server = new NetManager(_listener)
            {
                AutoRecycle = true,
                DisconnectTimeout = Configuration.Instance.Host.TimeoutSeconds * 1000,
                UnsyncedEvents = false,
                ChannelsCount = 4,
                BroadcastReceiveEnabled = true,
                EnableStatistics = true
            };

            bool started = _server.Start(port);
            if (!started)
            {
                DebugConsole.LogError("[LiteNetLibServer] Failed to start server on port " + port);
                OnError?.Invoke();
                return;
            }

            DebugConsole.Log("[LiteNetLibServer] LiteNetLib server started on port " + port);

            try
            {
                _tcpTransfer = new TcpFileTransferServer();
                _tcpTransfer.Start(port);
            }
            catch (Exception ex)
            {
                DebugConsole.LogWarning("[LiteNetLibServer] TCP file transfer server failed to start: " + ex.Message);
                _tcpTransfer = null;
            }

            // Register Local Host
            CLIENT_ID = 1;
            MultiplayerSession.SetHost(1);
            MultiplayerSession.InActiveSession = true;

            ClientList.Clear();
            ClientList.Add(1);

            if (!MultiplayerSession.ConnectedPlayers.TryGetValue(1, out var hostPlayer))
            {
                hostPlayer = new MultiplayerPlayer(1)
                {
                    PlayerName = Utils.GetLocalPlayerName(),
                    Connection = null
                };
                MultiplayerSession.ConnectedPlayers[1] = hostPlayer;
            }
            else
            {
                hostPlayer.PlayerName = Utils.GetLocalPlayerName();
                hostPlayer.Connection = null;
            }
            MultiplayerSession.KnownPlayerNames[1] = hostPlayer.PlayerName;

            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, hostPlayer.PlayerName));
        }

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (_server.ConnectedPeersCount < Configuration.Instance.Host.MaxLobbySize)
            {
                try
                {
                    string key = request.Data.GetString();
                    if (key == "ONI_TOGETHER")
                    {
                        ulong clientNetId = 0;
                        if (request.Data.AvailableBytes >= sizeof(ulong))
                        {
                            clientNetId = request.Data.GetULong();
                        }

                        var peer = request.Accept();
                        if (peer != null)
                        {
                            ulong assignedId = clientNetId > 1 ? clientNetId : ((ulong)peer.Id + 2);
                            _peersByClientId[assignedId] = peer;
                            _clientIdByPeerId[peer.Id] = assignedId;
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.LogWarning("[LiteNetLibServer] Error reading connection request: " + ex.Message);
                }

                request.Reject();
            }
            else
            {
                request.Reject();
            }
        }

        private void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            try
            {
                string req = reader.GetString();
                if (req == "ONI_DISCOVERY_REQ")
                {
                    var writer = new NetDataWriter();
                    writer.Put("ONI_DISCOVERY_RESP");
                    writer.Put(Utils.GetLocalPlayerName() ?? "Host");
                    writer.Put(SaveHelper.WorldName ?? "Colony");
                    writer.Put(GameClock.Instance != null ? GameClock.Instance.GetCycle() + 1 : 1);
                    writer.Put(MultiplayerSession.ConnectedPlayers.Count);
                    writer.Put(Configuration.Instance.Host.MaxLobbySize);
                    writer.Put(Configuration.Instance.Host.LanSettings.Port);
                    _server.SendUnconnectedMessage(writer, remoteEndPoint);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.LogWarning("[LiteNetLibServer] Error in discovery response: " + ex.Message);
            }
        }

        private void OnPeerConnected(NetPeer peer)
        {
            using var _ = Profiler.Scope();

            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
            {
                clientId = (ulong)peer.Id + 2;
                _peersByClientId[clientId] = peer;
                _clientIdByPeerId[peer.Id] = clientId;
            }

            if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player))
            {
                player = new MultiplayerPlayer(clientId);
                MultiplayerSession.ConnectedPlayers[clientId] = player;
            }
            player.Connection = peer;

            if (!ClientList.Contains(clientId))
                ClientList.Add(clientId);

            DebugConsole.Log("[LiteNetLibServer] Remote client connected: " + clientId + " (" + peer.Address + ":" + peer.Port + ")");
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            using var _ = Profiler.Scope();

            if (_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
            {
                _peersByClientId.Remove(clientId);
                _clientIdByPeerId.Remove(peer.Id);
                ClientList.Remove(clientId);

                if (MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player))
                {
                    player.Connection = null;
                    MultiplayerSession.ConnectedPlayers.Remove(clientId);
                    DebugConsole.Log("[LiteNetLibServer] Player " + clientId + " disconnected. Reason: " + disconnectInfo.Reason);
                }

                ReadyManager.RefreshReadyState();
                MultiplayerSession.RefreshAllPlayerCursors();
            }
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
            {
                byte[] rawData = reader.GetRemainingBytes();
                _incomingPackets.Enqueue((clientId, rawData));
            }
        }

        private void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            DebugConsole.LogWarning("[LiteNetLibServer] Network error from " + endPoint + ": " + socketError);
        }

        public override void Stop()
        {
            using var _ = Profiler.Scope();

            if (_server == null)
                return;

            _server.Stop();
            _server = null;
            _listener = null;

            _tcpTransfer?.Stop();
            _tcpTransfer = null;

            _peersByClientId.Clear();
            _clientIdByPeerId.Clear();
            ClientList.Clear();

            while (_incomingPackets.TryDequeue(out var _)) { }

            CLIENT_ID = Utils.NilUlong();
            MultiplayerSession.HostUserID = Utils.NilUlong();
            MultiplayerSession.InActiveSession = false;

            DebugConsole.Log("[LiteNetLibServer] Server stopped.");
        }

        public override void CloseConnections()
        {
            using var _ = Profiler.Scope();

            if (_server == null)
                return;

            _server.DisconnectAll();
            _peersByClientId.Clear();
            _clientIdByPeerId.Clear();
            ClientList.Clear();
            ClientList.Add(1);
        }

        public override void Update()
        {
            using var _ = Profiler.Scope();

            if (_server == null)
                return;

            _server.PollEvents();
            OnMessageRecieved();
            UpdateMetrics();
        }

        public override void OnMessageRecieved()
        {
            using var _ = Profiler.Scope();

            while (_incomingPackets.TryDequeue(out var packet))
            {
                try
                {
                    PacketHandler.HandleIncoming(packet.data);
                }
                catch (Exception ex)
                {
                    DebugConsole.LogError("[LiteNetLibServer] Exception while handling packet from " + packet.clientId + ": " + ex);
                }
            }
        }

        public override void KickClient(ulong clientId)
        {
            using var _ = Profiler.Scope();

            if (_peersByClientId.TryGetValue(clientId, out var peer))
            {
                peer.Disconnect();
                _peersByClientId.Remove(clientId);
                _clientIdByPeerId.Remove(peer.Id);
                ClientList.Remove(clientId);
                MultiplayerSession.ConnectedPlayers.Remove(clientId);
                DebugConsole.Log("[LiteNetLibServer] Kicked client: " + clientId);
            }
        }

        private void UpdateMetrics()
        {
            if (_server == null)
                return;

            float now = Time.realtimeSinceStartup;
            float dt = now - _srvLastBwPollTime;
            if (dt < 1f)
                return;

            long totalBytesIn = _server.Statistics.BytesReceived;
            long totalBytesOut = _server.Statistics.BytesSent;
            long totalPacketsIn = _server.Statistics.PacketsReceived;
            long totalPacketsOut = _server.Statistics.PacketsSent;

            _srvInBw = (totalBytesIn - _srvLastBytesIn) / dt;
            _srvOutBw = (totalBytesOut - _srvLastBytesOut) / dt;
            _srvInPps = (int)((totalPacketsIn - _srvLastMsgIn) / dt);
            _srvOutPps = (int)((totalPacketsOut - _srvLastMsgOut) / dt);

            _srvLastBytesIn = totalBytesIn;
            _srvLastBytesOut = totalBytesOut;
            _srvLastMsgIn = (int)totalPacketsIn;
            _srvLastMsgOut = (int)totalPacketsOut;
            _srvLastBwPollTime = now;
        }
    }
}
