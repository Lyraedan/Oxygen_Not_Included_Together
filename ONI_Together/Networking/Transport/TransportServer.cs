using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.DebugTools;
using ONI_Together.Networking.OxySync.Components;

namespace ONI_Together.Networking.Transport
{
    public abstract class TransportServer
    {
        public System.Action OnError;

        public abstract void Prepare();

        public abstract void Start();

        public abstract void Stop();

        public abstract void CloseConnections();

        public abstract void Update();

        public abstract void OnMessageRecieved();

        public abstract void KickClient(ulong clientId);

        // Bandwidth tracking (bytes/sec, packets/sec)
        public virtual float IncomingBandwidth => 0f;
        public virtual float OutgoingBandwidth => 0f;
        public virtual int IncomingPps => 0;
        public virtual int OutgoingPps => 0;

        #region Load-in-flight tracking

        // A client disconnects to load the level and then reconnects. While it is gone the host
        // must keep treating it as unready, or the resume gate opens and the ready screen closes
        // mid-load. Lives on the base class because both start signals (host hands out a save;
        // client sends ClientReadyState.Loading) are transport-agnostic.

        // clientId -> unscaledTime at which the load was recorded.
        private readonly Dictionary<ulong, float> _loadingClients = new Dictionary<ulong, float>();

        // Reconnects matched back to a pending load, so a returning loader can be told apart
        // from a new join (suppresses a duplicate "joined" chat line). Consumed once.
        private readonly HashSet<ulong> _reconnectedFromLoad = new HashSet<ulong>();

        private readonly List<ulong> _expiredLoadingClients = new List<ulong>();

        // Entries are dropped by a matching reconnect or by this timeout. It cannot reuse
        // Host.TimeoutSeconds (a socket timeout, defaulted and floored at 30s) because a
        // large-colony load takes 20-30s on its own - expiring there would open the gate on
        // exactly the client this class waits for. Sized well past any real load, not tuned.
        private const float LOAD_RECONNECT_TIMEOUT_SECONDS = 120f;

        /// <summary>HOST ONLY - record that a client is loading the level and is about to drop
        /// its connection. Repeated calls refresh the timestamp.</summary>
        public void MarkClientLoading(ulong clientId)
        {
            // Logged: this lifecycle picks the departure chat line and holds the resume gate.
            DebugConsole.Log(
                $"[TransportServer] {clientId} marked loading " +
                $"(was {(_loadingClients.ContainsKey(clientId) ? "already marked" : "unmarked")})");
            _loadingClients[clientId] = UnityEngine.Time.unscaledTime;
        }

        /// <summary>HOST ONLY - true (once) if this client's connect was matched back to a
        /// pending load, i.e. a returning loader rather than a new join.</summary>
        public bool ConsumeReconnectFromLoad(ulong clientId)
        {
            return _reconnectedFromLoad.Remove(clientId);
        }

        /// <summary>HOST ONLY - true while this exact client id has an in-flight load recorded.</summary>
        public bool IsClientLoading(ulong clientId)
        {
            return _loadingClients.ContainsKey(clientId);
        }

        /// <summary>
        /// HOST ONLY - match a fresh connection against the pending loads; true if this connect
        /// was a returning loader. Only Steamworks matches by id (keyed on the SteamID); both
        /// LAN transports derive the client id from the peer handle, so a returning loader
        /// arrives under a new id and they fall back to releasing the longest-pending entry.
        /// That guess is wrong when a new client connects while someone else is loading, leaving
        /// the gate held only by the new client's Unready flag - but do not "fix" it by keeping
        /// the entry, which stalls the gate for the full timeout after every load.
        /// SteamworksServer overrides out the fallback.
        /// </summary>
        public virtual bool ClaimLoadingReconnect(ulong clientId)
        {
            return ClaimExactLoadingReconnect(clientId) || ClaimOldestLoadingReconnect(clientId);
        }

        /// <summary>HOST ONLY - release the pending load under this exact client id. Only
        /// meaningful on a transport whose client id survives a reconnect.</summary>
        protected bool ClaimExactLoadingReconnect(ulong clientId)
        {
            if (!_loadingClients.Remove(clientId))
                return false;

            DebugConsole.Log($"[TransportServer] {clientId} reconnected; cleared its pending load");
            _reconnectedFromLoad.Add(clientId);
            return true;
        }

