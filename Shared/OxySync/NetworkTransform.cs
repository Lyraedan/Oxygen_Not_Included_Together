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

        private struct SnapshotEntry
        {
            public long timestamp;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private List<SnapshotEntry> _snapshots;
        private int _netPositionHash;
        private int _netRotationHash;
        private int _netScaleHash;
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
            _netPositionHash = nameof(_netPosition).GetHashCode();
            _netRotationHash = nameof(_netRotation).GetHashCode();
            _netScaleHash = nameof(_netScale).GetHashCode();
        }

        public override void ApplySyncVar(int fieldHash, object value, long timestamp)
        {
            base.ApplySyncVar(fieldHash, value, timestamp);

            if (!useSnapshotInterpolation || timestamp == 0) return;

            if (fieldHash == _netPositionHash || fieldHash == _netRotationHash || fieldHash == _netScaleHash)
            {
                AddSnapshot(timestamp);
            }
        }

        private void AddSnapshot(long timestamp)
        {
            if (_snapshots.Count > 0 && _snapshots[_snapshots.Count - 1].timestamp == timestamp)
                return;

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
            if (useSnapshotInterpolation)
            {
                UpdateInterpolation();
            }

            if (syncPosition)
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

            if (syncRotation)
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

            if (syncScale)
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

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long bufferMs = (long)(SyncInterval * bufferTimeMultiplier * 1000);
            long playbackTime = now - bufferMs;

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
                double t = (double)(playbackTime - from.timestamp) / (to.timestamp - from.timestamp);
                t = Math.Clamp(t, 0.0, 1.0);
                float ft = (float)t;

                _interpolatedPosition = Vector3.Lerp(from.position, to.position, ft);
                _interpolatedRotation = Quaternion.Slerp(from.rotation, to.rotation, ft);
                _interpolatedScale = Vector3.Lerp(from.scale, to.scale, ft);
            }
        }

        private void PruneSnapshots()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long cutoff = now - (long)(SyncInterval * bufferTimeMultiplier * 2 * 1000);

            while (_snapshots.Count > 0 && _snapshots[0].timestamp < cutoff)
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
            CallTargetRpc(requesterId, nameof(RpcReceivePosition),
                target.position, target.rotation, target.localScale);
        }

        [TargetRpc]
        protected void RpcReceivePosition(Vector3 position, Quaternion rotation, Vector3 scale)
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
