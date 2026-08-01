using System.Diagnostics;

namespace Shared.OxySync
{
    /// <summary>
    /// Maps remote packet timestamps onto the receiver's monotonic clock.
    /// This keeps snapshot interpolation independent from system clock differences
    /// between the host and client.
    /// </summary>
    public sealed class SnapshotTimeline
    {
        private bool _hasAnchor;
        private long _remoteAnchorMs;
        private double _localAnchorMs;

        public static double MonotonicMilliseconds =>
            Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

        public double ToLocalTime(long remoteTimestampMs, double localArrivalTimeMs)
        {
            if (!_hasAnchor)
            {
                _hasAnchor = true;
                _remoteAnchorMs = remoteTimestampMs;
                _localAnchorMs = localArrivalTimeMs;
            }

            return _localAnchorMs + (remoteTimestampMs - _remoteAnchorMs);
        }

        public void Reset()
        {
            _hasAnchor = false;
            _remoteAnchorMs = 0;
            _localAnchorMs = 0;
        }
    }
}
