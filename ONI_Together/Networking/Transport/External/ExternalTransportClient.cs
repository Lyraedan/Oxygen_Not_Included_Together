using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using Shared;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking.Transport.External
{
    /// <summary>
    /// Adapts a foreign client transport to the main mod's TransportClient surface.
    /// </summary>
    public sealed class ExternalTransportClient : TransportClient
    {
        private const int MaxPacketsPerPoll = 4096;
        private const int MaxPayloadBytes = 8 * 1024 * 1024;

        private readonly ExternalTransportProvider _provider;
        private readonly System.Action<Dictionary<string, string>> _connect;
        private readonly System.Action _disconnect;
        private readonly System.Action<Dictionary<string, string>> _reconnect;
        private readonly System.Action _update;
        private readonly TryDequeueDelegate _tryDequeue;
        private readonly Func<ulong, byte[], int, bool> _send;
        private readonly Func<ulong> _localPlayerId;
        private readonly Func<ulong> _hostPlayerId;
        private readonly Func<bool> _isConnected;
        private readonly Func<string> _lastDisconnectReason;
        private readonly Func<string> _lastDisconnectMessage;
        private readonly Func<int> _getPing;
        private readonly Func<int> _getJitterState;
        private readonly Func<int> _getLatencyState;
        private readonly Func<int> _getPacketLossState;
        private readonly Func<int> _getServerPerformanceState;
        private readonly Func<string> _getFileTransferHost;

        private bool _wasConnected;
        private bool _intentionalDisconnect;
        private bool _connectRequested;

        public Dictionary<string, string> Settings { get; set; } = new();

        public ExternalTransportProvider Provider => _provider;

        public ExternalTransportClient(ExternalTransportProvider provider, object instance)
        {
            _provider = provider;
            if (!ExternalApiBinder.TryGetDelegate<System.Action<Dictionary<string, string>>>(
                    instance, "Connect", [typeof(Dictionary<string, string>)], out _connect))
                throw new InvalidOperationException("Transport client is missing public Connect(Dictionary<string,string>).");

            if (!ExternalApiBinder.TryGetDelegate<System.Action>(instance, "Disconnect", Type.EmptyTypes, out _disconnect))
                throw new InvalidOperationException("Transport client is missing public Disconnect().");

            ExternalApiBinder.TryGetDelegate<System.Action<Dictionary<string, string>>>(
                instance, "Reconnect", [typeof(Dictionary<string, string>)], out _reconnect);

            if (!ExternalApiBinder.TryGetDelegate<System.Action>(instance, "Update", Type.EmptyTypes, out _update))
                throw new InvalidOperationException("Transport client is missing public Update().");

            if (!ExternalApiBinder.TryGetDelegate<TryDequeueDelegate>(
                    instance, "TryDequeueReceived",
                    [typeof(ulong).MakeByRefType(), typeof(byte[]).MakeByRefType()], out _tryDequeue))
                throw new InvalidOperationException(
                    "Transport client is missing public TryDequeueReceived(out ulong, out byte[]).");

            if (!ExternalApiBinder.TryGetDelegate<Func<ulong, byte[], int, bool>>(
                    instance, "Send", [typeof(ulong), typeof(byte[]), typeof(int)], out _send))
                throw new InvalidOperationException("Transport client is missing public Send(ulong, byte[], int).");

            if (!ExternalApiBinder.TryGetPropertyGetter(instance, "LocalPlayerId", out _localPlayerId))
                throw new InvalidOperationException("Transport client is missing public LocalPlayerId.");

            if (!ExternalApiBinder.TryGetPropertyGetter(instance, "HostPlayerId", out _hostPlayerId))
                throw new InvalidOperationException("Transport client is missing public HostPlayerId.");

            if (!ExternalApiBinder.TryGetPropertyGetter(instance, "IsConnected", out _isConnected))
                throw new InvalidOperationException("Transport client is missing public IsConnected.");

            if (!ExternalApiBinder.TryGetPropertyGetter(instance, "LastDisconnectReason", out _lastDisconnectReason))
                _lastDisconnectReason = null;

            if (!ExternalApiBinder.TryGetPropertyGetter(instance, "LastDisconnectMessage", out _lastDisconnectMessage))
                _lastDisconnectMessage = null;

            if (!ExternalApiBinder.TryGetDelegate<Func<int>>(instance, "GetPing", Type.EmptyTypes, out _getPing))
                _getPing = null;

            if (!ExternalApiBinder.TryGetDelegate<Func<int>>(instance, "GetJitterState", Type.EmptyTypes, out _getJitterState))
                _getJitterState = null;

            if (!ExternalApiBinder.TryGetDelegate<Func<int>>(instance, "GetLatencyState", Type.EmptyTypes, out _getLatencyState))
                _getLatencyState = null;

            if (!ExternalApiBinder.TryGetDelegate<Func<int>>(instance, "GetPacketLossState", Type.EmptyTypes, out _getPacketLossState))
                _getPacketLossState = null;

            if (!ExternalApiBinder.TryGetDelegate<Func<int>>(instance, "GetServerPerformanceState", Type.EmptyTypes, out _getServerPerformanceState))
                _getServerPerformanceState = null;

            if (provider.SupportsFileTransfer)
            {
                ExternalApiBinder.TryGetDelegate<Func<string>>(
                    instance, "GetFileTransferHost", Type.EmptyTypes, out _getFileTransferHost);
            }
        }

        public override ulong LocalPlayerId => SafeCall(_localPlayerId, 0UL);
        public override ulong HostPlayerId => SafeCall(_hostPlayerId, 0UL);
        public override bool IsConnected => SafeCall(_isConnected, false);

        public override void Prepare()
        {
        }

        public override void ConnectToHost(string ip, int port)
        {
            using var _ = Profiler.Scope();

            _wasConnected = false;
            _intentionalDisconnect = false;
            _connectRequested = true;

            try
            {
                _connect(Settings ?? new Dictionary<string, string>());
            }
            catch (Exception ex)
            {
                _connectRequested = false;
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' client Connect failed: {ex}");
                OnReturnToMenu?.Invoke(string.Empty, string.Empty);
            }
        }

        public override void Disconnect()
        {
            using var _ = Profiler.Scope();

            _intentionalDisconnect = true;
            _connectRequested = false;
            try
            {
                _disconnect();
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' client Disconnect failed: {ex}");
            }

            if (_wasConnected)
            {
                _wasConnected = false;
                HandleDisconnected(showMessage: false);
            }
        }

        public override void ReconnectToSession()
        {
            using var _ = Profiler.Scope();

            _wasConnected = false;
            _intentionalDisconnect = false;
            _connectRequested = true;

            try
            {
                if (_reconnect != null)
                    _reconnect(Settings ?? new Dictionary<string, string>());
                else
                {
                    _disconnect();
                    _connect(Settings ?? new Dictionary<string, string>());
                }
            }
            catch (Exception ex)
            {
                _connectRequested = false;
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' client Reconnect failed: {ex}");
                OnReturnToMenu?.Invoke(string.Empty, string.Empty);
            }
        }

        public override void Update()
        {
            using var _ = Profiler.Scope();

            try
            {
                _update();
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' client Update failed: {ex}");
                OnReturnToMenu?.Invoke(string.Empty, string.Empty);
                return;
            }

            bool connected = IsConnected;

            if (connected && !_wasConnected)
            {
                _wasConnected = true;
                _connectRequested = false;
                HandleConnected();
            }
            else if (!connected && _wasConnected)
            {
                _wasConnected = false;
                HandleDisconnected(!_intentionalDisconnect);
                _intentionalDisconnect = false;
            }
            else if (!connected && !_wasConnected && _connectRequested && !_intentionalDisconnect)
            {
                // Surface a failed initial connection even though the transport was
                // never in the connected state.
                string reason = SafeCall(_lastDisconnectReason, string.Empty);
                if (!string.IsNullOrEmpty(reason))
                {
                    _connectRequested = false;
                    HandleDisconnected(showMessage: true);
                }
            }
        }

        public override void OnMessageRecieved()
        {
            using var _ = Profiler.Scope();

            int processed = 0;
            while (processed++ < MaxPacketsPerPoll)
            {
                ulong senderId;
                byte[] payload;
                try
                {
                    if (!_tryDequeue(out senderId, out payload))
                        break;
                }
                catch (Exception ex)
                {
                    DebugConsole.LogError($"[Transport-API] '{_provider.Id}' client receive failed: {ex}");
                    break;
                }

                if (payload == null || payload.Length == 0)
                    continue;

                if (payload.Length > MaxPayloadBytes)
                {
                    DebugConsole.LogWarning(
                        $"[Transport-API] '{_provider.Id}' dropped {payload.Length}-byte payload from {senderId}.");
                    continue;
                }

                try
                {
                    PacketHandler.HandleIncoming(payload);
                }
                catch (Exception ex)
                {
                    DebugConsole.LogError(
                        $"[Transport-API] '{_provider.Id}' failed to dispatch packet from {senderId}: {ex}");
                }
            }
        }

        public bool SendRaw(ulong playerId, byte[] payload, PacketSendMode sendMode)
        {
            try
            {
                return _send(playerId, payload, (int)sendMode);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' client Send failed: {ex}");
                return false;
            }
        }

        public override int GetPing() => SafeCall(_getPing, -1);
        public override NetworkIndicatorsScreen.NetworkState GetJitterState() => ToState(SafeCall(_getJitterState, 0));
        public override NetworkIndicatorsScreen.NetworkState GetLatencyState() => ToState(SafeCall(_getLatencyState, 0));
        public override NetworkIndicatorsScreen.NetworkState GetPacketlossState() => ToState(SafeCall(_getPacketLossState, 0));
        public override NetworkIndicatorsScreen.NetworkState GetServerPerformanceState() => ToState(SafeCall(_getServerPerformanceState, 0));

        /// <summary>True when this transport can serve saves over its own direct TCP port.</summary>
        public bool SupportsFileTransfer => _provider.SupportsFileTransfer && _getFileTransferHost != null;

        /// <summary>Host address for the direct TCP save transfer, or an empty string.</summary>
        public string GetFileTransferHost() => SafeCall(_getFileTransferHost, string.Empty);

        private static NetworkIndicatorsScreen.NetworkState ToState(int value)
        {
            return value switch
            {
                1 => NetworkIndicatorsScreen.NetworkState.DEGRADED,
                2 => NetworkIndicatorsScreen.NetworkState.BAD,
                _ => NetworkIndicatorsScreen.NetworkState.GOOD
            };
        }

        private void HandleConnected()
        {
            using var _ = Profiler.Scope();

            ulong hostId = HostPlayerId;
            if (hostId == 0)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' client connected without a HostPlayerId.");
                OnReturnToMenu?.Invoke(string.Empty, string.Empty);
                return;
            }

            ulong localId = LocalPlayerId;

            MultiplayerSession.SetHost(hostId);
            MultiplayerSession.InActiveSession = true;
            PacketHandler.readyToProcess = true;

            if (!MultiplayerSession.ConnectedPlayers.ContainsKey(hostId))
            {
                MultiplayerSession.ConnectedPlayers[hostId] = new MultiplayerPlayer(hostId)
                {
                    Connection = hostId
                };
            }

            if (localId != 0)
                MultiplayerSession.KnownPlayerNames[localId] = Utils.GetLocalPlayerName();

            DebugConsole.Log($"[Transport-API] '{_provider.Id}' client connected to host {hostId}.");
            Game.Instance?.Trigger(MP_HASHES.OnConnected);

            OnClientConnected?.Invoke();
            OnRequestStateOrReturn?.Invoke();
        }

        private void HandleDisconnected(bool showMessage)
        {
            using var _ = Profiler.Scope();

            OnClientDisconnected?.Invoke();
            MultiplayerSession.ConnectedPlayers.Clear();
            MultiplayerSession.InActiveSession = false;
            MultiplayerSession.HostUserID = Utils.NilUlong();

            if (!showMessage)
                return;

            string reason = SafeCall(_lastDisconnectReason, string.Empty);
            string message = SafeCall(_lastDisconnectMessage, string.Empty);
            DebugConsole.Log($"[Transport-API] '{_provider.Id}' client disconnected. {reason} {message}");
            OnReturnToMenu?.Invoke(reason, message);
        }

        private static T SafeCall<T>(Func<T> func, T fallback)
        {
            if (func == null)
                return fallback;

            try
            {
                return func();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
