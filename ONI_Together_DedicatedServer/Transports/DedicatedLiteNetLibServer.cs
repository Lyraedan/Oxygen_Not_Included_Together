using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using ONI_Together_DedicatedServer.ONI;

namespace ONI_Together_DedicatedServer.Transports
{
    /// <summary>
    /// LiteNetLib-based relay server aligned with ONI_Together's 0.8.1 LiteNetLib client/server transport.
    /// </summary>
    public class DedicatedLiteNetLibServer : DedicatedTransportServer
    {
        private const string HandshakeKey = "ONI_TOGETHER";
        private const string DiscoveryRequestKey = "ONI_DISCOVERY_REQ";
        private const string DiscoveryResponseKey = "ONI_DISCOVERY_RESP";

        private NetManager? _server;
        private EventBasedNetListener? _listener;
        private readonly Dictionary<ulong, ONI.Player> _connectedPlayers = new();
        private readonly Dictionary<int, ulong> _clientIdByPeerId = new();

        private string _worldName = "Dedicated World";
        private int _cycle = 1;

        public override void SetWorldInfo(string worldName, int cycle = 1)
        {
            if (!string.IsNullOrWhiteSpace(worldName))
                _worldName = worldName;
            if (cycle > 0)
                _cycle = cycle;
        }

        public override bool Start()
        {
            if (IsRunning())
                return true;

            var config = ServerConfiguration.Instance.Config;
            string ip = config.Ip?.Trim() ?? "0.0.0.0";
            int port = config.Port;
            int timeoutMs = Math.Max(5, config.TimeoutSeconds) * 1000;

            _listener = new EventBasedNetListener();
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;
            _listener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;
            _listener.NetworkErrorEvent += OnNetworkError;

            _server = new NetManager(_listener)
            {
                AutoRecycle = true,
                DisconnectTimeout = timeoutMs,
                UnsyncedEvents = false,
                ChannelsCount = 4,
                BroadcastReceiveEnabled = true,
                UnconnectedMessagesEnabled = true,
                EnableStatistics = true
            };

            bool started = StartBoundSocket(_server, ip, port);
            if (!started)
            {
                Console.WriteLine($"[DedicatedServer] Failed to start LiteNetLib server on {ip}:{port}");
                _server = null;
                _listener = null;
                return false;
            }

            Console.WriteLine($"[DedicatedServer] LiteNetLib server listening on {ip}:{port} (MaxPlayers={config.MaxLobbySize}, Timeout={config.TimeoutSeconds}s)");
            return true;
        }

        private static bool StartBoundSocket(NetManager server, string ip, int port)
        {
            if (string.IsNullOrEmpty(ip) || ip == "0.0.0.0" || ip == "*")
                return server.Start(port);

            if (IPAddress.TryParse(ip, out var parsed))
            {
                if (parsed.AddressFamily == AddressFamily.InterNetwork)
                    return server.Start(parsed, IPAddress.IPv6Any, port);
                if (parsed.AddressFamily == AddressFamily.InterNetworkV6)
                    return server.Start(IPAddress.Any, parsed, port);
            }

            return server.Start(ip, "::", port);
        }

