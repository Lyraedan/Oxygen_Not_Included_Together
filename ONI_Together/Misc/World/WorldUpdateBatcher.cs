using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using System;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Misc.World
{
	public static class WorldUpdateBatcher
	{
		private static readonly List<WorldUpdatePacket.CellUpdate> pendingUpdates = new List<WorldUpdatePacket.CellUpdate>();
		private static readonly List<WorldUpdatePacket.CellUpdate> pendingReliableUpdates = new List<WorldUpdatePacket.CellUpdate>();
		private static float flushTimer = 0f;
		private const float FlushInterval = 10f; // Seconds

		// Conservative per-packet caps so Deflate-compressed UDP unreliable payloads stay under Steam MTU (~1200B)
		private const int MAX_UNRELIABLE_UPDATES_STEAM = 60;
		private const int MAX_UNRELIABLE_UPDATES_LAN = 32;
		private const int MAX_RELIABLE_UPDATES_PER_PACKET = 128;

		public static void Queue(WorldUpdatePacket.CellUpdate update, bool reliable = false)
		{
			using var _ = Profiler.Scope();

			if(MultiplayerSession.IsClient)
			{
				// Client is not allowed to send WorldUpdate states as the host has full authority
				return;
			}

			lock (pendingUpdates)
			{
				if (reliable)
					pendingReliableUpdates.Add(update);
				else
					pendingUpdates.Add(update);
			}
		}

		public static void Update()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsClient)
			{
				return;
			}

			flushTimer += Time.unscaledDeltaTime;
			if (flushTimer >= FlushInterval)
			{
				Flush();
				flushTimer = 0f;
			}
		}

        public static int Flush()
        {
	        using var _ = Profiler.Scope();

            if (MultiplayerSession.IsClient)
            {
                return 0;
            }

            bool isLan = NetworkConfig.IsLanConfig();
            int maxUnreliablePerPacket = isLan ? MAX_UNRELIABLE_UPDATES_LAN : MAX_UNRELIABLE_UPDATES_STEAM;
            int maxReliablePerPacket = isLan ? MAX_UNRELIABLE_UPDATES_LAN : MAX_RELIABLE_UPDATES_PER_PACKET;
            const float EstimatedBytesPerUpdate = 12f;

            lock (pendingUpdates)
            {
                int totalUpdates = pendingUpdates.Count + pendingReliableUpdates.Count;
                if (totalUpdates == 0)
                    return 0;

                // 1. Flush critical element-change updates reliably so dug tiles / phase changes never desync
                if (pendingReliableUpdates.Count > 0)
                {
                    for (int i = 0; i < pendingReliableUpdates.Count; i += maxReliablePerPacket)
                    {
                        int count = Math.Min(maxReliablePerPacket, pendingReliableUpdates.Count - i);
                        var packet = new WorldUpdatePacket();
                        for (int j = 0; j < count; j++)
                            packet.Updates.Add(pendingReliableUpdates[i + j]);
                        PacketSender.SendToAllClients(packet, sendType: PacketSendMode.Reliable);
                    }
                    pendingReliableUpdates.Clear();
                }

                // 2. Flush mass/temperature drift updates unreliably in MTU-safe batches
                if (pendingUpdates.Count > 0)
                {
                    for (int i = 0; i < pendingUpdates.Count; i += maxUnreliablePerPacket)
                    {
                        int count = Math.Min(maxUnreliablePerPacket, pendingUpdates.Count - i);
                        var packet = new WorldUpdatePacket();
                        for (int j = 0; j < count; j++)
                            packet.Updates.Add(pendingUpdates[i + j]);
                        PacketSender.SendToAllClients(packet, sendType: PacketSendMode.Unreliable);
                    }
                    pendingUpdates.Clear();
                }

                return (int)(totalUpdates * EstimatedBytesPerUpdate);
            }
        }

    }
}
