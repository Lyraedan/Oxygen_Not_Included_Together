using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Handshake;
using Shared.Profiling;
using ONI_Together.Networking.States;
using Shared;
using Steamworks;
using UnityEngine;
using static ONI_Together.Menus.NetworkIndicatorsScreen;

namespace ONI_Together.Networking.Transport.Steam
{
    public class SteamworksClient : TransportClient
    {
        private static Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChangedCallback;
        public static HSteamNetConnection? Connection { get; private set; }

        public override ulong LocalPlayerId => SteamUser.GetSteamID().m_SteamID;
        public override ulong HostPlayerId => MultiplayerSession.HostUserID;
        public override bool IsConnected => Connection.HasValue;

        private static SteamNetConnectionRealTimeStatus_t? connectionHealth = null;

        // Network health
        private const int JITTER_SAMPLE_COUNT = 20;
        private static readonly Queue<int> _pingSamples = new();
        private static IntPtr[] _receiveBuffer = new IntPtr[256];
        private static float _lastHealthEvalTime = -1f;
        private const float HEALTH_EVAL_INTERVAL = 0.25f;
        private const int MAX_MESSAGES_PER_FRAME = 1024;
        private const double MAX_POLL_MS_PER_FRAME = 12.0;
        private const int STEAM_SEND_BUFFER_BYTES = 4 * 1024 * 1024;
        private const int STEAM_SEND_RATE_MIN = 512 * 1024;
        private const int STEAM_SEND_RATE_MAX = 4 * 1024 * 1024;

        public override void Prepare()
        {
            using var _ = Profiler.Scope();

            if (_connectionStatusChangedCallback == null)
            {
                _connectionStatusChangedCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
                DebugConsole.Log("[GameClient] Registered connection status callback.");
            }
        }

        // Connects to the host using Steam P2P networking. IP and port are ignored since Steamworks uses host steam id to connect.
        public override void ConnectToHost(string ip, int port)
        {
            using var _ = Profiler.Scope();

            ulong hostSteamId = MultiplayerSession.HostUserID;
            DebugConsole.Log($"[GameClient] Attempting ConnectP2P to host {hostSteamId}...");

            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID64(hostSteamId);

            var options = new SteamNetworkingConfigValue_t[5];
            options[0].m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutInitial;
            options[0].m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32;
            options[0].m_val.m_int32 = Configuration.Instance.Client.TimeoutSeconds * 1000;

            options[1].m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_TimeoutConnected;
            options[1].m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32;
            options[1].m_val.m_int32 = Configuration.Instance.Client.TimeoutSeconds * 1000;

            options[2].m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize;
            options[2].m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32;
            options[2].m_val.m_int32 = STEAM_SEND_BUFFER_BYTES;

            options[3].m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin;
            options[3].m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32;
            options[3].m_val.m_int32 = STEAM_SEND_RATE_MIN;

            options[4].m_eValue = ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax;
            options[4].m_eDataType = ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32;
            options[4].m_val.m_int32 = STEAM_SEND_RATE_MAX;

            Connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, options.Length, options);
            DebugConsole.Log($"[GameClient] ConnectP2P returned handle: {Connection.Value.m_HSteamNetConnection}");
        }

        public override void Disconnect()
        {
            using var _ = Profiler.Scope();

            if (Connection.HasValue)
            {
                DebugConsole.Log("[GameClient] Disconnecting from host...");

                bool result = SteamNetworkingSockets.CloseConnection(
                        Connection.Value,
                        0,
                        "Client disconnecting",
                        false
                );

                DebugConsole.Log($"[GameClient] CloseConnection result: {result}");
                Connection = null;

                MultiplayerSession.InActiveSession = false;
                //SaveHelper.CaptureWorldSnapshot();
            }
            else
            {
                DebugConsole.LogWarning("[GameClient] Disconnect called, but no connection exists.");
            }
        }

