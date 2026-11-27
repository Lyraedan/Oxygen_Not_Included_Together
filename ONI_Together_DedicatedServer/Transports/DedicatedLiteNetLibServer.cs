using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using ONI_Together_DedicatedServer.Logging;
using ONI_Together_DedicatedServer.ONI;
using ONI_Together_DedicatedServer.Security;

namespace ONI_Together_DedicatedServer.Transports
{
    /// <summary>
    /// LiteNetLib-based relay server aligned with ONI_Together's 0.8.1 LiteNetLib client/server transport.
    /// Security: handshake key, lobby cap, packet-size caps, per-peer rate limiting, optional DLC checks.
    /// </summary>
    public class DedicatedLiteNetLibServer : DedicatedTransportServer
    {
        private const string HandshakeKey = "ONI_TOGETHER";
        private const string DiscoveryRequestKey = "ONI_DISCOVERY_REQ";
        private const string DiscoveryResponseKey = "ONI_DISCOVERY_RESP";

        private NetManager? _server;
        private EventBasedNetListener? _listener;
        private readonly Dictionary<ulong, Player> _connectedPlayers = new();
        private readonly Dictionary<int, ulong> _clientIdByPeerId = new();

        private string _worldName = "Dedicated World";
        private int _cycle = 1;

        private RateLimiter _rateLimiter = new(600, 600);
        private int _messagesThisPoll;
        private volatile bool _paused;

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
            config.Sanitize();

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
                BroadcastReceiveEnabled = config.DiscoveryEnabled,
                UnconnectedMessagesEnabled = config.DiscoveryEnabled,
                EnableStatistics = true
            };

            _rateLimiter = new RateLimiter(
                capacity: Math.Max(1, config.RateLimitMessagesPerSecond),
                refillPerSecond: config.RateLimitMessagesPerSecond);

            bool started = StartBoundSocket(_server, ip, port);
            if (!started)
            {
                ServerLogger.Current.Warn($"[DedicatedServer] Failed to start LiteNetLib server on {ip}:{port}");
                _server = null;
                _listener = null;
                return false;
            }

            ServerLogger.Current.Info($"[DedicatedServer] LiteNetLib server listening on {ip}:{port} (MaxPlayers={config.MaxLobbySize}, Timeout={config.TimeoutSeconds}s, MaxPacketSize={config.MaxPacketSize}, RateLimit={config.RateLimitMessagesPerSecond}/s)");
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
                // Reject absurdly large connection payloads.
                if (request.Data == null || request.Data.AvailableBytes < 0 || request.Data.AvailableBytes > config.MaxPacketSize)
                {
                    request.Reject();
                    return;
                }

                string key = request.Data.GetString();
                if (key != HandshakeKey)
                {
                    request.Reject();
                    return;
                }

                ulong clientNetId = 0;
                if (request.Data.AvailableBytes >= sizeof(ulong))
                    clientNetId = request.Data.GetULong();

                // Optional DLC declaration appended after key + clientNetId (backwards compatible).
                var dlc = TryReadDlcInfo(request.Data);

                var verifier = new DeclaredDlcOwnershipVerifier(new DlcPolicy
                {
                    Mode = config.RequireDlcDeclaration && config.DlcCheckMode == DlcCheckMode.Off
                        ? DlcCheckMode.Required
                        : config.DlcCheckMode,
                    RequiredDlcIds = config.RequiredDlcIds,
                    ForbiddenDlcIds = config.ForbiddenDlcIds,
                    ServerActiveDlcIds = config.ServerActiveDlcIds
                });

                var dlcResult = verifier.Verify(dlc);
                if (!dlcResult.Allowed)
                {
                    ServerLogger.Current.Warn($"[DedicatedServer] Connection rejected (DLC): {dlcResult.Reason}");
                    request.Reject();
                    return;
                }

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
            catch (Exception ex)
            {
                ServerLogger.Current.Warn($"[DedicatedServer] Invalid connection request: {ex.Message}");
            }

