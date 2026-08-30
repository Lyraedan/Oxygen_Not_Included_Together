using LiteNetLib;
using LiteNetLib.Utils;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.States;
using Shared;
using Shared.Profiling;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using static ONI_Together.Menus.NetworkIndicatorsScreen;

namespace ONI_Together.Networking.Transport.Lan
{
    public class LiteNetLibClient : TransportClient
    {
        private static NetManager _client;
        private static EventBasedNetListener _listener;
        private static NetPeer _serverPeer;

        // LAN Discovery
        private static NetManager _discoveryClient;
        private static EventBasedNetListener _discoveryListener;
        public static readonly Dictionary<string, LanDiscoveredHost> DiscoveredHosts = new Dictionary<string, LanDiscoveredHost>();
        public static event System.Action OnDiscoveredHostsChanged;

        public static NetManager Client => _client;
        public static NetPeer ServerPeer => _serverPeer;
        public static ulong CLIENT_ID { get; private set; }

        public bool IsConnected => _serverPeer != null && _serverPeer.ConnectionState == ConnectionState.Connected;
        public static int MaxServerCapacity { get; internal set; } = 16;
        public bool IsLoadingReconnect { get; set; }

        private static readonly ConcurrentQueue<byte[]> _incomingPackets = new ConcurrentQueue<byte[]>();

        // Network health
        private const int JITTER_SAMPLE_COUNT = 20;
        private readonly Queue<int> _pingSamples = new Queue<int>();

        // Bandwidth tracking
        private long _lastBytesIn, _lastBytesOut;
        private long _lastPacketsIn, _lastPacketsOut;
        private float _clientInBw, _clientOutBw;
        private int _clientInPps, _clientOutPps;
        private float _lastBwPollTime;

        public override float IncomingBandwidth => _clientInBw;
        public override float OutgoingBandwidth => _clientOutBw;
        public override int IncomingPps => _clientInPps;
        public override int OutgoingPps => _clientOutPps;

        public static void ResetLocalId()
        {
            CLIENT_ID = 0;
        }

        public static void StartLanDiscovery(int targetPort = 8080)
        {
            if (_discoveryClient == null)
            {
                _discoveryListener = new EventBasedNetListener();
                _discoveryListener.NetworkReceiveUnconnectedEvent += OnDiscoveryResponseReceived;
                _discoveryClient = new NetManager(_discoveryListener)
                {
                    UnsyncedEvents = false,
                    BroadcastReceiveEnabled = true
                };
                _discoveryClient.Start();
            }

            var writer = new NetDataWriter();
            writer.Put("ONI_DISCOVERY_REQ");
            _discoveryClient.SendBroadcast(writer, targetPort);
            if (targetPort != 7777)
            {
                _discoveryClient.SendBroadcast(writer, 7777);
            }
        }

        public static void PollDiscovery()
        {
            if (_discoveryClient != null && _discoveryClient.IsRunning)
            {
                _discoveryClient.PollEvents();
            }
        }

        public static void StopLanDiscovery()
        {
            if (_discoveryClient != null)
            {
                _discoveryClient.Stop();
                _discoveryClient = null;
                _discoveryListener = null;
            }
        }

