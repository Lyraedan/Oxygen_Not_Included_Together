using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using ONI_Together_DedicatedServer.Logging;
using ONI_Together_DedicatedServer.ONI;
using ONI_Together_DedicatedServer.Security;
using Shared.Networking.Dedicated;

namespace ONI_Together_DedicatedServer.Transports
{
    /// <summary>
    /// </summary>
    public class DedicatedLiteNetLibServer : DedicatedTransportServer
    {
        internal const ulong MasterClientId = 1;

        private static readonly TimeSpan ReservationTime = TimeSpan.FromMinutes(10);

        private const int MaxMasterMessageBytes = 1024 * 1024;
        private const int ReliableBurstFactor = 10;
        private const string DiscoveryRequestKey = "ONI_DISCOVERY_REQ";
        private const string DiscoveryResponseKey = "ONI_DISCOVERY_RESP";

        private NetManager? _server;
        private EventBasedNetListener? _listener;
        private readonly Dictionary<ulong, Player> _connectedPlayers = new();
        private readonly Dictionary<int, ulong> _clientIdByPeerId = new();

        private string _worldName = "Dedicated World";
        private int _cycle = 1;

        private RateLimiter _unreliableLimiter = new(600, 600);
        private RateLimiter _floodLimiter = new(6000, 1200);
        private volatile bool _paused;

        private NetPeer? _masterPeer;
        private ulong _masterToken;
        private bool _hostReady;
        private bool _masterFetchedColony;
        private ulong _reservedToken;
        private DateTime _reservedUntilUtc;
        private RelaySaveAssembler _upload = new(1);

        public Func<SaveFile?>? ColonyProvider { get; set; }

        public event Action<SaveFile>? ColonyReceived;

        internal bool HostReady => _hostReady;

        public string HostStatus =>
            _masterPeer == null
                ? (_reservedToken != 0 && DateTime.UtcNow < _reservedUntilUtc ? "loading the colony (slot reserved)" : "none - the next player to join hosts")
                : (_hostReady ? "ready" : "connected, loading");

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

            CreateLimiters(config.RateLimitMessagesPerSecond);

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
                if (request.Data == null || request.Data.AvailableBytes > config.MaxPacketSize)
                {
                    request.Reject();
                    return;
                }

                if (request.Data.GetString() != RelayProtocol.ConnectionKey)
                {
                    request.Reject();
                    return;
                }

                if (!RelayProtocol.Hello.TryParse(request.Data.GetRemainingBytes(), out var hello)
                    || hello.ProtocolVersion != RelayProtocol.Version)
                {
                    ServerLogger.Current.Warn($"[DedicatedServer] Connection rejected: {request.RemoteEndPoint} sent no matching relay hello (mod version too old or too new).");
                    request.Reject();
                    return;
                }

                var verifier = new DeclaredDlcOwnershipVerifier(new DlcPolicy
                {
                    Mode = config.RequireDlcDeclaration && config.DlcCheckMode == DlcCheckMode.Off
                        ? DlcCheckMode.Required
                        : config.DlcCheckMode,
                    RequiredDlcIds = config.RequiredDlcIds,
                    ForbiddenDlcIds = config.ForbiddenDlcIds,
                    ServerActiveDlcIds = config.ServerActiveDlcIds
                });

                var dlcResult = verifier.Verify(new ClientDlcInfo
                {
                    Declared = true,
                    ActiveDlcIds = hello.ActiveDlcIds,
                    OwnedDlcIds = hello.OwnedDlcIds
                });
                if (!dlcResult.Allowed)
                {
                    ServerLogger.Current.Warn($"[DedicatedServer] Connection rejected (DLC): {dlcResult.Reason}");
                    request.Reject();
                    return;
                }

                bool becomesMaster = _masterPeer == null;
                if (becomesMaster && IsReservedForSomeoneElse(hello.InstanceToken))
                {
                    ServerLogger.Current.Info("[DedicatedServer] Connection rejected: the host is still loading the colony.");
                    request.Reject();
                    return;
                }

                if (!becomesMaster && !_hostReady)
                {
                    ServerLogger.Current.Info("[DedicatedServer] Connection rejected: the host has not loaded the colony yet.");
                    request.Reject();
                    return;
                }

                var peer = request.Accept();
                if (peer == null)
                    return;