            request.Reject();
        }

        private static ClientDlcInfo TryReadDlcInfo(NetDataReader data)
        {
            var info = new ClientDlcInfo();
            try
            {
                if (data == null || data.AvailableBytes < sizeof(int))
                    return info;

                info.Declared = true;
                int activeCount = data.GetInt();
                if (activeCount < 0 || activeCount > 4096) { info.Declared = false; return info; }
                var active = new List<string>(activeCount);
                for (int i = 0; i < activeCount; i++) active.Add(SafeGetString(data));
                int ownedCount = data.AvailableBytes >= sizeof(int) ? data.GetInt() : 0;
                if (ownedCount < 0 || ownedCount > 4096) { info.Declared = false; return info; }
                var owned = new List<string>(ownedCount);
                for (int i = 0; i < ownedCount; i++) owned.Add(SafeGetString(data));
                info.ActiveDlcIds = active;
                info.OwnedDlcIds = owned;
            }
            catch
            {
                // Malformed optional DLC block -> treat as undeclared.
                info = new ClientDlcInfo();
            }
            return info;
        }

        private static string SafeGetString(NetDataReader data)
        {
            try
            {
                var s = data.GetString();
                if (s == null) return "";
                return s.Length > 128 ? s.Substring(0, 128) : s;
            }
            catch { return ""; }
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
            var player = new Player(peer, isMaster, clientId);
            _connectedPlayers[clientId] = player;
            // Resume immediately on join so packets in the same PollEvents aren't dropped.
            _paused = false;

            ServerLogger.Current.Info($"[DedicatedServer] Player joined: ClientID={clientId}, Master={isMaster}, Endpoint={peer.Address}:{peer.Port}");
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
                return;

            _clientIdByPeerId.Remove(peer.Id);
            _rateLimiter.Reset(peer.Id.ToString());

            bool wasMaster = false;
            if (_connectedPlayers.TryGetValue(clientId, out var player))
            {
                wasMaster = player.IsMaster;
                _connectedPlayers.Remove(clientId);
            }

            ServerLogger.Current.Info($"[DedicatedServer] Player left: ClientID={clientId}, Reason={disconnectInfo.Reason}");

            if (!wasMaster)
                return;

            // NOTE: In current relay mode this does NOT transfer simulation state (see TODO.md final milestone).
            ServerLogger.Current.Info("[DedicatedServer] Master disconnected! Attempting master migration...");
            if (_connectedPlayers.Count > 0)
            {
                var newMaster = _connectedPlayers.Values
                    .Where(p => p.Connection.ConnectionState == ConnectionState.Connected)
                    .OrderBy(p => p.Connection.Ping)
                    .FirstOrDefault();

                if (newMaster != null)
                {
                    newMaster.UpdateMasterState(true);
                    ServerLogger.Current.Info($"[DedicatedServer] New master assigned: ClientID={newMaster.ClientID} (Ping={newMaster.PingMs}ms)");
                }
            }
            else
            {
                ServerLogger.Current.Info("[DedicatedServer] No clients remaining. Server idle.");
            }
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong senderId) ||
                !_connectedPlayers.TryGetValue(senderId, out var sender))
            {
                return;
            }

            var config = ServerConfiguration.Instance.Config;

            // PauseWhenEmpty: no packet processing while empty; connections still accepted. Resume on join.
            // Do NOT drop packets once a player is connected (e.g. GameStateRequestPacket in same PollEvents as join).
            if (_connectedPlayers.Count == 0)
                return;
            if (ComputePaused(config.PauseWhenEmpty, _connectedPlayers.Count))
                return;

            byte[] rawData = reader.GetRemainingBytes();
            if (rawData.Length == 0)
                return;

            // Packet size cap.
            if (rawData.Length > config.MaxPacketSize || rawData.Length > config.MaxMessageSize)
            {
                ServerLogger.Current.Warn($"[DedicatedServer] Dropping oversized packet from {senderId}: {rawData.Length}B");
                return;
            }

            // Per-peer rate limiting.
            if (!_rateLimiter.TryAcquire(peer.Id.ToString()))
                return;

            // MaxMessagesPerPoll (global per PollEvents).
            if (_messagesThisPoll >= Math.Max(1, config.MaxMessagesPerPoll))
                return;
            _messagesThisPoll++;

            if (config.VerbosePacketLogging)
            {
                int packetType = rawData.Length >= 4 ? BitConverter.ToInt32(rawData, 0) : 0;
                ServerLogger.Current.Info($"[DedicatedServer] Packet from {senderId} (Master={sender.IsMaster}): Type={packetType}, Size={rawData.Length}B, Channel={channelNumber}, Method={deliveryMethod}");
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
            if (_server == null) return;
            var config = ServerConfiguration.Instance.Config;
            if (!config.DiscoveryEnabled) return;

            try
            {
                if (reader.AvailableBytes > config.MaxPacketSize) return;
                string req = reader.GetString();
                if (req == DiscoveryRequestKey)
                {
                    // Optional discovery token.
                    if (!string.IsNullOrEmpty(config.DiscoveryToken))
                    {
                        if (reader.AvailableBytes <= 0) return;
                        string token = reader.GetString();
                        if (token != config.DiscoveryToken) return;
                    }

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
            ServerLogger.Current.Warn($"[DedicatedServer] Network error from {endPoint}: {socketError}");
        }

        public override void Update()
        {
            _messagesThisPoll = 0;
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
            ServerLogger.Current.Info("[DedicatedServer] LiteNetLib server stopped.");
        }

        public override bool IsRunning()
        {
            return _server != null && _server.IsRunning;
        }

        public override bool IsPaused => _paused;

        /// <summary>Packet processing disabled while paused. Internal for tests.</summary>
        internal bool RelayAllowed => !_paused;

        /// <summary>Pause packet processing while empty. Still poll events so a join can resume.</summary>
        public override void SetPaused(bool paused) => _paused = paused;

        /// <summary>PauseWhenEmpty && no players => paused. Internal for tests.</summary>
        internal static bool ComputePaused(bool pauseWhenEmpty, int playerCount) => pauseWhenEmpty && playerCount <= 0;

        /// <summary>Re-read live config without restarting socket or dropping players.</summary>
        public override void ReloadConfiguration()
        {
            if (_server == null) return;
            var config = ServerConfiguration.Instance.Config;
            config.Sanitize();

            int timeoutMs = Math.Max(5, config.TimeoutSeconds) * 1000;
            _server.DisconnectTimeout = timeoutMs;
            _server.BroadcastReceiveEnabled = config.DiscoveryEnabled;
            _server.UnconnectedMessagesEnabled = config.DiscoveryEnabled;
            _rateLimiter = new RateLimiter(
                capacity: Math.Max(1, config.RateLimitMessagesPerSecond),
                refillPerSecond: config.RateLimitMessagesPerSecond);
            // MaxLobbySize, MaxPacketSize, MaxMessageSize, MaxMessagesPerPoll read per-event.
            // Ip/Port/Transport require Stop+Start (not preserving players) — not applied here.
        }

        public override Dictionary<ulong, Player> GetPlayers()
        {
            return _connectedPlayers;
        }
    }
}
