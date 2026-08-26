using System;
using System.Collections.Generic;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace Shared.OxySync
{
    public class NetworkTransform : NetworkBehaviour
    {
        [SyncVar(Epsilon = 0.01f)]
        protected Vector3 _netPosition;

        [SyncVar(Epsilon = 0.01f)]
        protected Quaternion _netRotation;

        [SyncVar(Epsilon = 0.01f)]
        protected Vector3 _netScale;

        public bool syncPosition = true;
        public bool syncRotation = false;
        public bool syncScale = false;

        public bool interpolatePosition = true;
        public bool interpolateRotation = true;
        public bool interpolateScale = true;

        public enum CoordinateSpace { Local, World }
        public enum UpdateMethod { Update, FixedUpdate, LateUpdate }

        public Transform target;

        public CoordinateSpace coordinateSpace = CoordinateSpace.World;
        public UpdateMethod updateMethod = UpdateMethod.Update;

        public float snapThreshold = 1.5f;
        public float lerpSpeed = 15f;

        public bool useSnapshotInterpolation;
        public double bufferTimeMultiplier = 2.0;
		public double maxAdaptiveBufferMilliseconds = 350.0;

        private struct SnapshotEntry
        {
            public double timestamp;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private List<SnapshotEntry> _snapshots;
        private int _netPositionHash;
        private int _netRotationHash;
        private int _netScaleHash;
        private SnapshotTimeline _snapshotTimeline;
        private Vector3 _interpolatedPosition;
        private Quaternion _interpolatedRotation;
        private Vector3 _interpolatedScale;

        protected float _lastRequestTime;
        protected const float REQUEST_COOLDOWN = 0.5f;

        public override void OnSpawn()
        {
            base.OnSpawn();
            if (target == null) target = transform;
            _netPosition = target.position;
            _netRotation = target.rotation;
            _netScale = target.localScale;
            SyncInterval = 0.05f;
            _snapshots = new List<SnapshotEntry>(16);
            _snapshotTimeline = new SnapshotTimeline();
            _netPositionHash = OxySyncHash.Compute(nameof(_netPosition));
            _netRotationHash = OxySyncHash.Compute(nameof(_netRotation));
            _netScaleHash = OxySyncHash.Compute(nameof(_netScale));
        }

        public override bool ApplySyncVar(int fieldHash, object value, long timestamp)
        {
            bool isTransformField = fieldHash == _netPositionHash ||
                                    fieldHash == _netRotationHash ||
                                    fieldHash == _netScaleHash;

            if (!base.ApplySyncVar(fieldHash, value, timestamp))
                return false;

            if (!isTransformField || timestamp == 0)
                return true;

            if (!useSnapshotInterpolation)
                return true;

            double localTimestamp = _snapshotTimeline.ToLocalTime(
                timestamp,
                SnapshotTimeline.MonotonicMilliseconds);
            AddSnapshot(localTimestamp);
            return true;
        }

        private void AddSnapshot(double timestamp)
        {
            if (_snapshots.Count > 0)
            {
                int lastIndex = _snapshots.Count - 1;
                if (timestamp < _snapshots[lastIndex].timestamp)
                    return;

                // A batch gives position, rotation, and scale the same timestamp.
                // Replace the pending snapshot so it contains the complete batch,
                // rather than retaining only the first field that was applied.
                if (timestamp == _snapshots[lastIndex].timestamp)
                {
                    _snapshots[lastIndex] = new SnapshotEntry
                    {
                        timestamp = timestamp,
                        position = _netPosition,
                        rotation = _netRotation,
                        scale = _netScale,
                    };
                    return;
                }
            }

            _snapshots.Add(new SnapshotEntry
            {
                timestamp = timestamp,
                position = _netPosition,
                rotation = _netRotation,
                scale = _netScale,
            });

            while (_snapshots.Count > 16)
                _snapshots.RemoveAt(0);
        }

        private void Update()
        {
            if (updateMethod == UpdateMethod.Update)
                Poll();
        }

        private void FixedUpdate()
        {
            if (updateMethod == UpdateMethod.FixedUpdate)
                Poll();
        }

        private void LateUpdate()
        {
            if (updateMethod == UpdateMethod.LateUpdate)
                Poll();
        }

        private void Poll()
        {
            if (NetId == 0 || !inSession) return;

            if (isServer)
                ServerUpdate();
            else
                ClientUpdate();
        }

        [Server]
        protected virtual void ServerUpdate()
        {
            if (syncPosition)
            {
                _netPosition = coordinateSpace == CoordinateSpace.Local
                    ? target.localPosition
                    : target.position;
            }
            if (syncRotation)
            {
                _netRotation = coordinateSpace == CoordinateSpace.Local
                    ? target.localRotation
                    : target.rotation;
            }
            if (syncScale)
            {
                _netScale = coordinateSpace == CoordinateSpace.Local
                    ? target.localScale
                    : target.lossyScale;
            }
        }

        [Client]
        protected virtual void ClientUpdate()
        {
            bool applyClientState = ShouldApplyClientState();

            if (useSnapshotInterpolation && applyClientState)
            {
                UpdateInterpolation();
            }

            if (applyClientState && syncPosition)
            {
                Vector3 desired = useSnapshotInterpolation ? _interpolatedPosition : _netPosition;
                Vector3 currentPos = coordinateSpace == CoordinateSpace.Local
                    ? target.localPosition
                    : target.position;

                float dist = Vector3.Distance(currentPos, desired);

                if (dist > snapThreshold)
                {
                    if (coordinateSpace == CoordinateSpace.Local)
                        target.localPosition = desired;
                    else
                        target.position = desired;
                }
                else if (interpolatePosition && !useSnapshotInterpolation)
                {
                    Vector3 lerped = Vector3.Lerp(currentPos, desired, Mathf.Clamp01(lerpSpeed * Time.unscaledDeltaTime));
                    if (coordinateSpace == CoordinateSpace.Local)
                        target.localPosition = lerped;
                    else
                        target.position = lerped;
                }
                else if (coordinateSpace == CoordinateSpace.Local)
                {
                    target.localPosition = desired;
                }
                else
                {
                    target.position = desired;
                }
            }

            if (applyClientState && syncRotation)
            {
                Quaternion desired = useSnapshotInterpolation ? _interpolatedRotation : _netRotation;
                if (interpolateRotation && !useSnapshotInterpolation)
                {
                    Quaternion currentRot = coordinateSpace == CoordinateSpace.Local
                        ? target.localRotation
                        : target.rotation;
                    Quaternion slerped = Quaternion.Slerp(currentRot, desired, Mathf.Clamp01(lerpSpeed * Time.unscaledDeltaTime));
                    if (coordinateSpace == CoordinateSpace.Local)
                        target.localRotation = slerped;
                    else
                        target.rotation = slerped;
                }
                else if (coordinateSpace == CoordinateSpace.Local)
                {
                    target.localRotation = desired;
                }
                else
                {
                    target.rotation = desired;
                }
            }

            if (applyClientState && syncScale)
            {
                Vector3 desired = useSnapshotInterpolation ? _interpolatedScale : _netScale;
                if (interpolateScale && !useSnapshotInterpolation)
                {
                    Vector3 currentScale = coordinateSpace == CoordinateSpace.Local
                        ? target.localScale
                        : target.lossyScale;
                    Vector3 lerped = Vector3.Lerp(currentScale, desired, Mathf.Clamp01(lerpSpeed * Time.unscaledDeltaTime));
                    if (coordinateSpace == CoordinateSpace.Local)
                        target.localScale = lerped;
                }
                else if (coordinateSpace == CoordinateSpace.Local)
                {
                    target.localScale = desired;
                }
            }

            TryRequestPosition();
        }

		/// <summary>
		/// Allows a specialized client-side playback component to remain the sole
		/// writer of a transform while retaining OxySync's stale-state request path.
		/// Generic network transforms continue to use the normal implementation.
		/// </summary>
		protected virtual bool ShouldApplyClientState()
		{
			return true;
		}

        [Client]
        private void UpdateInterpolation()
        {
            PruneSnapshots();

            if (_snapshots.Count == 0)
            {
                _interpolatedPosition = _netPosition;
                _interpolatedRotation = _netRotation;
                _interpolatedScale = _netScale;
                return;
            }

            double now = SnapshotTimeline.MonotonicMilliseconds;
			double baseBufferMs = SyncInterval * bufferTimeMultiplier * 1000.0;
			double bufferMs = _snapshotTimeline.GetAdaptiveBufferMilliseconds(
				baseBufferMs, maxAdaptiveBufferMilliseconds);
            double playbackTime = now - bufferMs;

            int index = -1;
            for (int i = 0; i < _snapshots.Count - 1; i++)
            {
                if (_snapshots[i].timestamp <= playbackTime && _snapshots[i + 1].timestamp > playbackTime)
                {
                    index = i;
                    break;
                }
            }

            if (index == -1)
            {
                if (playbackTime < _snapshots[0].timestamp)
                {
                    _interpolatedPosition = _snapshots[0].position;
                    _interpolatedRotation = _snapshots[0].rotation;
                    _interpolatedScale = _snapshots[0].scale;
                }
                else
                {
                    _interpolatedPosition = _snapshots[_snapshots.Count - 1].position;
                    _interpolatedRotation = _snapshots[_snapshots.Count - 1].rotation;
                    _interpolatedScale = _snapshots[_snapshots.Count - 1].scale;
                }
            }
            else
            {
                var from = _snapshots[index];
                var to = _snapshots[index + 1];
                double t = (playbackTime - from.timestamp) / (to.timestamp - from.timestamp);
                t = Math.Clamp(t, 0.0, 1.0);
                float ft = (float)t;

                _interpolatedPosition = Vector3.Lerp(from.position, to.position, ft);
                _interpolatedRotation = Quaternion.Slerp(from.rotation, to.rotation, ft);
                _interpolatedScale = Vector3.Lerp(from.scale, to.scale, ft);
            }
        }

        private void PruneSnapshots()
        {
            double now = SnapshotTimeline.MonotonicMilliseconds;
            double retentionMs = Math.Max(1000.0, SyncInterval * bufferTimeMultiplier * 4.0 * 1000.0);
            double cutoff = now - retentionMs;

            while (_snapshots.Count > 2 && _snapshots[0].timestamp < cutoff)
                _snapshots.RemoveAt(0);
        }

        [Client]
        private void TryRequestPosition()
        {
            if (!ShouldRequestPosition()) return;

            if (Time.unscaledTime - _lastRequestTime < REQUEST_COOLDOWN)
                return;

            _lastRequestTime = Time.unscaledTime;
            CallCommand(nameof(CmdRequestPositionSync), LocalUserIdQuery?.Invoke() ?? 0);
        }

        protected virtual bool ShouldRequestPosition()
        {
            return false;
        }

        [Command]
        protected void CmdRequestPositionSync(ulong requesterId)
        {
            OnServerPositionRequest(requesterId);
        }

        protected virtual void OnServerPositionRequest(ulong requesterId)
        {
            CallTargetRpc(requesterId, nameof(TargetReceivePosition),
                target.position, target.rotation, target.localScale);
        }

        [TargetRpc]
        protected void TargetReceivePosition(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            _netPosition = position;
            _netRotation = rotation;
            _netScale = scale;
            OnPositionReceived(position, rotation, scale);
        }

        protected virtual void OnPositionReceived(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (syncPosition)
            {
                if (coordinateSpace == CoordinateSpace.Local)
                    target.localPosition = position;
                else
                    target.position = position;
            }
            if (syncRotation)
            {
                if (coordinateSpace == CoordinateSpace.Local)
                    target.localRotation = rotation;
                else
                    target.rotation = rotation;
            }
            if (syncScale)
            {
                if (coordinateSpace == CoordinateSpace.Local)
                    target.localScale = scale;
            }
            _lastRequestTime = Time.unscaledTime;
        }
    }
}
