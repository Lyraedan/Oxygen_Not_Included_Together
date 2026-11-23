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
		private long _lastRemoteTimestampMs;
		private double _lastLocalArrivalMs;
		private double _arrivalJitterMs;

		public double EstimatedJitterMilliseconds => _arrivalJitterMs;

        public static double MonotonicMilliseconds =>
            Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

        public double ToLocalTime(long remoteTimestampMs, double localArrivalTimeMs)
        {
            if (!_hasAnchor)
            {
                _hasAnchor = true;
                _remoteAnchorMs = remoteTimestampMs;
                _localAnchorMs = localArrivalTimeMs;
				_lastRemoteTimestampMs = remoteTimestampMs;
				_lastLocalArrivalMs = localArrivalTimeMs;
			}
			else if (remoteTimestampMs > _lastRemoteTimestampMs)
			{
				double remoteDelta = remoteTimestampMs - _lastRemoteTimestampMs;
				double arrivalDelta = localArrivalTimeMs - _lastLocalArrivalMs;
				if (arrivalDelta >= 0.0)
				{
					double deviation = System.Math.Abs(arrivalDelta - remoteDelta);
					// A responsive EWMA adapts within a few snapshots, while avoiding
					// a single delayed packet permanently increasing input latency.
					_arrivalJitterMs += (deviation - _arrivalJitterMs) * 0.25;
				}

				_lastRemoteTimestampMs = remoteTimestampMs;
				_lastLocalArrivalMs = localArrivalTimeMs;
            }
			// Reliable and unreliable channels may be delivered out of order. An
			// older packet still maps correctly through the original anchor, but it
			// must not move the jitter estimator backwards and poison the following
			// sample's arrival delta.

            return _localAnchorMs + (remoteTimestampMs - _remoteAnchorMs);
        }

		public double GetAdaptiveBufferMilliseconds(double baseBufferMs, double maxBufferMs)
		{
			double adaptive = baseBufferMs + _arrivalJitterMs * 2.0;
			return System.Math.Max(baseBufferMs, System.Math.Min(adaptive, maxBufferMs));
		}

        public void Reset()
        {
            _hasAnchor = false;
            _remoteAnchorMs = 0;
            _localAnchorMs = 0;
			_lastRemoteTimestampMs = 0;
			_lastLocalArrivalMs = 0;
			_arrivalJitterMs = 0;
        }
    }
}