        /// <summary>HOST ONLY - release the longest-pending load entry and credit it to
        /// <paramref name="clientId"/>. See <see cref="ClaimLoadingReconnect"/> for the cost.</summary>
        protected bool ClaimOldestLoadingReconnect(ulong clientId)
        {
            if (_loadingClients.Count == 0)
                return false;

            ulong oldest = 0;
            float oldestStartedAt = float.MaxValue;
            foreach (var kvp in _loadingClients)
            {
                if (kvp.Value >= oldestStartedAt)
                    continue;

                oldestStartedAt = kvp.Value;
                oldest = kvp.Key;
            }

            _loadingClients.Remove(oldest);
            _reconnectedFromLoad.Add(clientId);

            // The only branch that guesses - log it so a bad gate traces back to the assumption.
            DebugConsole.Log(
                $"[TransportServer] {clientId} has no pending load of its own; assuming it is " +
                $"returning loader {oldest} (pending loads left: {_loadingClients.Count}). " +
                "Wrong if a different client connected while someone else was loading.");

            return true;
        }

        /// <summary>HOST ONLY - drop a pending load without crediting a returning loader. A
        /// kicked client would otherwise hold the gate until the timeout, and its own disconnect
        /// event cannot clear it: the kick already removed the peer mapping that event keys on.</summary>
        public void ForgetClientLoading(ulong clientId)
        {
            if (_loadingClients.Remove(clientId))
                DebugConsole.Log($"[TransportServer] Dropped pending load for {clientId} (kick/leave)");
            _reconnectedFromLoad.Remove(clientId);
        }

        /// <summary>HOST ONLY - drop all load bookkeeping when the server stops. The transport
        /// instance outlives a session (NetworkConfig replaces it only when the transport itself
        /// changes), so a client left mid-load would hold the *next* session's gate closed.</summary>
        protected void ClearLoadTracking()
        {
            _loadingClients.Clear();
            _reconnectedFromLoad.Clear();
        }

        /// <summary>HOST ONLY - drop load entries that never came back, so a client that timed
        /// out or crashed mid-load cannot hold the resume gate closed forever. Call from Update().</summary>
        protected void ExpireStaleLoadingClients()
        {
            if (_loadingClients.Count == 0)
                return;

            float now = UnityEngine.Time.unscaledTime;
            _expiredLoadingClients.Clear();

            foreach (var kvp in _loadingClients)
            {
                if (now - kvp.Value > LOAD_RECONNECT_TIMEOUT_SECONDS)
                    _expiredLoadingClients.Add(kvp.Key);
            }

            if (_expiredLoadingClients.Count == 0)
                return;

            foreach (ulong id in _expiredLoadingClients)
                _loadingClients.Remove(id);

            // Dropping entries is not enough - nothing else recomputes the gate on a timer, so
            // without the refresh below the host sits on the ready screen indefinitely.
            DebugConsole.Log(
                $"[TransportServer] Expired {_expiredLoadingClients.Count} stale load(s); " +
                $"pending loads now {PendingLoadingClientCount}");

            // A loader that never came back has failed to join; without this the gate just
            // opens with no explanation of who is missing.
            foreach (ulong id in _expiredLoadingClients)
            {
                string name = MultiplayerSession.KnownPlayerNames.TryGetValue(id, out var known)
                    ? known
                    : id.ToString();
                OxySyncChat.AddSystemMessage(
                    string.Format(STRINGS.UI.MP_CHATWINDOW.CHAT_CLIENT_FAILED, name));
            }

            ReadyManager.RefreshScreen();
            ReadyManager.RefreshReadyState();
        }

        /// <summary>
        /// HOST ONLY - clients mid "disconnect-to-load-then-reconnect": off the live roster but
        /// NOT gone, so the resume gate and ready screen must keep expecting them.
        ///
        /// Only off-roster loaders count. Loading is signalled just *before* the disconnect, so
        /// the id is briefly in ConnectedPlayers too, already counted there as Unready - counting
        /// it twice inflates the ready-screen total. That also makes this naturally zero on
        /// Steamworks, which keeps a Connection==null roster placeholder for the whole load.
        /// </summary>
        public int PendingLoadingClientCount
        {
            get
            {
                int count = 0;
                foreach (ulong id in _loadingClients.Keys)
                {
                    if (!MultiplayerSession.ConnectedPlayers.ContainsKey(id))
                        count++;
                }
                return count;
            }
        }

        /// <summary>HOST ONLY - true while any client is mid load-reconnect (see <see cref="PendingLoadingClientCount"/>).</summary>
        public bool HasPendingLoadingClients => PendingLoadingClientCount > 0;

        #endregion
    }
}
