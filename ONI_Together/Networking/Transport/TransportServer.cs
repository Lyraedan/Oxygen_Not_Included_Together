using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        /// Matching is by id and nothing else, because guessing is not safe here: consuming
        /// the entry of a client that is still loading drops PendingLoadingClientCount to zero,
        /// and the gate then rests solely on the *new* client's Unready flag - so it opens as
        /// soon as that client reports Ready, with the original loader still mid-load. That is
        /// precisely the failure this class exists to prevent, so an unmatched connect leaves
        /// the pending entry alone and the gate stays closed.
        ///
        /// LiteNetLib and Steamworks always match: LiteNetLib's OnConnectionRequest echoes back
        /// the persistent id the client supplied, and Steamworks keys on the SteamID. Riptide
        /// reassigns its id on reconnect and has nothing to match on - see the override in
        /// RiptideServer for the trade it is forced into.
        /// </summary>
        public virtual bool ClaimLoadingReconnect(ulong clientId)
        {
            if (!_loadingClients.Remove(clientId))
                return false;

            _reconnectedFromLoad.Add(clientId);
            return true;
        }

        /// <summary>
        /// HOST ONLY - release the longest-pending load entry and credit it to
        /// <paramref name="clientId"/>. Only for transports that cannot identify a returning
        /// client (Riptide); see the warning on <see cref="ClaimLoadingReconnect"/> for what
        /// this costs when the guess is wrong.
        /// </summary>
        protected void ClaimOldestLoadingReconnect(ulong clientId)
        {
            if (_loadingClients.Count == 0)
                return;

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
        }

        /// <summary>
        /// HOST ONLY - drop a client's pending load without crediting it as a returning
        /// loader. For a client that is not coming back: a kicked client would otherwise hold
        /// the gate until the timeout, and its own disconnect event cannot clear it (the kick
        /// has already removed the peer mapping that event is keyed on).
        /// </summary>
        public void ForgetClientLoading(ulong clientId)
        {
            _loadingClients.Remove(clientId);
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

            foreach (ulong id in _expiredLoadingClients)
                _loadingClients.Remove(id);
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