        private void OnConnectionRequest(ConnectionRequest request)
        {
            var config = ServerConfiguration.Instance.Config;
            if (_server == null || _server.ConnectedPeersCount >= config.MaxLobbySize)
            {
                request.Reject();
                return;
            }

            try
            {
                string key = request.Data.GetString();
                if (key == HandshakeKey)
                {
                    ulong clientNetId = 0;
                    if (request.Data.AvailableBytes >= sizeof(ulong))
                        clientNetId = request.Data.GetULong();

                    var peer = request.Accept();
                    if (peer != null)
                    {
                        ulong assignedId = clientNetId > 1 ? clientNetId : ((ulong)peer.Id + 2);
                        while (_connectedPlayers.ContainsKey(assignedId))
                            assignedId++;

                        _clientIdByPeerId[peer.Id] = assignedId;
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DedicatedServer] Invalid connection request: {ex.Message}");
            }

            request.Reject();
        }

        private void OnPeerConnected(NetPeer peer)
        {
            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
            {
                clientId = (ulong)peer.Id + 2;
                while (_connectedPlayers.ContainsKey(clientId))
                    clientId++;
                _clientIdByPeerId[peer.Id] = clientId;
            }

            bool isMaster = _connectedPlayers.Count == 0;
            var player = new ONI.Player(peer, isMaster, clientId);
            _connectedPlayers[clientId] = player;

            Console.WriteLine($"[DedicatedServer] Player joined: ClientID={clientId}, Master={isMaster}, Endpoint={peer.Address}:{peer.Port}");
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
                return;

            _clientIdByPeerId.Remove(peer.Id);

            bool wasMaster = false;
            if (_connectedPlayers.TryGetValue(clientId, out var player))
            {
                wasMaster = player.IsMaster;
                _connectedPlayers.Remove(clientId);
            }

            Console.WriteLine($"[DedicatedServer] Player left: ClientID={clientId}, Reason={disconnectInfo.Reason}");

            if (!wasMaster)
                return;

            Console.WriteLine("[DedicatedServer] Master disconnected! Attempting master migration...");
            if (_connectedPlayers.Count > 0)
            {
                var newMaster = _connectedPlayers.Values
                    .Where(p => p.Connection.ConnectionState == ConnectionState.Connected)
                    .OrderBy(p => p.Connection.Ping)
                    .FirstOrDefault();

                if (newMaster != null)
                {
                    newMaster.UpdateMasterState(true);
                    Console.WriteLine($"[DedicatedServer] New master assigned: ClientID={newMaster.ClientID} (Ping={newMaster.PingMs}ms)");
                }
            }
            else
            {
                Console.WriteLine("[DedicatedServer] No clients remaining. Server idle.");
            }
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong senderId) ||
                !_connectedPlayers.TryGetValue(senderId, out var sender))
            {
                return;
            }

            byte[] rawData = reader.GetRemainingBytes();
            if (rawData.Length == 0)
                return;

            if (ServerConfiguration.Instance.Config.VerbosePacketLogging)
            {
                int packetType = rawData.Length >= 4 ? BitConverter.ToInt32(rawData, 0) : 0;
                Console.WriteLine($"[DedicatedServer] Packet from {senderId} (Master={sender.IsMaster}): Type={packetType}, Size={rawData.Length}B, Channel={channelNumber}, Method={deliveryMethod}");
            }

            if (sender.IsMaster)
            {
                foreach (var player in _connectedPlayers.Values)
                {
                    if (!player.IsMaster && player.Connection.ConnectionState == ConnectionState.Connected)
                        player.Connection.Send(rawData, channelNumber, deliveryMethod);
                }
            }
            else
            {
                var master = _connectedPlayers.Values.FirstOrDefault(p => p.IsMaster);
                if (master != null && master.Connection.ConnectionState == ConnectionState.Connected)
                    master.Connection.Send(rawData, channelNumber, deliveryMethod);
            }
        }

        private void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            if (_server == null)
                return;

            try
            {
                string req = reader.GetString();
                if (req == DiscoveryRequestKey)
                {
                    var config = ServerConfiguration.Instance.Config;
                    var writer = new NetDataWriter();
                    writer.Put(DiscoveryResponseKey);
                    writer.Put(config.ServerName);
                    writer.Put(_worldName);
                    writer.Put(_cycle);
                    writer.Put(_connectedPlayers.Count);
                    writer.Put(config.MaxLobbySize);
                    writer.Put(config.Port);
                    _server.SendUnconnectedMessage(writer, remoteEndPoint);
                }
            }
            catch
            {
                // Ignore malformed unconnected UDP probes
            }
        }

        private void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Console.WriteLine($"[DedicatedServer] Network error from {endPoint}: {socketError}");
        }

        public override void Update()
        {
            _server?.PollEvents();
        }

        public override void Stop()
        {
            if (_server == null)
                return;

            _server.DisconnectAll();
            _server.Stop();
            _server = null;
            _listener = null;
            _connectedPlayers.Clear();
            _clientIdByPeerId.Clear();
            Console.WriteLine("[DedicatedServer] LiteNetLib server stopped.");
        }

        public override bool IsRunning()
        {
            return _server != null && _server.IsRunning;
        }

        public override Dictionary<ulong, ONI.Player> GetPlayers()
        {
            return _connectedPlayers;
        }
    }
}