        public override void ReconnectToSession()
        {
            using var _ = Profiler.Scope();

            // If already connected/connecting, disconnect first to avoid duplicate P2P handles
            if (Connection.HasValue || GameClient.State == ClientState.Connected || GameClient.State == ClientState.Connecting)
            {
                DebugConsole.Log("[GameClient] Reconnecting: First disconnecting existing connection.");
                Disconnect();
                // Note: previous Thread.Sleep(100) removed - blocks main thread; SteamNetworkingSockets needs 1 frame via RunCallbacks instead
            }

            if (MultiplayerSession.HostUserID != Utils.NilUlong())
            {
                DebugConsole.Log("[GameClient] Attempting to reconnect to host...");
                //ConnectToHost(MultiplayerSession.HostSteamID);
                ConnectToHost(string.Empty, 7777);
            }
            else
            {
                DebugConsole.LogWarning("[GameClient] Cannot reconnect: HostSteamID is not set.");
            }
        }

        public override void Update()
        {
            using var _ = Profiler.Scope();

            SteamNetworkingSockets.RunCallbacks();
            float now = Time.unscaledTime;
            if (now - _lastHealthEvalTime >= HEALTH_EVAL_INTERVAL)
            {
                _lastHealthEvalTime = now;
                EvaluateConnectionHealth();
            }
        }

        public override void OnMessageRecieved()
        {
            using var _ = Profiler.Scope();

            if (Connection.HasValue)
                ProcessIncomingMessages(Connection.Value);
        }

        private static void ProcessIncomingMessages(HSteamNetConnection conn)
        {
            using var _ = Profiler.Scope();

            var scope = Profiler.Scope();
            int totalBytes = 0;
            int totalMessages = 0;

            int configuredBatch = Configuration.Instance?.Client?.MaxMessagesPerPoll ?? 256;
            int batchSize = Math.Max(configuredBatch, 128);
            if (_receiveBuffer.Length < batchSize)
                _receiveBuffer = new IntPtr[batchSize];

            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            double ticksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            while (totalMessages < MAX_MESSAGES_PER_FRAME)
            {
                int remaining = MAX_MESSAGES_PER_FRAME - totalMessages;
                int toReceive = Math.Min(batchSize, remaining);
                int msgCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, _receiveBuffer, toReceive);
                if (msgCount <= 0)
                    break;

                for (int i = 0; i < msgCount; i++)
                {
                    var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_receiveBuffer[i]);
                    totalBytes += msg.m_cbSize;
                    byte[] data = new byte[msg.m_cbSize];
                    Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);

                    try
                    {
                        PacketHandler.HandleIncoming(data);
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.LogWarning($"[GameClient] Failed to handle incoming packet: {ex}");
                    }

