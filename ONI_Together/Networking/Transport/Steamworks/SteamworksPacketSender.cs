using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ClipperLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using Steamworks;

namespace ONI_Together.Networking.Transport.Steam
{
    public class SteamworksPacketSender : TransportPacketSender
    {
        private readonly Dictionary<uint, Queue<(HSteamNetConnection conn, byte[] bytes, int sendFlags, IPacket packet)>> _reliableRetryQueues = new();
        private readonly List<uint> _emptyRetryKeys = new();
        private const int MAX_RELIABLE_RETRY_QUEUE = 2048;

        public override bool SendPacket(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
        {
            using var _ = Profiler.Scope();

            if (conn is not HSteamNetConnection s_conn)
                return false;

            var bytes = PacketSender.SerializePacketForSending(packet);
            var _sendType = ConvertSendType(sendType);
            bool isReliable = (sendType & PacketSendMode.Reliable) == PacketSendMode.Reliable;

            // Preserve ordering for reliable packets when a connection already has queued retries.
            if (isReliable && _reliableRetryQueues.TryGetValue(s_conn.m_HSteamNetConnection, out var existingQueue) && existingQueue.Count > 0)
            {
                if (existingQueue.Count < MAX_RELIABLE_RETRY_QUEUE)
                {
                    existingQueue.Enqueue((s_conn, bytes, _sendType, packet));
                    return true;
                }
                DebugConsole.LogWarning($"[SteamworksPacketSender] Reliable retry queue full ({MAX_RELIABLE_RETRY_QUEUE}) for conn {s_conn.m_HSteamNetConnection}, dropping {packet.GetType().Name}");
                return false;
            }

            var result = SendRawPinned(s_conn, bytes, _sendType);
            if (result == EResult.k_EResultOK)
            {
                PacketTracker.TrackSent(new PacketTracker.PacketTrackData
                {
                    packet = packet,
                    size = bytes.Length
                });
                return true;
            }

            if (isReliable && result == EResult.k_EResultLimitExceeded)
            {
                if (!_reliableRetryQueues.TryGetValue(s_conn.m_HSteamNetConnection, out var queue))
                {
                    queue = new Queue<(HSteamNetConnection, byte[], int, IPacket)>();
                    _reliableRetryQueues[s_conn.m_HSteamNetConnection] = queue;
                }
                if (queue.Count < MAX_RELIABLE_RETRY_QUEUE)
                {
                    queue.Enqueue((s_conn, bytes, _sendType, packet));
                    return true;
                }
            }

            return false;
        }

        private static EResult SendRawPinned(HSteamNetConnection s_conn, byte[] bytes, int sendFlags)
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                return SteamNetworkingSockets.SendMessageToConnection(s_conn, ptr, (uint)bytes.Length, sendFlags, out _);
            }
            finally
            {
                handle.Free();
            }
        }

        public override void Flush()
        {
            base.Flush();

            if (_reliableRetryQueues.Count == 0)
                return;

            _emptyRetryKeys.Clear();
            foreach (var kvp in _reliableRetryQueues)
            {
                var queue = kvp.Value;
                while (queue.Count > 0)
                {
                    var item = queue.Peek();
                    var result = SendRawPinned(item.conn, item.bytes, item.sendFlags);
                    if (result == EResult.k_EResultOK)
                    {
                        queue.Dequeue();
                        PacketTracker.TrackSent(new PacketTracker.PacketTrackData
                        {
                            packet = item.packet,
                            size = item.bytes.Length
                        });
                    }
                    else if (result == EResult.k_EResultLimitExceeded)
                    {
                        // Buffer still full; try again next frame.
                        break;
                    }
                    else
                    {
                        // Connection closed or invalid; discard queued packets for this handle.
                        queue.Clear();
                        break;
                    }
                }

                if (queue.Count == 0)
                    _emptyRetryKeys.Add(kvp.Key);
            }

            for (int i = 0; i < _emptyRetryKeys.Count; i++)
                _reliableRetryQueues.Remove(_emptyRetryKeys[i]);
        }

        public int ConvertSendType(PacketSendMode mode)
        {
            int result = 0;

            // Reliable / Unreliable
            if ((mode & PacketSendMode.Reliable) == PacketSendMode.Reliable)
                result |= 8;  // k_nSteamNetworkingSend_Reliable
            else
                result |= 0;  // k_nSteamNetworkingSend_Unreliable (implicitly 0)

            // Immediate (flush) corresponds to NoNagle behavior
            if ((mode & PacketSendMode.Immediate) == PacketSendMode.Immediate)
                result |= 1;  // k_nSteamNetworkingSend_NoNagle

            // NoDelay (drop if can't send soon)
            if ((mode & PacketSendMode.NoDelay) == PacketSendMode.NoDelay)
                result |= 4;  // k_nSteamNetworkingSend_NoDelay

            return result;
        }
    }
}