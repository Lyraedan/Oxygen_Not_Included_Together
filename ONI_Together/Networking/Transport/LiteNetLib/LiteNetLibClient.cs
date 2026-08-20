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

        public static NetManager Client => _client;
        public static NetPeer ServerPeer => _serverPeer;
        public static ulong CLIENT_ID { get; private set; }

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

        public override void Prepare()
        {
            using var _ = Profiler.Scope();
        }

        public override void ConnectToHost(string ip, int port)
        {
            using var _ = Profiler.Scope();

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

            _client = new NetManager(_listener)
            {
                AutoRecycle = true,
                DisconnectTimeout = Configuration.Instance.Client.TimeoutSeconds * 1000,
                UnsyncedEvents = true,
                ChannelsCount = 4
            };

            _client.Start();
            DebugConsole.Log("[LiteNetLibClient] Connecting to " + ip + ":" + port + "...");
            _serverPeer = _client.Connect(ip, port, "ONI_TOGETHER");

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
                OnReturnToMenu?.Invoke("Connection Timeout", "Failed to connect to the host within the timeout period.");
            }
        }

        private void OnConnectedToServer(NetPeer peer)
        {
            using var _ = Profiler.Scope();

            _serverPeer = peer;
            CLIENT_ID = (ulong)peer.Id + 2; // Unique client ID

            OnClientConnected?.Invoke();
            MultiplayerSession.SetHost(1);
            MultiplayerSession.InActiveSession = true;
            PacketHandler.readyToProcess = true;

            var host = new MultiplayerPlayer(1) { Connection = peer };
            MultiplayerSession.ConnectedPlayers[1] = host;
            MultiplayerSession.KnownPlayerNames[CLIENT_ID] = Utils.GetLocalPlayerName();

            DebugConsole.Log("[LiteNetLibClient] Connected to host! Assigned Client ID: " + CLIENT_ID);
            OnRequestStateOrReturn?.Invoke();
        }

        private void OnDisconnectedFromServer(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            using var _ = Profiler.Scope();

            CLIENT_ID = Utils.NilUlong();
            _serverPeer = null;

            OnClientDisconnected?.Invoke();
            MultiplayerSession.ConnectedPlayers.Clear();

            DebugConsole.Log("[LiteNetLibClient] Disconnected from server. Reason: " + disconnectInfo.Reason);
            OnReturnToMenu?.Invoke("Disconnected", "Disconnected from host (" + disconnectInfo.Reason + ").");
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

            _client?.PollEvents();
            OnMessageRecieved();
            UpdateBandwidth();
        }

        public override void OnMessageRecieved()
        {
            using var _ = Profiler.Scope();

            while (_incomingPackets.TryDequeue(out var rawData))
            {
                try
                {
                    PacketHandler.HandleIncoming(rawData);
                }
                catch (Exception ex)
                {
                    DebugConsole.LogError("[LiteNetLibClient] Failed to handle incoming packet: " + ex);
                }
            }
        }

        public override int GetPing()
        {
            return _serverPeer != null ? _serverPeer.Ping : 0;
        }

        public override NetworkState GetJitterState()
        {
            int ping = GetPing();
            _pingSamples.Enqueue(ping);
            while (_pingSamples.Count > JITTER_SAMPLE_COUNT)
                _pingSamples.Dequeue();

            if (_pingSamples.Count < 2)
                return NetworkState.GOOD;

            int min = int.MaxValue;
            int max = int.MinValue;
            foreach (var p in _pingSamples)
            {
                if (p < min) min = p;
                if (p > max) max = p;
            }

            int jitter = max - min;
            if (jitter > 60) return NetworkState.BAD;
            if (jitter > 30) return NetworkState.DEGRADED;
            return NetworkState.GOOD;
        }

        public override NetworkState GetLatencyState()
        {
            int ping = GetPing();
            if (ping >= NetworkConfig.PingRanges.BAD) return NetworkState.BAD;
            if (ping >= NetworkConfig.PingRanges.DEGRADED) return NetworkState.DEGRADED;
            return NetworkState.GOOD;
        }

        public override NetworkState GetPacketlossState()
        {
            return NetworkState.GOOD;
        }

        public override NetworkState GetServerPerformanceState()
        {
            return NetworkState.GOOD;
        }

        private void UpdateBandwidth()
        {
            float now = Time.unscaledTime;
            if (now - _lastBwPollTime < 0.5f) return;
            float dt = now - _lastBwPollTime;
            _lastBwPollTime = now;

            if (_client != null)
            {
                var stats = _client.Statistics;
                long bytesIn = stats.BytesReceived;
                long bytesOut = stats.BytesSent;
                long packetsIn = stats.PacketsReceived;
                long packetsOut = stats.PacketsSent;

                _clientInBw = (bytesIn - _lastBytesIn) / dt;
                _clientOutBw = (bytesOut - _lastBytesOut) / dt;
                _clientInPps = (int)((packetsIn - _lastPacketsIn) / dt);
                _clientOutPps = (int)((packetsOut - _lastPacketsOut) / dt);

                _lastBytesIn = bytesIn;
                _lastBytesOut = bytesOut;
                _lastPacketsIn = packetsIn;
                _lastPacketsOut = packetsOut;
            }
        }
    }
}