        private static void OnDiscoveryResponseReceived(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            try
            {
                string resp = reader.GetString();
                if (resp == "ONI_DISCOVERY_RESP")
                {
                    var host = new LanDiscoveredHost
                    {
                        EndPoint = remoteEndPoint,
                        HostName = reader.GetString(),
                        WorldName = reader.GetString(),
                        Cycle = reader.GetInt(),
                        PlayerCount = reader.GetInt(),
                        MaxPlayers = reader.GetInt(),
                        Port = reader.GetInt(),
                        LastSeenTime = Time.realtimeSinceStartup
                    };

                    string key = remoteEndPoint.Address.ToString() + ":" + host.Port;
                    DiscoveredHosts[key] = host;
                    OnDiscoveredHostsChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.LogWarning("[LiteNetLibClient] Error parsing discovery response: " + ex.Message);
            }
        }

        public override void Prepare()
        {
            using var _ = Profiler.Scope();
        }

        public override void ConnectToHost(string ip, int port)
        {
            using var _ = Profiler.Scope();

            StopLanDiscovery();

            if (_client != null && _client.IsRunning)
            {
                if (_serverPeer != null && _serverPeer.ConnectionState == ConnectionState.Connected)
                    return;
            }

            MultiplayerSession.ServerIp = ip;
            MultiplayerSession.ServerPort = port;

            _listener = new EventBasedNetListener();
            _listener.PeerConnectedEvent += OnConnectedToServer;
            _listener.PeerDisconnectedEvent += OnDisconnectedFromServer;
            _listener.NetworkReceiveEvent += OnNetworkReceive;
            _listener.NetworkErrorEvent += OnNetworkError;

            // UnsyncedEvents = false ensures ALL events execute on Main Thread in PollEvents()
            _client = new NetManager(_listener)
            {
                AutoRecycle = true,
                DisconnectTimeout = Configuration.Instance.Client.TimeoutSeconds * 1000,
                UnsyncedEvents = false,
                ChannelsCount = 4,
                EnableStatistics = true
            };

            _client.Start();
            DebugConsole.Log("[LiteNetLibClient] Connecting to " + ip + ":" + port + "...");

            var writer = new NetDataWriter();
            writer.Put("ONI_TOGETHER");
            _serverPeer = _client.Connect(ip, port, writer);

            int timeout = Configuration.Instance.Client.TimeoutSeconds;
            CoroutineRunner.RunOne(WaitForConnectionSuccess(timeout));
        }

        private IEnumerator WaitForConnectionSuccess(int timeoutSeconds)
        {
            float elapsed = 0f;
            while (elapsed < timeoutSeconds)
            {
                if (_serverPeer != null && _serverPeer.ConnectionState == ConnectionState.Connected)
                    yield break;

                yield return new WaitForSecondsRealtime(0.5f);
                elapsed += 0.5f;
            }

            if (_serverPeer == null || _serverPeer.ConnectionState != ConnectionState.Connected)
            {
                DebugConsole.LogError("[LiteNetLibClient] Connection timed out.");
                Disconnect();
                OnReturnToMenu?.Invoke(
                    STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.CONNECTION_FAILED,
                    STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.CONNECTION_FAILED_DESC
                );
            }
        }

        private void OnConnectedToServer(NetPeer peer)
        {
            using var _ = Profiler.Scope();

            _serverPeer = peer;
            CLIENT_ID = (ulong)peer.RemoteId + 2;

            OnClientConnected?.Invoke();
            MultiplayerSession.SetHost(1);
            MultiplayerSession.InActiveSession = true;
            PacketHandler.readyToProcess = true;

            var host = new MultiplayerPlayer(1) { Connection = peer };
            MultiplayerSession.ConnectedPlayers[1] = host;
            MultiplayerSession.KnownPlayerNames[CLIENT_ID] = Utils.GetLocalPlayerName();

            DebugConsole.Log("[LiteNetLibClient] Connected to host! Assigned Client ID: " + CLIENT_ID);
            Game.Instance?.Trigger(MP_HASHES.OnConnected);
            OnRequestStateOrReturn?.Invoke();
        }

        private void OnDisconnectedFromServer(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            using var _ = Profiler.Scope();

            _serverPeer = null;

            OnClientDisconnected?.Invoke();
            MultiplayerSession.ConnectedPlayers.Clear();

            var (reason, message) = GetDisconnectInfo(disconnectInfo);
            DebugConsole.Log($"[LiteNetLibClient] Disconnected from server. Reason: {disconnectInfo.Reason} ({reason})");
            OnReturnToMenu?.Invoke(reason, message);
        }

        private (string reason, string message) GetDisconnectInfo(DisconnectInfo info)
        {
            switch (info.Reason)
            {
                case DisconnectReason.Timeout:
                    return (
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.CONNECTION_TIMED_OUT,
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.CONNECTION_TIMED_OUT_DESC
                    );

                case DisconnectReason.RemoteConnectionClose:
                case DisconnectReason.DisconnectPeerCalled:
                    return (
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.HOST_DISCONNECTED,
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.HOST_DISCONNECTED_DESC
                    );

                case DisconnectReason.ConnectionRejected:
                    return (
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.CONNECTION_REJECTED,
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.CONNECTION_REJECTED_DESC
                    );

                case DisconnectReason.ConnectionFailed:
                case DisconnectReason.HostUnreachable:
                case DisconnectReason.NetworkUnreachable:
                case DisconnectReason.UnknownHost:
                    return (
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.CONNECTION_FAILED,
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.CONNECTION_FAILED_DESC
                    );

                default:
                    return (
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.UNKNOWN,
                        STRINGS.UI.MP_OVERLAY.CLIENT.LITENETLIB.UNKNOWN_DESC
                    );
            }
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            byte[] rawData = reader.GetRemainingBytes();
            _incomingPackets.Enqueue(rawData);
        }

        private void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            DebugConsole.LogWarning("[LiteNetLibClient] Network error: " + socketError);
        }