                    SteamNetworkingMessage_t.Release(_receiveBuffer[i]);
                }

                totalMessages += msgCount;

                if (msgCount < toReceive)
                    break;

                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * ticksToMs;
                if (elapsedMs >= MAX_POLL_MS_PER_FRAME)
                    break;
            }

            scope.End(totalMessages, totalBytes);
        }

        private static void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t data)
        {
            using var _ = Profiler.Scope();

            var state = data.m_info.m_eState;
            var remote = data.m_info.m_identityRemote.GetSteamID();

            DebugConsole.Log($"[GameClient] Connection status changed: {state} (remote={remote})");

            if (Connection.HasValue && data.m_hConn.m_HSteamNetConnection != Connection.Value.m_HSteamNetConnection)
                return;

            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                    OnConnected();
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    OnDisconnected("Closed by peer or problem detected locally", remote, state);
                    break;
                default:
                    break;
            }
        }

        private static void OnConnected()
        {
            using var _ = Profiler.Scope();

            //MultiplayerOverlay.Close();

            // We've reconnected in game
            MultiplayerSession.InActiveSession = true;
            Game.Instance?.Trigger(MP_HASHES.OnConnected);
            NetworkConfig.TransportClient.OnClientConnected.Invoke();

            var hostId = MultiplayerSession.HostUserID;
            if (!MultiplayerSession.ConnectedPlayers.ContainsKey(hostId))
            {
                var hostPlayer = new MultiplayerPlayer(hostId);
                MultiplayerSession.ConnectedPlayers[hostId] = hostPlayer;
            }

            // Store the connection handle for host
            MultiplayerSession.ConnectedPlayers[hostId].Connection = Connection;

            DebugConsole.Log("[GameClient] Connection to host established!");

            // Skip mod verification if we are the host
			if (MultiplayerSession.IsHost)
			{
				return;
			}

			PacketHandler.readyToProcess = true;
			NetworkConfig.TransportClient.OnRequestStateOrReturn.Invoke();
		}

        private static void OnDisconnected(string reason, CSteamID remote, ESteamNetworkingConnectionState state)
        {
            using var _ = Profiler.Scope();

            DebugConsole.LogWarning($"[GameClient] Connection closed or failed ({state}) for {remote}. Reason: {reason}");

            // If we're intentionally disconnecting for world loading, don't show error or return to title
            // We will reconnect automatically after the world finishes loading via ReconnectFromCache()
            if (GameClient.State == ClientState.LoadingWorld)
            {
                DebugConsole.Log("[GameClient] Ignoring disconnect callback - world is loading, will reconnect after.");
                return;
            }

            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    // The host closed our connection
                    if (remote.m_SteamID == MultiplayerSession.HostUserID)
                    {
                        // Invoke(reason, message)
                        NetworkConfig.TransportClient.OnReturnToMenu.Invoke(STRINGS.UI.MP_OVERLAY.CLIENT.STEAMWORKS.HOST_DISCONNECTED, STRINGS.UI.MP_OVERLAY.CLIENT.STEAMWORKS.HOST_DISCONNECTED_DESC);
                    }
                    break;
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    // Something went wrong locally
                    NetworkConfig.TransportClient.OnReturnToMenu.Invoke(STRINGS.UI.MP_OVERLAY.CLIENT.STEAMWORKS.LOCAL_PROBLEM, STRINGS.UI.MP_OVERLAY.CLIENT.STEAMWORKS.LOCAL_PROBLEM_DESC);
                    break;
            }
        }

        #region Connection Health
        public static SteamNetConnectionRealTimeStatus_t? QueryConnectionHealth()
        {
            using var _ = Profiler.Scope();

            if (Connection.HasValue)
            {
                SteamNetConnectionRealTimeStatus_t status = default;
                SteamNetConnectionRealTimeLaneStatus_t laneStatus = default;

                EResult res = SteamNetworkingSockets.GetConnectionRealTimeStatus(
                        Connection.Value,
                        ref status,
                        0,
                        ref laneStatus
                );

                if (res == EResult.k_EResultOK)
                {
                    return status;
                }
            }
            return null;
        }

        public static void EvaluateConnectionHealth()
        {
            using var _ = Profiler.Scope();

            connectionHealth = QueryConnectionHealth();
        }

        public static SteamNetConnectionRealTimeStatus_t? GetConnectionHealth()
        {
            using var _ = Profiler.Scope();

            return connectionHealth;
        }

        public static float GetLocalPacketQuality()
        {
            using var _ = Profiler.Scope();

            if (!connectionHealth.HasValue)
                return 0f;

            return connectionHealth.Value.m_flConnectionQualityLocal;
        }

        public static float GetRemotePacketQuality()
        {
            using var _ = Profiler.Scope();

            if (!connectionHealth.HasValue)
                return 0f;

            return connectionHealth.Value.m_flConnectionQualityRemote;
        }

        public static int GetUnackedReliable()
        {
            using var _ = Profiler.Scope();

            if (!connectionHealth.HasValue)
                return -1;

            return connectionHealth.Value.m_cbSentUnackedReliable;
        }

        public static int GetPendingUnreliable()
        {
            using var _ = Profiler.Scope();

            if (!connectionHealth.HasValue)
                return -1;

            return connectionHealth.Value.m_cbPendingUnreliable;
        }

        public static long GetUsecQueueTime()
        {
            using var _ = Profiler.Scope();

            if (!connectionHealth.HasValue)
                return -1;

            return (long)connectionHealth.Value.m_usecQueueTime;
        }

        public static float GetSendRate()
        {
            if (!connectionHealth.HasValue) return 0f;
            return connectionHealth.Value.m_flOutBytesPerSec;
        }

        public static float GetRecvRate()
        {
            if (!connectionHealth.HasValue) return 0f;
            return connectionHealth.Value.m_flInBytesPerSec;
        }

        public static float GetInPacketsPerSec()
        {
            if (!connectionHealth.HasValue) return 0f;
            return connectionHealth.Value.m_flInPacketsPerSec;
        }

        public static float GetOutPacketsPerSec()
        {
            if (!connectionHealth.HasValue) return 0f;
            return connectionHealth.Value.m_flOutPacketsPerSec;
        }

        public override float IncomingBandwidth => GetRecvRate();
        public override float OutgoingBandwidth => GetSendRate();
        public override int IncomingPps => (int)GetInPacketsPerSec();
        public override int OutgoingPps => (int)GetOutPacketsPerSec();

        public override NetworkState GetJitterState()
        {
            using var _ = Profiler.Scope();

            var connhealth = GetConnectionHealth();
            if (!connhealth.HasValue)
                return NetworkState.BAD;

            int ping = connhealth.Value.m_nPing;
            if (ping <= 0)
                return NetworkState.BAD;

            // Collect samples
            _pingSamples.Enqueue(ping);
            while (_pingSamples.Count > JITTER_SAMPLE_COUNT)
                _pingSamples.Dequeue();

            // Not enough data yet
            if (_pingSamples.Count < 5)
                return NetworkState.DEGRADED;

            // Calculate mean
            float mean = 0f;
            foreach (var p in _pingSamples)
                mean += p;
            mean /= _pingSamples.Count;

            // Calculate standard deviation
            float variance = 0f;
            foreach (var p in _pingSamples)
            {
                float diff = p - mean;
                variance += diff * diff;
            }

            float jitter = Mathf.Sqrt(variance / _pingSamples.Count);

            if (jitter <= 10f)
                return NetworkState.GOOD;

            if (jitter <= 30f)
                return NetworkState.DEGRADED;

            return NetworkState.BAD;
        }

        public override NetworkState GetLatencyState()
        {
            using var _ = Profiler.Scope();

            var connhealth = GetConnectionHealth();
            if (!connhealth.HasValue)
                return NetworkState.BAD;

            int ping = connhealth.Value.m_nPing;

            // Invalid or unknown ping
            if (ping <= 0)
                return NetworkState.BAD;

            if (ping <= NetworkConfig.PingRanges.DEGRADED)
                return NetworkState.GOOD;

            if (ping <= NetworkConfig.PingRanges.BAD)
                return NetworkState.DEGRADED;

            return NetworkState.BAD;
        }

        public override NetworkState GetPacketlossState()
        {
            using var _ = Profiler.Scope();

            var connhealth = GetConnectionHealth();
            if (!connhealth.HasValue)
                return NetworkState.BAD;

            float quality = connhealth.Value.m_flConnectionQualityLocal;

            if (quality >= 0.95f)
                return NetworkState.GOOD;

            if (quality >= 0.85f)
                return NetworkState.DEGRADED;

            return NetworkState.BAD;
        }

        public override NetworkState GetServerPerformanceState()
        {
            using var _ = Profiler.Scope();

            if (!Connection.HasValue)
                return NetworkState.BAD;

            var connHealth = GetConnectionHealth();

            // Connection no longer exists
            if (!connHealth.HasValue)
                return NetworkState.BAD;

            // Authoritative disconnect states
            switch (connHealth.Value.m_eState)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                    return NetworkState.BAD;
            }

            // Hard transport failure
            if (connHealth.Value.m_cbSentUnackedReliable > 64 * 1024 || // 64kb unacked by server
                (long)connHealth.Value.m_usecQueueTime > 1_000_000 || // 1 second old unsent packets
                connHealth.Value.m_flConnectionQualityRemote <= 0.85f) // server side reports badly degraded quality
            {
                return NetworkState.BAD;
            }

            // Soft degradation
            if (connHealth.Value.m_cbSentUnackedReliable > 16 * 1024 || // 16kb unacked by server
                (long)connHealth.Value.m_usecQueueTime > 500_000 || // 500ms old unsent packets
                connHealth.Value.m_flConnectionQualityRemote <= 0.95f) // server side reports degraded quality
            {
                return NetworkState.DEGRADED;
            }

            return NetworkState.GOOD;
        }

        public override int GetPing()
        {
            using var _ = Profiler.Scope();

            if (!connectionHealth.HasValue)
                return -1;

            return connectionHealth.Value.m_nPing;
        }
        #endregion
    }
}
