using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Networking.Components
{
	internal class BulkPacketMonitor : MonoBehaviour
	{
		// Poll often enough for 100ms bulk packets while leaving pacing to PacketSender.
		private float updateIntervalSeconds = 0.1f;
		private float updateTimer;

		public void LateUpdate()
		{
			if (!MultiplayerSession.InSession)
				return;

			updateTimer += Time.unscaledDeltaTime;

			if (updateTimer < updateIntervalSeconds)
			{
				return;
			}
			updateTimer -= updateIntervalSeconds;
			PacketSender.DispatchPendingBulkPackets();
		}
	}
}
