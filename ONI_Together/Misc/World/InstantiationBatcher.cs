using ONI_Together.Networking;
using ONI_Together.Networking.Packets;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Misc.World
{
	public static class InstantiationBatcher
	{
		private static readonly List<InstantiationsPacket.InstantiationEntry> queue = new List<InstantiationsPacket.InstantiationEntry>();
		private static float timeSinceLastFlush = 0f;
		private const float FlushInterval = 2.0f;

		public static void ResetSessionState()
		{
			queue.Clear();
			timeSinceLastFlush = 0f;
		}

		public static void Queue(InstantiationsPacket.InstantiationEntry entry)
		{
			using var _ = Profiler.Scope();

			queue.Add(entry);
		}

		public static void Update()
		{
			using var _ = Profiler.Scope();

			timeSinceLastFlush += Time.unscaledDeltaTime;

			if (timeSinceLastFlush >= FlushInterval)
			{
				Flush();
				timeSinceLastFlush = 0f;
			}
		}

		public static void Flush()
		{
			using var _ = Profiler.Scope();

			if (queue.Count == 0)
				return;

			var packet = new InstantiationsPacket
			{
				Entries = new List<InstantiationsPacket.InstantiationEntry>(queue)
			};

			PacketSender.SendToAll(packet, sendType: PacketSendMode.Unreliable);
			queue.Clear();
		}
	}
}
