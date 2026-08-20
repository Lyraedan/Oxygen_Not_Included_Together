using LiteNetLib;
using LiteNetLib.Utils;
using ONI_Together_DedicatedServer.ONI;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace ONI_Together_DedicatedServer.Transports
{
    public class DedicatedLiteNetLibServer : DedicatedTransportServer
    {
        private NetManager? _server;
        private EventBasedNetListener? _listener;
        public Dictionary<ulong, ONI.Player> ConnectedPlayers = new Dictionary<ulong, ONI.Player>();
        private readonly Dictionary<int, ulong> _clientIdByPeerId = new Dictionary<int, ulong>();

        public override void Start()
        {
            using var _ = Profiler.Scope();

            if (IsRunning())
                return;

            string ip = ServerConfiguration.Instance.Config.Ip;
            int port = ServerConfiguration.Instance.Config.Port;
            int maxPlayers = ServerConfiguration.Instance.Config.MaxLobbySize;

            _listener = new EventBasedNetListener();
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;
            _listener.NetworkReceiveUnconnectedEvent += OnNetworkReceiveUnconnected;

            _server = new NetManager(_listener)
            {
                AutoRecycle = true,
                DisconnectTimeout = ServerConfiguration.Instance.Config.TimeoutSeconds * 1000,
                UnsyncedEvents = false,
                ChannelsCount = 4,
                DiscoveryEnabled = true
            };

            bool started = _server.Start(port);
            if (started)
            {
                Console.WriteLine($"[DedicatedServer] LiteNetLib server started on {ip}:{port}");
            }
            else
            {
                Console.WriteLine($"[DedicatedServer] Failed to start server on port {port}");
            }
        }

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (_server != null && _server.ConnectedPeersCount < ServerConfiguration.Instance.Config.MaxLobbySize)
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
                            _clientIdByPeerId[peer.Id] = assignedId;
                        }
                        return;
                    }
                }
                catch { }

                request.Reject();
            }
            else
            {
                request.Reject();
            }
        }

        private void OnPeerConnected(NetPeer peer)
        {
            using var _ = Profiler.Scope();

            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
            {
                clientId = (ulong)peer.Id + 2;
                _clientIdByPeerId[peer.Id] = clientId;
            }

            bool isMaster = ConnectedPlayers.Count == 0;
            var player = new Player(peer, isMaster, clientId);
            ConnectedPlayers[clientId] = player;

            Console.WriteLine($"[DedicatedServer] Player joined: ClientID={clientId}, IsMaster={isMaster} ({peer.Address}:{peer.Port})");
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            using var _ = Profiler.Scope();

            if (_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
            {
                _clientIdByPeerId.Remove(peer.Id);
                bool wasMaster = false;

                if (ConnectedPlayers.TryGetValue(clientId, out var player))
                {
                    wasMaster = player.IsMaster;
                    ConnectedPlayers.Remove(clientId);
                }

                Console.WriteLine($"[DedicatedServer] Player disconnected: ClientID={clientId}, Reason={disconnectInfo.Reason}");

                if (wasMaster)
                {
                    Console.WriteLine("[DedicatedServer] Master disconnected! Migrating master role...");
                    if (ConnectedPlayers.Count > 0)
                    {
                        var newMaster = ConnectedPlayers.Values.OrderBy(p => p.Connection.Ping).FirstOrDefault();
                        if (newMaster != null)
                        {
                            newMaster.UpdateMasterState(true);
                            Console.WriteLine($"[DedicatedServer] New master assigned: ClientID={newMaster.ClientID} (Ping: {newMaster.Connection.Ping}ms)");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[DedicatedServer] No clients remaining. Server idle.");
                    }
                }
            }
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            byte[] rawData = reader.GetRemainingBytes();

            // Relay packet to other connected peers
            foreach (var kvp in ConnectedPlayers)
            {
                if (kvp.Value.Connection.Id != peer.Id && kvp.Value.Connection.ConnectionState == ConnectionState.Connected)
                {
                    kvp.Value.Connection.Send(rawData, channelNumber, deliveryMethod);
                }
            }
        }

        private void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            if (messageType == UnconnectedMessageType.DiscoveryRequest && _server != null)
            {
                try
                {
                    string req = reader.GetString();
                    if (req == "ONI_DISCOVERY_REQ")
                    {
                        var writer = new NetDataWriter();
                        writer.Put("ONI_DISCOVERY_RESP");
                        writer.Put("Dedicated Server");
                        writer.Put("Dedicated World");
                        writer.Put(1);
                        writer.Put(ConnectedPlayers.Count);
                        writer.Put(ServerConfiguration.Instance.Config.MaxLobbySize);
                        writer.Put(ServerConfiguration.Instance.Config.Port);
                        _server.SendDiscoveryResponse(writer, remoteEndPoint);
                    }
                }
                catch { }
            }
        }

        public override void Update()
        {
            _server?.PollEvents();
        }

        public override void Stop()
        {
            _server?.Stop();
            _server = null;
            ConnectedPlayers.Clear();
            _clientIdByPeerId.Clear();
            Console.WriteLine("[DedicatedServer] Server stopped.");
        }

        public override bool IsRunning()
        {
            return _server != null && _server.IsRunning;
        }

        public override Dictionary<ulong, ONI.Player> GetPlayers()
        {
            return ConnectedPlayers;
        }
    }
}
