using System;
using System.Threading;
using ONI_Together.Networking.Packets.Core;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	public class EntityPositionHandler : KMonoBehaviour
	{
		private static long _nextHostSequence;
#if DEBUG
		private static bool _checkpointFrozen;
#endif
        [MyCmpGet] public KBatchedAnimController kbac;
        [MyCmpGet] public Navigator navigator;

        private Vector3 lastSentPosition;
		private float lastSendTime;

		private const float PositionThreshold = 0.05f;
		private const float MIN_DT = 0.016f;

        public Vector3 serverPosition;
        public long serverTimestamp;
        public bool serverFlipX;
        public bool serverFlipY;
        public NavType serverNavType;

        private const float SNAP_DISTANCE = 1.5f;
        private const float LERP_SPEED = 20f;

        private float _lastRequestTime;
        private float _lastServerUpdateTime;
        private const float REQUEST_COOLDOWN = 0.5f;
        private const float STALE_THRESHOLD = 2f;
		private const float HEARTBEAT_INTERVAL = 1f;

        public override void OnSpawn()
		{
			using var _ = Profiler.Scope();
			base.OnSpawn();

			lastSentPosition = transform.position;
			lastSendTime = Time.unscaledTime;
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (this.GetNetId() == 0)
				return;

			if (!MultiplayerSession.InSession)
				return;

			if (MultiplayerSession.IsClient)
			{
				UpdatePosition();
				TryRequestEntityPosition();
                return;
			}

			// Skip if no clients connected
			if (MultiplayerSession.ConnectedPlayers.Count == 0)
				return;

#if DEBUG
			if (_checkpointFrozen)
				return;
#endif

			SendPositionUpdate();
		}

		internal static long NextHostSequence()
		{
			long sequence = Interlocked.Increment(ref _nextHostSequence);
			if (sequence > 0)
				return sequence;
			Interlocked.Exchange(ref _nextHostSequence, 1);
			return 1;
		}

		internal static void ResetSessionState()
		{
			Interlocked.Exchange(ref _nextHostSequence, 0);
#if DEBUG
			_checkpointFrozen = false;
#endif
		}

#if DEBUG
		internal static void SetCheckpointFrozen(bool frozen) => _checkpointFrozen = frozen;
		internal static bool CheckpointFrozen => _checkpointFrozen;

		internal static Vector3 SelectHashPosition(
			bool localIsClient, Vector3 transformPosition,
			long authoritativeSequence, Vector3 authoritativePosition)
			=> localIsClient && authoritativeSequence > 0
				? authoritativePosition
				: transformPosition;
#endif

        private void TryRequestEntityPosition()
        {
			if (!GameClient.CanSendRuntimeRequests(GameClient.State))
				return;

			float now = Time.unscaledTime;
			if (now - _lastRequestTime <= REQUEST_COOLDOWN)
				return;

			bool isVisible = false;
			if (WorldStateSyncer.TryGetLocalViewport(out var viewport))
			{
				int cell = Grid.PosToCell(transform.position);
				isVisible = Grid.IsValidCell(cell) && WorldStateSyncer.IsCellInRect(cell, viewport);
			}

			if (!ShouldRequestServerState(serverTimestamp, isVisible, _lastServerUpdateTime, now))
				return;

			// Throttle attempts even if the transport drops between the state gate and send.
			_lastRequestTime = now;
			PacketSender.SendToHost(new EntityPositionRequestPacket { NetId = this.GetNetId() });
        }

		internal static bool ShouldRequestServerState(
			long timestamp, bool isVisible, float lastUpdateTime, float now)
			=> timestamp == 0 || isVisible && IsServerStateStale(timestamp, lastUpdateTime, now);

        internal static bool IsServerStateStale(long timestamp, float lastUpdateTime, float now)
        {
            return timestamp == 0 || now - lastUpdateTime > STALE_THRESHOLD;
        }

        internal void MarkServerUpdateReceived()
        {
            _lastServerUpdateTime = Time.unscaledTime;
        }

        private void SendPositionUpdate()
        {
	        using var _ = Profiler.Scope();

	        try
	        {
		        Vector3 currentPosition = transform.position;
		        float currentTime = Time.unscaledTime;

                if (currentTime - lastSendTime < MIN_DT)
			        return;

                bool moved = Vector3.Distance(currentPosition, lastSentPosition) >= PositionThreshold;
                bool heartbeatDue = currentTime - lastSendTime >= HEARTBEAT_INTERVAL;
                if (!moved && !heartbeatDue)
			        return;

		        NavType navType = NavType.Floor;
		        if (navigator != null && navigator.CurrentNavType != NavType.NumNavTypes)
			        navType = navigator.CurrentNavType;

		        var packet = new EntityPositionPacket
		        {
			        NetId = this.GetNetId(),
			        Position = currentPosition,
			        FlipX = kbac != null && kbac.FlipX,
			        FlipY = kbac != null && kbac.FlipY,
			        NavType = navType,
				        Timestamp = NextHostSequence()
		        };

		        PacketSender.SendToAllClients(packet, sendType: PacketSendMode.Unreliable);

		        lastSentPosition = currentPosition;
		        lastSendTime = currentTime;
	        }
	        catch (Exception)
	        {
	        }
        }

        private void UpdatePosition()
        {
	        using var _ = Profiler.Scope();

            if (serverTimestamp == 0)
                return;

            if (kbac != null)
            {
	            kbac.FlipX = serverFlipX;
	            kbac.FlipY = serverFlipY;
            }

            if (navigator != null && navigator.CurrentNavType != serverNavType)
	            navigator.SetCurrentNavType(serverNavType);

            Vector3 currentPos = transform.position;
            float error = Vector3.Distance(currentPos, serverPosition);

            if (error > SNAP_DISTANCE)
            {
                transform.SetPosition(serverPosition);
                return;
            }

            float t = Mathf.Clamp01(LERP_SPEED * Time.unscaledDeltaTime);
            transform.SetPosition(Vector3.Lerp(currentPos, serverPosition, t));
        }
	}
}
