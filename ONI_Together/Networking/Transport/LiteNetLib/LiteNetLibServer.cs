using LiteNetLib;
using LiteNetLib.Utils;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
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
        private static NetManager _hostClient;
        private static EventBasedNetListener _hostClientListener;

        private TcpFileTransferServer _tcpTransfer;
        private readonly Dictionary<ulong, NetPeer> _peersByClientId = new Dictionary<ulong, NetPeer>();
        private readonly Dictionary<int, ulong> _clientIdByPeerId = new Dictionary<int, ulong>();
        private readonly ConcurrentQueue<(ulong clientId, byte[] data)> _incomingPackets = new ConcurrentQueue<(ulong, byte[])>();

        public static NetManager ServerInstance => _server;
        public static NetManager HostClient => _hostClient;
        public static ulong CLIENT_ID { get; private set; }

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

            _server = new NetManager(_listener)
            {
                AutoRecycle = true,
                DisconnectTimeout = Configuration.Instance.Host.TimeoutSeconds * 1000,
                UnsyncedEvents = true,
                ChannelsCount = 4
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

            // Start local host client
            _hostClientListener = new EventBasedNetListener();
            _hostClientListener.PeerConnectedEvent += OnHostClientConnected;
            _hostClientListener.PeerDisconnectedEvent += OnHostClientDisconnected;
            _hostClientListener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                byte[] raw = reader.GetRemainingBytes();
                _incomingPackets.Enqueue((1, raw));
            };

            _hostClient = new NetManager(_hostClientListener)
            {
                AutoRecycle = true,
                DisconnectTimeout = Configuration.Instance.HostTimeoutSeconds * 1000,
                UnsyncedEvents = true
            };
            _hostClient.Start();
            _hostClient.Connect("127.0.0.1", port, "ONI_TOGETHER");
        }

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (_server.ConnectedPeersCount < Configuration.Instance.Host.MaxLobbySize)
            {
                request.AcceptIfKey("ONI_TOGETHER");
            }
            else
            {
                request.Reject();
            }
        }

        private void OnPeerConnected(NetPeer peer)
        {
            using var _ = Profiler.Scope();

            ulong clientId = (ulong)peer.Id + 1; // 1-based unique client ID
            _peersByClientId[clientId] = peer;
            _clientIdByPeerId[peer.Id] = clientId;

            if (!MultiplayerSession.ConnectedPlayers.TryGetValue(clientId, out var player))
            {
                player = new MultiplayerPlayer(clientId);
                MultiplayerSession.ConnectedPlayers[clientId] = player;
            }
            player.Connection = peer;

            if (!ClientList.Contains(clientId))
                ClientList.Add(clientId);

            DebugConsole.Log("[LiteNetLibServer] Client connected: " + clientId + " (" + peer.Address + ":" + peer.Port + ")");
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

        private void OnHostClientConnected(NetPeer peer)
        {
            CLIENT_ID = 1;
            MultiplayerSession.SetHost(1);
            MultiplayerSession.InActiveSession = true;

            string hostName = Utils.GetLocalPlayerName();
            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_JOINED, hostName));
            DebugConsole.Log("[LiteNetLibServer] Host client connected!");
        }

        private void OnHostClientDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            CLIENT_ID = Utils.NilUlong();
            MultiplayerSession.HostUserID = Utils.NilUlong();
            MultiplayerSession.InActiveSession = false;
            DebugConsole.Log("[LiteNetLibServer] Host client disconnected!");
        }

        public override void Stop()
        {
            using var _ = Profiler.Scope();

            OxySyncChat.AddSystemMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STOPPED, "LiteNetLib"));

            _tcpTransfer?.Stop();
            _tcpTransfer = null;

            _hostClient?.Stop();
            _hostClient = null;

            _server?.Stop();
            _server = null;

            _peersByClientId.Clear();
            _clientIdByPeerId.Clear();
            ClientList.Clear();
            MultiplayerSession.InActiveSession = false;
        }

        public override void CloseConnections()
        {
            _server?.DisconnectAll();
        }

        public override void Update()
        {
            using var _ = Profiler.Scope();

            _server?.PollEvents();
            _hostClient?.PollEvents();
            OnMessageRecieved();
            UpdateBandwidth();
        }

        public override void OnMessageRecieved()
        {
            using var _ = Profiler.Scope();

            while (_incomingPackets.TryDequeue(out var item))
            {
                try
                {
                    PacketHandler.HandleIncoming(item.data);
                }
                catch (Exception ex)
                {
                    DebugConsole.LogError("[LiteNetLibServer] Error processing packet: " + ex);
                }
            }
        }

        public override void KickClient(ulong clientId)
        {
            if (_peersByClientId.TryGetValue(clientId, out var peer))
            {
                peer.Disconnect();
            }
        }

        private void UpdateBandwidth()
        {
            float now = Time.unscaledTime;
            if (now - _srvLastBwPollTime < 0.5f) return;
            float dt = now - _srvLastBwPollTime;
            _srvLastBwPollTime = now;

            if (_server != null)
            {
                var stats = _server.Statistics;
                long bytesIn = stats.BytesReceived;
                long bytesOut = stats.BytesSent;
                long packetsIn = stats.PacketsReceived;
                long packetsOut = stats.PacketsSent;

                _srvInBw = (bytesIn - _srvLastBytesIn) / dt;
                _srvOutBw = (bytesOut - _srvLastBytesOut) / dt;
                _srvInPps = (int)((packetsIn - _srvLastMsgIn) / dt);
                _srvOutPps = (int)((packetsOut - _srvLastMsgOut) / dt);

                _srvLastBytesIn = bytesIn;
                _srvLastBytesOut = bytesOut;
                _srvLastMsgIn = (int)packetsIn;
                _srvLastMsgOut = (int)packetsOut;
            }
        }
    }
}
