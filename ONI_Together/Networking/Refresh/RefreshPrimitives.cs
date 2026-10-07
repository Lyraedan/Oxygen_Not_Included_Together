using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ONI_Together.Networking.Refresh
{
    internal enum RefreshState : byte
    {
        Idle, Waiting, Accepted, Completed, Incomplete, Busy, Cooldown, Unavailable, Failed, Cancelled
    }

    internal enum RefreshRecordKind : byte { Terrain, Structure, Logic }

    internal static class RefreshLimits
    {
        public const int BatchBytes = 64 * 1024;
        public const int FragmentBytes = BatchBytes - 64;
        public const int RecordBytes = 4 * 1024 * 1024;
        public const double Timeout = 30;
        public const double Cooldown = 30;
    }

    internal readonly struct RefreshRegion
    {
        public readonly int X, Y, Width, Height;
        public RefreshRegion(int x, int y, int width, int height)
        { X = x; Y = y; Width = width; Height = height; }
    }

    internal static class RefreshLayout
    {
        public static IEnumerable<RefreshRegion> Regions(int width, int height)
        {
            for (int y = 0; y < height; y += 32)
                for (int x = 0; x < width; x += 32)
                    yield return new RefreshRegion(x, y, Math.Min(32, width - x), Math.Min(32, height - y));
        }
    }

    internal static class RefreshRouting
    {
        public static bool IsResponseFor(Guid activeRequest, ulong host, Guid responseRequest, ulong? sender)
            => activeRequest != Guid.Empty && responseRequest == activeRequest && sender.HasValue && sender.Value == host;
    }

    internal sealed class RefreshGate
    {
        private readonly Dictionary<ulong, double> lastRequests = new();
        public Guid RequestId { get; private set; }
        public ulong PlayerId { get; private set; }
        public bool Active => RequestId != Guid.Empty;
        public double LastProgress { get; private set; }
        public int AwaitingSequence { get; private set; } = -1;

        public RefreshState Begin(ulong player, Guid request, double now, out int retrySeconds)
        {
            retrySeconds = 0;
            if (request == Guid.Empty) return RefreshState.Unavailable;
            if (Active) return RefreshState.Busy;
            if (lastRequests.TryGetValue(player, out var last) && now - last < RefreshLimits.Cooldown)
            {
                retrySeconds = (int)Math.Ceiling(RefreshLimits.Cooldown - (now - last));
                return RefreshState.Cooldown;
            }
            lastRequests[player] = now;
            RequestId = request;
            PlayerId = player;
            LastProgress = now;
            return RefreshState.Accepted;
        }

        public void Sent(int sequence) { AwaitingSequence = sequence; }
        public void Progress(double now) { if (AwaitingSequence < 0) LastProgress = now; }
        public bool Acknowledge(ulong sender, Guid request, int sequence, double now)
        {
            if (!Active || sender != PlayerId || request != RequestId || sequence != AwaitingSequence || sequence < 0)
                return false;
            AwaitingSequence = -1;
            LastProgress = now;
            return true;
        }
        public bool Expired(double now) => Active && now - LastProgress >= RefreshLimits.Timeout;
        public void Finish() { RequestId = Guid.Empty; PlayerId = 0; AwaitingSequence = -1; }
        public void Reset() { Finish(); lastRequests.Clear(); }
    }

    internal sealed class RefreshRecordAssembler
    {
        private byte[] buffer;
        private int received;
        private RefreshRecordKind kind;
        public void Clear() { buffer = null; received = 0; }
        public byte[] Append(RefreshRecordKind recordKind, int total, int offset, byte[] fragment)
        {
            if (total <= 0 || total > RefreshLimits.RecordBytes || fragment == null || fragment.Length == 0
                || fragment.Length > RefreshLimits.FragmentBytes || offset < 0 || offset > total - fragment.Length)
                throw new InvalidDataException("Invalid refresh fragment");
            if (buffer == null)
            {
                if (offset != 0) throw new InvalidDataException("Missing first refresh fragment");
                buffer = new byte[total];
                kind = recordKind;
            }
            if (kind != recordKind || buffer.Length != total || offset != received)
                throw new InvalidDataException("Unexpected refresh fragment order");
            Buffer.BlockCopy(fragment, 0, buffer, offset, fragment.Length);
            received += fragment.Length;
            if (received != total) return null;
            var result = buffer;
            Clear();
            return result;
        }
    }

    internal sealed class RevisionCache<T>
    {
        private readonly Dictionary<T, long> revisions = new();
        public bool IsNewer(T key, long revision) => revision > 0 && (!revisions.TryGetValue(key, out var last) || revision > last);
        public void Record(T key, long revision) { revisions[key] = revision; }
        public void Clear() { revisions.Clear(); }
    }

    internal static class StateRevisions
    {
        private static long nextRevision;
        public static readonly RevisionCache<int> Terrain = new();
        public static long Next() => Interlocked.Increment(ref nextRevision);
    }
}
