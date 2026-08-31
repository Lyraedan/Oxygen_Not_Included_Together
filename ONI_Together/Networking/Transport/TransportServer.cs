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

        // A client disconnects to load the level and then reconnects. While it is gone the
        // host must keep treating it as unready, or the resume gate opens and the ready
        // screen closes mid-load. This bookkeeping lives on the base class rather than in one
        // transport because the signal that starts it (ClientReadyState.Loading, sent from
        // GameClient when the save is requested and again from SaveHelper before the
        // disconnect) is transport-agnostic and every transport needs the same answer.
        //
        // clientId -> unscaledTime at which the client reported it started loading.
        private readonly Dictionary<ulong, float> _loadingClients = new Dictionary<ulong, float>();

        // Clients whose reconnect was matched back to a pending load, so callers can tell a
        // returning loader apart from a genuinely new join (used to suppress the duplicate
        // "joined" chat line). Consumed once per entry.
        private readonly HashSet<ulong> _reconnectedFromLoad = new HashSet<ulong>();

        private readonly List<ulong> _expiredLoadingClients = new List<ulong>();

        // A load entry is only ever dropped by a matching reconnect or by this timeout. It has
        // to outlast a legitimately slow load, so it cannot reuse Host.TimeoutSeconds: that is
        // a socket timeout, defaults to 30s and is floored at 30s, while a large-colony
        // rebuild measured 20-30s on its own. Expiring at the same order as a healthy load
        // would open the gate on exactly the client this class exists to wait for. This is a
        // last-resort release for a client that hard crashed and is never coming back, so it
        // is sized well past any real load rather than tuned.
        private const float LOAD_RECONNECT_TIMEOUT_SECONDS = 120f;

        /// <summary>
        /// HOST ONLY - record that a client reported it is loading the level (and is therefore
        /// about to drop its connection). Repeated calls just refresh the timestamp.
        /// </summary>
        public void MarkClientLoading(ulong clientId)
        {
            // Logged because this lifecycle decides which chat line a departure gets (failed
            // join vs left) and whether the gate holds through a load window - and its main
            // caller, SaveFileRequestPacket, was otherwise silent about it.
            DebugConsole.Log(
                $"[TransportServer] {clientId} marked loading " +
                $"(was {(_loadingClients.ContainsKey(clientId) ? "already marked" : "unmarked")})");
            _loadingClients[clientId] = UnityEngine.Time.unscaledTime;
        }

        /// <summary>
        /// HOST ONLY - true (once) if this client's connect was matched back to a pending load,
        /// i.e. it is a returning loader rather than a new join.
        /// </summary>
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
        /// HOST ONLY - called from a transport's client-connected path to match a fresh
        /// connection against the pending loads. Returns true if this connect was a returning
        /// loader.
        ///
        /// Only Steamworks can match by id: it keys on the SteamID. Neither LAN transport can -
        /// both derive the client id from the peer handle, and a returning loader is measured
        /// to come back under a new one (a client that left as id 2 returned as id 3). So the
        /// LAN case falls back to releasing the longest-pending entry.
        ///
        /// That fallback is a guess, and the guess is wrong when a brand-new client connects
        /// while someone else is loading: it releases the loader's entry, and the gate is then
        /// held only by the new client's Unready flag, so it opens as soon as that client
        /// reports Ready. It needs two clients moving at once to bite, and the alternative -
        /// leaving the entry alone - stalls the gate for the full timeout after every single
        /// load. Restoring a stable client id across a reconnect would remove the choice.
        ///
        /// This base implementation is the LAN one. SteamworksServer overrides it to drop the
        /// fallback, so the guess cannot fire on a transport that does not need it.
        /// </summary>
        public virtual bool ClaimLoadingReconnect(ulong clientId)
        {
            return ClaimExactLoadingReconnect(clientId) || ClaimOldestLoadingReconnect(clientId);
        }

        /// <summary>
        /// HOST ONLY - release the pending load recorded under this exact client id. Only
        /// meaningful on a transport whose client id survives a reconnect.
        /// </summary>
        protected bool ClaimExactLoadingReconnect(ulong clientId)
        {
            if (!_loadingClients.Remove(clientId))
                return false;

            DebugConsole.Log($"[TransportServer] {clientId} reconnected; cleared its pending load");
            _reconnectedFromLoad.Add(clientId);
            return true;
        }

        /// <summary>
        /// HOST ONLY - release the longest-pending load entry and credit it to
        /// <paramref name="clientId"/>. See the warning on
        /// <see cref="ClaimLoadingReconnect"/> for what this costs when the guess is wrong.
        /// </summary>
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

            // The only branch in this mechanism that guesses. Worth a line in the log: when
            // the gate misbehaves, this tells you whether an entry was released by an id match
            // or by assumption, and which entry got consumed.
            DebugConsole.Log(
                $"[TransportServer] {clientId} has no pending load of its own; assuming it is " +
                $"returning loader {oldest} (pending loads left: {_loadingClients.Count}). " +
                "Wrong if a different client connected while someone else was loading.");

            return true;
        }

        /// <summary>
        /// HOST ONLY - drop a client's pending load without crediting it as a returning
        /// loader. For a client that is not coming back: a kicked client would otherwise hold
        /// the gate until the timeout, and its own disconnect event cannot clear it (the kick
        /// has already removed the peer mapping that event is keyed on).
        /// </summary>
        public void ForgetClientLoading(ulong clientId)
        {
            if (_loadingClients.Remove(clientId))
                DebugConsole.Log($"[TransportServer] Dropped pending load for {clientId} (kick/leave)");
            _reconnectedFromLoad.Remove(clientId);
        }

        /// <summary>
        /// HOST ONLY - drop all load bookkeeping. Call when the server stops: the transport
        /// instance outlives a session (NetworkConfig only replaces it when the transport
        /// itself changes), so without this a client that was mid-load when the host shut
        /// down still holds the gate closed in the *next* session until it ages out.
        /// </summary>
        protected void ClearLoadTracking()
        {
            _loadingClients.Clear();
            _reconnectedFromLoad.Clear();
        }

        /// <summary>
        /// HOST ONLY - drop load entries that never came back, so a client that timed out or
        /// hard crashed mid-load cannot hold the resume gate closed forever. Call from
        /// Update().
        /// </summary>
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

            // Dropping the entry is not enough on its own. Nothing else recomputes the gate on
            // a timer, so without this refresh the safety net silently frees the count and the
            // host still sits on the ready screen until some unrelated event happens to
            // recalculate. Measured in a Steam session: a client left for good at 05:14:04
            // holding one pending load, the entry expired ~05:16 with no visible effect, and
            // the gate only reported OPEN at 05:19:28 when a late ClosedByPeer arrived -
            // 5m24s of the world held frozen by a player who was already gone.
            DebugConsole.Log(
                $"[TransportServer] Expired {_expiredLoadingClients.Count} stale load(s); " +
                $"pending loads now {PendingLoadingClientCount}");

            // Tell the table why the wait ended. A loader that never came back has failed to
            // join, and without this the gate simply opens with no explanation of who is
            // missing.
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
        /// HOST ONLY - how many clients are mid "disconnect-to-load-then-reconnect" and have
        /// therefore dropped off the live roster but are NOT gone. The resume gate and the
        /// ready screen must keep treating them as unready/expected until they return.
        ///
        /// Only off-roster loaders count: a client signals Loading just *before* it
        /// disconnects, so for a brief window its id is in both _loadingClients and
        /// ConnectedPlayers - where it is already counted (as Unready). Counting it here too
        /// would inflate the ready-screen total (e.g. "1/3" for a single loading client).
        /// This also makes the count naturally zero for a transport that keeps a
        /// Connection==null placeholder in the roster for the whole load (Steamworks): an
        /// in-roster loader holds the gate via its Unready state, an off-roster loader holds
        /// it via this count - no double count, no gap.
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
