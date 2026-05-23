using System;
using ONI_Together.Networking.Packets.Core;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	public class EntityPositionHandler : KMonoBehaviour
	{
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
                // Only do this if this entity is NOT visible by the host but is visible by the client
                TryRequestEntityPositionIfVisible();
                return;
			}

			// Skip if no clients connected
			if (MultiplayerSession.ConnectedPlayers.Count == 0)
				return;

			SendPositionUpdate();
		}

        private void TryRequestEntityPositionIfVisible()
        {
            if (WorldStateSyncer.TryGetLocalViewport(out var viewport))
            {
                int cell = Grid.PosToCell(transform.position);
                if (WorldStateSyncer.IsCellInRect(cell, viewport) && Time.unscaledTime - _lastRequestTime > REQUEST_COOLDOWN)
                {
                    // serverTimestamp is stale or we've never heard from the host
                    if (serverTimestamp == 0 || Time.unscaledTime - (serverTimestamp / 1000f) > STALE_THRESHOLD)
                    {
                        PacketSender.SendToHost(new EntityPositionRequestPacket { NetId = this.GetNetId() });
                        _lastRequestTime = Time.unscaledTime;
                    }
                }
            }
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
			        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
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