                if (becomesMaster)
                {
                    _masterPeer = peer;
                    _masterToken = hello.InstanceToken;
                    _hostReady = false;
                    _masterFetchedColony = false;
                    _reservedToken = 0;
                    _upload = new RelaySaveAssembler((int)Math.Min(config.MaxSaveBytes, int.MaxValue / 2));
                }

                _clientIdByPeerId[peer.Id] = becomesMaster ? MasterClientId : (ulong)peer.Id + 2;
                return;
            }
            catch (Exception ex)
            {
                ServerLogger.Current.Warn($"[DedicatedServer] Invalid connection request: {ex.Message}");
            }

            request.Reject();
        }

        private bool IsReservedForSomeoneElse(ulong token)
        {
            return _reservedToken != 0 && _reservedToken != token && DateTime.UtcNow < _reservedUntilUtc;
        }

        private void OnPeerConnected(NetPeer peer)
        {
            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
            {
                peer.Disconnect();
                return;
            }

            bool isMaster = clientId == MasterClientId;
            _connectedPlayers[clientId] = new Player(peer, isMaster, clientId);
            // Resume immediately on join so packets in the same PollEvents aren't dropped.
            _paused = false;

            ServerLogger.Current.Info($"[DedicatedServer] Player joined: ClientID={clientId}, Master={isMaster}, Endpoint={peer.Address}:{peer.Port}");

            if (!isMaster)
            {
                SendToMaster(RelayProtocol.PeerEvent(RelayFrameType.PeerJoined, clientId));
                return;
            }

            var colony = ColonyProvider?.Invoke();
            SendToMaster(RelayProtocol.MasterAssigned(
                colony != null,
                colony == null ? string.Empty : Path.GetFileNameWithoutExtension(colony.Name),
                colony?.Data.Length ?? 0));

            if (colony == null)
                ServerLogger.Current.Warn("[DedicatedServer] The server holds no colony. Start it once with --save <file.sav>.");
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong clientId))
                return;
            if (_connectedPlayers.TryGetValue(clientId, out var known) && known.Connection != peer)
                return;

            _clientIdByPeerId.Remove(peer.Id);
            _unreliableLimiter.Reset(peer.Id.ToString());
            _floodLimiter.Reset(peer.Id.ToString());
            _connectedPlayers.Remove(clientId);

            ServerLogger.Current.Info($"[DedicatedServer] Player left: ClientID={clientId}, Reason={disconnectInfo.Reason}");

            if (clientId != MasterClientId)
            {
                SendToMaster(RelayProtocol.PeerEvent(RelayFrameType.PeerLeft, clientId));
                return;
            }

            ReleaseMaster();
        }

        private void ReleaseMaster()
        {
            if (!_hostReady && _masterFetchedColony)
            {
                _reservedToken = _masterToken;
                _reservedUntilUtc = DateTime.UtcNow + ReservationTime;
                ServerLogger.Current.Info("[DedicatedServer] Host is loading the colony; its slot is reserved.");
            }

            _masterPeer = null;
            _hostReady = false;
            _upload.Reset();

            foreach (var player in _connectedPlayers.Values.ToList())
            {
                _connectedPlayers.Remove(player.ClientID);
                _clientIdByPeerId.Remove(player.Connection.Id);
                player.Connection.Disconnect();
            }
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (!_clientIdByPeerId.TryGetValue(peer.Id, out ulong senderId) ||
                !_connectedPlayers.ContainsKey(senderId) ||
                !RelayAllowed)
            {
                return;
            }

            var config = ServerConfiguration.Instance.Config;
            bool fromMaster = senderId == MasterClientId;

            byte[] rawData = reader.GetRemainingBytes();
            if (rawData.Length == 0)
                return;

            int sizeCap = fromMaster
                ? MaxMasterMessageBytes
                : Math.Min(config.MaxPacketSize, config.MaxMessageSize);
            if (rawData.Length > sizeCap)
            {
                ServerLogger.Current.Warn($"[DedicatedServer] Disconnecting {senderId}: oversized packet ({rawData.Length}B).");
                peer.Disconnect();
                return;
            }

            if (config.VerbosePacketLogging)
                ServerLogger.Current.Info($"[DedicatedServer] Packet from {senderId}: Size={rawData.Length}B, Channel={channelNumber}, Method={deliveryMethod}");

            if (fromMaster)
            {
                HandleMasterMessage(rawData, channelNumber, deliveryMethod);
                return;
            }

            if (deliveryMethod == DeliveryMethod.Unreliable)
            {
                if (!_unreliableLimiter.TryAcquire(peer.Id.ToString()))
                    return;
            }
            else if (!_floodLimiter.TryAcquire(peer.Id.ToString()))
            {
                ServerLogger.Current.Warn($"[DedicatedServer] Disconnecting {senderId}: reliable packet flood.");
                peer.Disconnect();
                return;
            }

            if (_masterPeer == null || !_hostReady)
                return;

            Forward(_masterPeer, RelayProtocol.PeerData(RelayFrameType.FromPeer, senderId, rawData), channelNumber, deliveryMethod);
        }

        private void HandleMasterMessage(byte[] data, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (!RelayProtocol.IsFrame(data))
                return;

            switch (RelayProtocol.TypeOf(data))
            {
                case RelayFrameType.ToPeer:
                    if (RelayProtocol.TryReadPeerData(data, out ulong target, out byte[] payload)
                        && target != MasterClientId
                        && _connectedPlayers.TryGetValue(target, out var player))
                    {
                        Forward(player.Connection, payload, channelNumber, deliveryMethod);
                    }
                    break;

                case RelayFrameType.KickPeer:
                    if (data.Length >= RelayProtocol.PeerHeaderSize)
                    {
                        ulong kicked = BitConverter.ToUInt64(data, RelayProtocol.HeaderSize);
                        if (kicked != MasterClientId && _connectedPlayers.TryGetValue(kicked, out var victim))
                            victim.Connection.Disconnect();
                    }
                    break;

                case RelayFrameType.HostReady:
                    _hostReady = true;
                    _reservedToken = 0;
                    ServerLogger.Current.Info("[DedicatedServer] Host is ready; players can join.");
                    break;

                case RelayFrameType.SaveRequest:
                    SendColonyToMaster();
                    break;

                case RelayFrameType.SaveBegin:
                case RelayFrameType.SaveData:
                    if (_upload.Accept(data, out string name, out byte[] save))
                    {
                        ServerLogger.Current.Info($"[DedicatedServer] Received colony '{name}' from the host ({save.Length} bytes).");
                        SetWorldInfo(name);
                        ColonyReceived?.Invoke(new SaveFile(name + ".sav", save));
                    }
                    break;

                default:
                    ServerLogger.Current.Warn($"[DedicatedServer] Unknown frame from the host: {RelayProtocol.TypeOf(data)}");
                    break;
            }
        }

        private void SendColonyToMaster()
        {
            var colony = ColonyProvider?.Invoke();
            if (colony == null || _masterPeer == null)
                return;

            _masterFetchedColony = true;
            ServerLogger.Current.Info($"[DedicatedServer] Sending colony '{colony.Name}' to the host.");

            string name = Path.GetFileNameWithoutExtension(colony.Name);
            foreach (byte[] frame in RelayProtocol.SaveFrames(name, colony.Data))
                _masterPeer.Send(frame, RelayProtocol.SaveChannel, DeliveryMethod.ReliableOrdered);
        }

        private void SendToMaster(byte[] frame)
        {
            if (_masterPeer != null)
                Forward(_masterPeer, frame, 0, DeliveryMethod.ReliableOrdered);
        }

        private static void Forward(NetPeer to, byte[] data, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (to.ConnectionState != ConnectionState.Connected)
                return;

            if (deliveryMethod == DeliveryMethod.Unreliable && data.Length > to.GetMaxSinglePacketSize(DeliveryMethod.Unreliable))
                deliveryMethod = DeliveryMethod.ReliableUnordered;

            to.Send(data, channelNumber, deliveryMethod);
        }

        private void CreateLimiters(int perSecond)
        {
            int rate = Math.Max(1, perSecond);
            _unreliableLimiter = new RateLimiter(capacity: rate, refillPerSecond: rate);
            _floodLimiter = new RateLimiter(capacity: rate * ReliableBurstFactor, refillPerSecond: rate * 2);
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
            _masterPeer = null;
            _hostReady = false;
            _reservedToken = 0;
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
            CreateLimiters(config.RateLimitMessagesPerSecond);
            // Ip/Port/Transport require Stop+Start (not preserving players) — not applied here.
        }

        public override Dictionary<ulong, Player> GetPlayers()
        {
            return _connectedPlayers;
        }
    }
}