        public override void Disconnect()
        {
            using var _ = Profiler.Scope();

            _serverPeer?.Disconnect();
            _client?.Stop();
            _serverPeer = null;
            _client = null;

            while (_incomingPackets.TryDequeue(out var _)) { }
        }

        public override void ReconnectToSession()
        {
            if (!string.IsNullOrEmpty(MultiplayerSession.ServerIp) && MultiplayerSession.ServerPort > 0)
            {
                ConnectToHost(MultiplayerSession.ServerIp, MultiplayerSession.ServerPort);
            }
        }

        public override void Update()
        {
            using var _ = Profiler.Scope();

            PollDiscovery();

            if (_client == null)
                return;

            _client.PollEvents();
            OnMessageRecieved();
            UpdateMetrics();
        }

        public override void OnMessageRecieved()
        {
            using var _ = Profiler.Scope();

            while (_incomingPackets.TryDequeue(out var data))
            {
                try
                {
                    PacketHandler.HandleIncoming(data);
                }
                catch (Exception ex)
                {
                    DebugConsole.LogError("[LiteNetLibClient] Exception while handling packet from host: " + ex);
                }
            }
        }

        public override int GetPing()
        {
            if (_serverPeer == null) return -1;
            return _serverPeer.Ping * 2; // RTT
        }

        public override NetworkIndicatorsScreen.NetworkState GetJitterState()
        {
            int currentPing = GetPing();
            if (currentPing < 0) return NetworkState.BAD;

            _pingSamples.Enqueue(currentPing);
            if (_pingSamples.Count > JITTER_SAMPLE_COUNT)
                _pingSamples.Dequeue();

            if (_pingSamples.Count < 5) return NetworkState.GOOD;

            int min = int.MaxValue, max = int.MinValue;
            foreach (var p in _pingSamples)
            {
                if (p < min) min = p;
                if (p > max) max = p;
            }

            int jitter = max - min;
            if (jitter > 80) return NetworkState.BAD;
            if (jitter > 40) return NetworkState.DEGRADED;
            return NetworkState.GOOD;
        }

        public override NetworkIndicatorsScreen.NetworkState GetLatencyState()
        {
            int ping = GetPing();
            if (ping < 0) return NetworkState.BAD;
            if (ping > NetworkConfig.PingRanges.BAD) return NetworkState.BAD;
            if (ping > NetworkConfig.PingRanges.DEGRADED) return NetworkState.DEGRADED;
            return NetworkState.GOOD;
        }

        public override NetworkIndicatorsScreen.NetworkState GetPacketlossState()
        {
            if (_client == null || _serverPeer == null) return NetworkState.BAD;
            return NetworkState.GOOD;
        }

        public override NetworkIndicatorsScreen.NetworkState GetServerPerformanceState()
        {
            return NetworkState.GOOD;
        }

        private void UpdateMetrics()
        {
            if (_client == null)
                return;

            float now = Time.realtimeSinceStartup;
            float dt = now - _lastBwPollTime;
            if (dt < 1f)
                return;

            long totalBytesIn = _client.Statistics.BytesReceived;
            long totalBytesOut = _client.Statistics.BytesSent;
            long totalPacketsIn = _client.Statistics.PacketsReceived;
            long totalPacketsOut = _client.Statistics.PacketsSent;

            _clientInBw = (totalBytesIn - _lastBytesIn) / dt;
            _clientOutBw = (totalBytesOut - _lastBytesOut) / dt;
            _clientInPps = (int)((totalPacketsIn - _lastPacketsIn) / dt);
            _clientOutPps = (int)((totalPacketsOut - _lastPacketsOut) / dt);

            _lastBytesIn = totalBytesIn;
            _lastBytesOut = totalBytesOut;
            _lastPacketsIn = totalPacketsIn;
            _lastPacketsOut = totalPacketsOut;
            _lastBwPollTime = now;
        }
    }
}
