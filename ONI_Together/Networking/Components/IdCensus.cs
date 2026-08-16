using System.Collections.Generic;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Core;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
    /// <summary>
    /// Ask continuously, and for every kind of object at once, whether the client has what
    /// the host has. See IdCensusPacket for why this is worth a packet.
    ///
    /// The host walks its own NetworkIdentity objects in batches and the client looks each
    /// id up. An id missing on one pass is probably in flight; an id missing on two
    /// consecutive passes is gone, and only the second is reported.
    ///
    /// What it cannot see, stated so a zero is not read as good news:
    /// - objects the host never gave an id. Nothing without an id can appear in a census
    ///   of ids.
    /// - objects the client has and the host does not. The traffic goes one way.
    /// - an object the client holds under a DIFFERENT id. This walks ids, so "present
    ///   under another number" and "absent" look the same from here. Telling those apart
    ///   needs cell and prefab on the wire, which costs several times the bandwidth; worth
    ///   doing only if the count below ever justifies it.
    /// - anything while the client is disconnected, which is when a reconnect is most
    ///   likely to have emptied the registry.
    /// </summary>
    public class IdCensus : MonoBehaviour
    {
        /// <summary>
        /// Four batches a second, forty ids each.
        ///
        /// Sized so the interesting counter can actually fire. At one batch a second a pass
        /// over ten thousand objects takes four minutes and the two-pass count needs eight,
        /// which is longer than many sessions spend settled - the counter would read zero
        /// and could not read anything else. At four a second a pass is about a minute.
        ///
        /// Forty per packet keeps the payload near 160 bytes, well inside the 1000-byte
        /// limit, so this never reaches the chunking path.
        /// </summary>
        private const float SendInterval = 0.25f;
        private const int BatchSize = 40;

        /// <summary>
        /// Nothing for the first half minute of a session. A join spawns everything at once
        /// and the two peers are legitimately out of step while that lands.
        /// </summary>
        private const float StartDelay = 30f;

        private float _next;
        private float _startedAt;
        private bool _started;

        /// <summary>
        /// The ids to walk, snapshotted per pass rather than re-read per batch: the world
        /// changes while the walk is in progress, and a moving list would silently skip
        /// entries - the one failure an instrument built to find missing things cannot have.
        /// </summary>
        private List<int> _snapshot;
        private int _position;
        private int _cycle;

        // --- client side ---

        private static readonly HashSet<int> _missingThisCycle = new HashSet<int>();
        private static readonly HashSet<int> _missingLastCycle = new HashSet<int>();
        private static int _cycleSeen = -1;

        /// <summary>Ids the host offered and this peer looked up.</summary>
        public static int Checked { get; private set; }

        /// <summary>Ids missing at the moment they were offered. Includes things in flight.</summary>
        public static int MissingNow { get; private set; }

        /// <summary>
        /// Ids missing on two consecutive passes. This is the number that means something -
        /// a pass is about a minute, and nothing legitimately in flight survives that long.
        /// </summary>
        public static int MissingPersistent { get; private set; }

        /// <summary>
        /// Passes completed, so a zero above can be told from "it never ran". Read these
        /// two together; a zero with no completed passes says nothing at all.
        /// </summary>
        public static int CyclesCompleted { get; private set; }

        public static void Reset()
        {
            _missingThisCycle.Clear();
            _missingLastCycle.Clear();
            _cycleSeen = -1;
            Checked = 0;
            MissingNow = 0;
            MissingPersistent = 0;
            CyclesCompleted = 0;
        }

        private void Update()
        {
            if (!MultiplayerSession.SessionHasPlayers || !MultiplayerSession.IsHost) return;
            if (Game.Instance == null) return;

            if (!_started)
            {
                _started = true;
                _startedAt = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - _startedAt < StartDelay) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + SendInterval;

            if (_snapshot == null || _position >= _snapshot.Count)
            {
                // The world rather than the registry: this asks what the host actually
                // holds, and a registry that has quietly lost an entry is one of the things
                // worth catching rather than trusting.
                _snapshot = new List<int>();
                foreach (var identity in Object.FindObjectsByType<NetworkIdentity>(
                             FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (identity.IsNullOrDestroyed() || identity.NetId == 0) continue;
                    _snapshot.Add(identity.NetId);
                }
                _position = 0;
                _cycle++;
            }

            int take = Mathf.Min(BatchSize, _snapshot.Count - _position);
            if (take <= 0) return;

            var batch = new int[take];
            _snapshot.CopyTo(_position, batch, 0, take);
            _position += take;

            PacketSender.SendToAllClients(
                new IdCensusPacket { Cycle = _cycle, NetIds = batch },
                PacketSendMode.Reliable);
        }

        /// <summary>Client side: look each id up, and remember what was not there.</summary>
        internal static void Receive(int cycle, int[] netIds)
        {
            if (netIds == null) return;

            if (cycle != _cycleSeen)
            {
                if (_cycleSeen >= 0)
                {
                    CyclesCompleted++;
                    _missingLastCycle.Clear();
                    foreach (int id in _missingThisCycle) _missingLastCycle.Add(id);
                }
                _missingThisCycle.Clear();
                _cycleSeen = cycle;
            }

            foreach (int id in netIds)
            {
                if (id == 0) continue;
                Checked++;

                if (NetworkIdentityRegistry.TryGet(id, out var identity) && !identity.IsNullOrDestroyed())
                    continue;

                _missingThisCycle.Add(id);
                MissingNow++;

                if (!_missingLastCycle.Contains(id)) continue;

                MissingPersistent++;

                // Only the first few, and only for absences that have survived a pass.
                // A per-id line for everything would be the whole log; the counter is the
                // measurement and these are for finding out what one of them was.
                if (MissingPersistent <= 10)
                {
                    DebugConsole.LogWarning(
                        $"[Census] the host holds NetId {id} and this peer does not, on two " +
                        $"consecutive passes - grep the host log for that id to see what it is " +
                        $"({MissingPersistent} so far, {Checked} checked)");
                }
            }
        }
    }
}
