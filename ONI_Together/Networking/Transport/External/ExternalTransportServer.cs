using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Transfer;
using Shared;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Networking.Transport.External
{
    /// <summary>
    /// Adapts a foreign host transport to the main mod's TransportServer surface.
    /// </summary>
    public sealed class ExternalTransportServer : TransportServer
    {
        private const int MaxPacketsPerPoll = 4096;
        private const int MaxPayloadBytes = 8 * 1024 * 1024;

        private readonly ExternalTransportProvider _provider;
        private readonly System.Action<Dictionary<string, string>> _start;
        private readonly System.Action _stop;
        private readonly System.Action _update;
        private readonly TryDequeueDelegate _tryDequeue;
        private readonly Func<ulong[]> _getPlayers;
        private readonly Func<ulong, byte[], int, bool> _send;
        private readonly System.Action<ulong> _kick;
        private readonly Func<ulong> _localPlayerId;
        private readonly Func<string> _lastError;
        private readonly Func<int> _getFileTransferPort;

        private readonly HashSet<ulong> _knownPlayers = new();
        private string _lastReportedError = string.Empty;
        private bool _stopping;

        public Dictionary<string, string> Settings { get; set; } = new();

        public ExternalTransportProvider Provider => _provider;

        /// <summary>Optional host-side TCP save transfer service started with the transport.</summary>
        public TcpFileTransferServer TcpTransfer { get; private set; }

        public ExternalTransportServer(ExternalTransportProvider provider, object instance)
        {
            _provider = provider;
            if (!ExternalApiBinder.TryGetDelegate<System.Action<Dictionary<string, string>>>(
                    instance, "Start", [typeof(Dictionary<string, string>)], out _start))
                throw new InvalidOperationException("Transport server is missing public Start(Dictionary<string,string>).");

            if (!ExternalApiBinder.TryGetDelegate<System.Action>(instance, "Stop", Type.EmptyTypes, out _stop))
                throw new InvalidOperationException("Transport server is missing public Stop().");

            if (!ExternalApiBinder.TryGetDelegate<System.Action>(instance, "Update", Type.EmptyTypes, out _update))
                throw new InvalidOperationException("Transport server is missing public Update().");

            if (!ExternalApiBinder.TryGetDelegate<TryDequeueDelegate>(
                    instance, "TryDequeueReceived",
                    [typeof(ulong).MakeByRefType(), typeof(byte[]).MakeByRefType()], out _tryDequeue))
                throw new InvalidOperationException(
                    "Transport server is missing public TryDequeueReceived(out ulong, out byte[]).");

            if (!ExternalApiBinder.TryGetDelegate<Func<ulong[]>>(
                    instance, "GetConnectedPlayerIds", Type.EmptyTypes, out _getPlayers))
                throw new InvalidOperationException("Transport server is missing public GetConnectedPlayerIds().");

            if (!ExternalApiBinder.TryGetDelegate<Func<ulong, byte[], int, bool>>(
                    instance, "Send", [typeof(ulong), typeof(byte[]), typeof(int)], out _send))
                throw new InvalidOperationException("Transport server is missing public Send(ulong, byte[], int).");

            if (!ExternalApiBinder.TryGetPropertyGetter(instance, "LocalPlayerId", out _localPlayerId))
                throw new InvalidOperationException("Transport server is missing public LocalPlayerId.");

            ExternalApiBinder.TryGetDelegate<System.Action<ulong>>(
                instance, "Kick", [typeof(ulong)], out _kick);

            if (!ExternalApiBinder.TryGetPropertyGetter(instance, "LastError", out _lastError))
                _lastError = null;

            if (provider.SupportsFileTransfer)
            {
                ExternalApiBinder.TryGetDelegate<Func<int>>(
                    instance, "GetFileTransferPort", Type.EmptyTypes, out _getFileTransferPort);
            }
        }

        public override ulong LocalPlayerId
        {
            get
            {
                try
                {
                    return _localPlayerId != null ? _localPlayerId() : 0UL;
                }
                catch
                {
                    return 0UL;
                }
            }
        }

        public override void Prepare()
        {
        }

        public override void Start()
        {
            using var _ = Profiler.Scope();

            _stopping = false;
            _lastReportedError = string.Empty;
            _knownPlayers.Clear();
            StopFileTransfer();

            try
            {
                _start(Settings ?? new Dictionary<string, string>());
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' server Start failed: {ex}");
                OnError?.Invoke();
                return;
            }

            ulong hostId = LocalPlayerId;
            if (hostId == 0)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' server did not provide a LocalPlayerId.");
                OnError?.Invoke();
                return;
            }

            MultiplayerSession.SetHost(hostId);
            MultiplayerSession.InActiveSession = true;
            ReconcilePlayers(initial: true);
            TryStartFileTransfer();

            TryAddChatMessage(string.Format(
                STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STARTED,
                _provider.DisplayName));
        }

        public override void Stop()
        {
            using var _ = Profiler.Scope();

            _stopping = true;
            try
            {
                _stop();
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' server Stop failed: {ex}");
            }

            StopFileTransfer();

            TryAddChatMessage(string.Format(
                STRINGS.UI.MP_CHATWINDOW.CHAT_SERVER_STOPPED,
                _provider.DisplayName));

            _knownPlayers.Clear();
            MultiplayerSession.InActiveSession = false;
            MultiplayerSession.HostUserID = Utils.NilUlong();
        }

        public override void CloseConnections()
        {
            // Stop() owns the full teardown. GameServer.Shutdown calls both.
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
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' server Update failed: {ex}");
                OnError?.Invoke();
                return;
            }

            ReportErrorIfChanged();
            ReconcilePlayers(initial: false);
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
                    DebugConsole.LogError($"[Transport-API] '{_provider.Id}' server receive failed: {ex}");
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

        public override void KickClient(ulong clientId)
        {
            try
            {
                _kick?.Invoke(clientId);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' server Kick failed: {ex}");
            }
        }

        public override IReadOnlyList<ulong> GetConnectedPlayerIds()
        {
            var result = new List<ulong>();
            ulong hostId = LocalPlayerId;

            try
            {
                var ids = _getPlayers?.Invoke();
                if (ids != null)
                    result.AddRange(ids);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' GetConnectedPlayerIds failed: {ex}");
            }

            if (hostId != 0 && !result.Contains(hostId))
                result.Add(hostId);

            return result;
        }

        public bool SendRaw(ulong playerId, byte[] payload, PacketSendMode sendMode)
        {
            try
            {
                return _send(playerId, payload, (int)sendMode);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' server Send failed: {ex}");
                return false;
            }
        }

        private void ReconcilePlayers(bool initial)
        {
            if (_stopping || MultiplayerSession.IsQuitting)
                return;

            ulong hostId = LocalPlayerId;
            if (hostId == 0)
                return;

            var present = new HashSet<ulong>(GetConnectedPlayerIds());
            present.Add(hostId);

            foreach (ulong id in present)
            {
                if (id == 0)
                    continue;

                if (!_knownPlayers.Add(id))
                    continue;

                if (MultiplayerSession.ConnectedPlayers.ContainsKey(id))
                    continue;

                var player = new MultiplayerPlayer(id)
                {
                    Connection = id == hostId ? null : (object)id
                };

                if (id == hostId)
                    player.PlayerName = Utils.GetLocalPlayerName();

                MultiplayerSession.ConnectedPlayers[id] = player;
                MultiplayerSession.KnownPlayerNames[id] = player.PlayerName;

                if (initial || id == hostId)
                    continue;

                MultiplayerSession.KnownPlayerNames[id] = player.PlayerName;

                var boxedId = Boxed<ulong>.Get(id);
                Game.Instance?.Trigger(MP_HASHES.OnPlayerJoined, boxedId);
                boxedId.Release();

                ReadyManager.RefreshReadyState();
                MultiplayerSession.RefreshAllPlayerCursors();
            }

            var removed = new List<ulong>();
            foreach (ulong id in _knownPlayers)
            {
                if (id != hostId && !present.Contains(id))
                    removed.Add(id);
            }

            foreach (ulong id in removed)
            {
                _knownPlayers.Remove(id);

                if (MultiplayerSession.ConnectedPlayers.TryGetValue(id, out var player))
                {
                    player.Connection = null;
                    MultiplayerSession.ConnectedPlayers.Remove(id);
                }

                if (initial)
                    continue;

                string name = MultiplayerSession.KnownPlayerNames.TryGetValue(id, out var cached)
                    ? cached
                    : $"Player {id}";
                TryAddChatMessage(string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_LEFT, name));
                Utils.PauseSimOnPlayerLeft();

                var boxedId = Boxed<ulong>.Get(id);
                Game.Instance?.Trigger(MP_HASHES.OnPlayerLeft, boxedId);
                boxedId.Release();
            }

            if (removed.Count > 0)
            {
                ReadyManager.RefreshReadyState();
                MultiplayerSession.RefreshAllPlayerCursors();
            }
        }

        private void TryStartFileTransfer()
        {
            if (!_provider.SupportsFileTransfer || _getFileTransferPort == null)
                return;

            int port;
            try
            {
                port = _getFileTransferPort();
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] '{_provider.Id}' GetFileTransferPort failed: {ex.Message}");
                return;
            }

            if (port <= 0 || port > ushort.MaxValue)
                return;

            try
            {
                var server = new TcpFileTransferServer();
                server.StartOnPort(port);
                TcpTransfer = server;
                DebugConsole.Log($"[Transport-API] '{_provider.Id}' save transfer listening on TCP {port}.");
            }
            catch (Exception ex)
            {
                TcpTransfer = null;
                DebugConsole.LogWarning(
                    $"[Transport-API] '{_provider.Id}' could not start TCP save transfer on port {port}: {ex.Message}");
            }
        }

        private void StopFileTransfer()
        {
            TcpFileTransferServer server = TcpTransfer;
            TcpTransfer = null;

            if (server == null)
                return;

            try
            {
                server.Stop();
            }
            catch (Exception ex)
            {
                DebugConsole.LogWarning(
                    $"[Transport-API] '{_provider.Id}' TCP save transfer stop failed: {ex.Message}");
            }
        }

        private void ReportErrorIfChanged()
        {
            string error = string.Empty;
            try
            {
                error = _lastError?.Invoke() ?? string.Empty;
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(error) || error == _lastReportedError)
                return;

            _lastReportedError = error;
            DebugConsole.LogError($"[Transport-API] '{_provider.Id}' server error: {error}");
            OnError?.Invoke();
        }

        private static void TryAddChatMessage(string message)
        {
            try
            {
                OxySyncChat.AddSystemMessage(message);
            }
            catch
            {
            }
        }
    }
}
