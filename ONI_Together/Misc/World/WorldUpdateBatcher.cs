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
		private static float flushTimer = 0f;
		private const float FlushInterval = 10f; // Seconds

		public static void Queue(WorldUpdatePacket.CellUpdate update)
		{
			using var _ = Profiler.Scope();

			if(MultiplayerSession.IsClient)
			{
				// Client is not allowed to send WorldUpdate states as the host has full authority
				return;
			}

			lock (pendingUpdates)
			{
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
            bool isSteam = NetworkConfig.IsSteamConfig();

            // Max packet sizes (bytes)
            float maxPacketSize =
                isLan ? PacketSender.MAX_PACKET_SIZE_LAN * 1024 :
                isSteam ? PacketSender.MAX_PACKET_SIZE_UNRELIABLE :
                1024; // fallback

            const int PacketHeaderSize = 4;
            const float BytesPerUpdate = 5.38f; // Measured compressed size, this is a rough estimate

            lock (pendingUpdates)
            {
                if (pendingUpdates.Count == 0)
                    return 0;

                int totalUpdates = pendingUpdates.Count;

                List<WorldUpdatePacket.CellUpdate> currentBatch = new List<WorldUpdatePacket.CellUpdate>();
                float currentSize = PacketHeaderSize;

                for (int i = 0; i < pendingUpdates.Count; i++)
                {
                    var update = pendingUpdates[i];

                    // If adding this update would exceed packet size -> flush current batch
                    if (currentSize + BytesPerUpdate > maxPacketSize)
                    {
                        if (currentBatch.Count > 0)
                        {
                            var packet = new WorldUpdatePacket();
                            packet.Updates.AddRange(currentBatch);

                            PacketSender.SendToAllClients(packet, sendType: PacketSendMode.Unreliable);

                            currentBatch.Clear();
                            currentSize = PacketHeaderSize;
                        }
                    }

                    currentBatch.Add(update);
                    currentSize += BytesPerUpdate;
                }

                // Flush remaining
                if (currentBatch.Count > 0)
                {
                    var packet = new WorldUpdatePacket();
                    packet.Updates.AddRange(currentBatch);

                    PacketSender.SendToAllClients(packet, sendType: PacketSendMode.Unreliable);
                }

                pendingUpdates.Clear();

                return (int)(totalUpdates * BytesPerUpdate);
            }
        }

    }
}
