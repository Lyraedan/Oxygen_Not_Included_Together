using System;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	public class EntityPositionHandler : KMonoBehaviour
	{
        [MyCmpGet] KBatchedAnimController kbac;
        [MyCmpGet] Navigator navigator;

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
                return;
			}

			// Skip if no clients connected
			if (MultiplayerSession.ConnectedPlayers.Count == 0)
				return;

			SendPositionUpdate();
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

		        if (Vector3.Distance(currentPosition, lastSentPosition) < PositionThreshold)
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
