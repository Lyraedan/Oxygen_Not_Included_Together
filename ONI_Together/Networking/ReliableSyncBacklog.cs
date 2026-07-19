using System.Collections.Generic;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;

namespace ONI_Together.Networking
{
	internal enum SyncBacklogResult
	{
		NotBuffered,
		Buffered,
		Overflow,
		Terminated
	}

	internal static class ReliableSyncBacklog
	{
		private const int MaxEntriesPerClient = DeferredReliableBatchPacket.MaxFrames;
		private const int MaxBytesPerClient = ReliablePageChannel.MaxQueuedBytes;
		private const int EmptyBatchCost = sizeof(int) * 4;

		private sealed class ClientBacklog
		{
			public readonly Queue<byte[]> Packets = new();
			public int Bytes = EmptyBatchCost;
			public bool Overflowed;
			public bool Replaying;
			public int ReplayCount;
			public MultiplayerPlayer ReplayPlayer;
			public System.Action<bool> Completion;
		}

		private static readonly Dictionary<ulong, ClientBacklog> Clients = new();

		internal static void Begin(ulong clientId)
		{
			if (Clients.TryGetValue(clientId, out ClientBacklog current) && current.Replaying)
			{
				AbortReplay(clientId, current);
				ReadyManager.CancelPendingReadyCommit(clientId);
				return;
			}
			Clients[clientId] = new ClientBacklog();
		}

		internal static SyncBacklogResult TryBuffer(ulong clientId, IPacket packet, PacketSendMode sendMode)
		{
			if (!Clients.TryGetValue(clientId, out ClientBacklog backlog)
			    || (sendMode & PacketSendMode.Reliable) == 0)
				return SyncBacklogResult.NotBuffered;
			if (backlog.Overflowed)
				return SyncBacklogResult.Terminated;

			try
			{
				byte[] payload = PacketSender.SerializePacketForSending(packet);
				int cost = checked(sizeof(int) + payload.Length);
				if (packet is DeferredReliablePacket or DeferredReliableBatchPacket
				    || backlog.Packets.Count >= MaxEntriesPerClient
				    || cost > MaxBytesPerClient - backlog.Bytes)
				{
					backlog.Overflowed = true;
					return SyncBacklogResult.Overflow;
				}

				backlog.Packets.Enqueue(payload);
				backlog.Bytes += cost;
				return SyncBacklogResult.Buffered;
			}
			catch (System.Exception ex)
			{
				backlog.Overflowed = true;
				DebugConsole.LogWarning($"[SyncBacklog] Failed to snapshot {packet.GetType().Name}: {ex.Message}");
				return SyncBacklogResult.Overflow;
			}
		}

		internal static void BufferForDisconnectedClients(
			IPacket packet,
			PacketSendMode sendMode,
			System.Func<ulong, bool> isExcluded)
		{
			foreach (ulong clientId in new List<ulong>(Clients.Keys))
			{
				if (MultiplayerSession.ConnectedPlayers.ContainsKey(clientId)
				    || isExcluded != null && isExcluded(clientId))
					continue;

					if (TryBuffer(clientId, packet, sendMode) != SyncBacklogResult.Overflow)
						continue;
					DebugConsole.LogWarning($"[SyncBacklog] Disconnected client {clientId} exceeded its delta journal");
					ReadyManager.PrepareFreshSnapshot(clientId);
			}
		}

		internal static bool Replay(MultiplayerPlayer player, System.Action<bool> completion)
		{
			if (player == null || player.Connection == null
			    || !Clients.TryGetValue(player.PlayerId, out ClientBacklog backlog)
			    || backlog.Overflowed || backlog.Replaying || completion == null)
				return false;

			backlog.Replaying = true;
			backlog.ReplayPlayer = player;
			backlog.Completion = completion;
			return SendNextBatch(player.PlayerId, backlog);
		}

		private static bool SendNextBatch(ulong clientId, ClientBacklog backlog)
		{
			if (backlog.Packets.Count == 0)
			{
				Clients.Remove(clientId);
				System.Action<bool> completed = backlog.Completion;
				backlog.Completion = null;
				completed?.Invoke(true);
				return true;
			}

			byte[][] frames = backlog.Packets.ToArray();
			backlog.ReplayCount = frames.Length;
			var batch = new DeferredReliableBatchPacket(frames);
			bool accepted = PacketSender.SendReliableWithCompletion(
				backlog.ReplayPlayer.Connection,
				batch,
				succeeded => CompleteBatch(clientId, backlog, succeeded));
			if (accepted)
				return true;
			backlog.Replaying = false;
			System.Action<bool> completion = backlog.Completion;
			backlog.Completion = null;
			completion?.Invoke(false);
			return false;
		}

		private static void CompleteBatch(
			ulong clientId, ClientBacklog backlog, bool succeeded)
		{
			if (!Clients.TryGetValue(clientId, out ClientBacklog current)
			    || !ReferenceEquals(current, backlog))
				return;
			if (!succeeded)
			{
				backlog.Replaying = false;
				System.Action<bool> failed = backlog.Completion;
				backlog.Completion = null;
				failed?.Invoke(false);
				return;
			}

			for (int index = 0; index < backlog.ReplayCount; index++)
			{
				byte[] frame = backlog.Packets.Dequeue();
				backlog.Bytes -= sizeof(int) + frame.Length;
			}
			backlog.ReplayCount = 0;
			SendNextBatch(clientId, backlog);
		}

		internal static bool Transfer(ulong oldClientId, ulong newClientId)
		{
			if (!CanTransfer(oldClientId, newClientId))
				return false;
			if (oldClientId == newClientId)
				return true;

			ClientBacklog backlog = Clients[oldClientId];
			Clients.Remove(oldClientId);
			Clients.Add(newClientId, backlog);
			return true;
		}

		internal static bool CanTransfer(ulong oldClientId, ulong newClientId)
			=> Clients.TryGetValue(oldClientId, out ClientBacklog backlog)
			   && !backlog.Overflowed
			   && !backlog.Replaying
			   && (oldClientId == newClientId || !Clients.ContainsKey(newClientId));

		internal static void Prune(System.Func<ulong, bool> keep)
		{
			foreach (ulong clientId in new List<ulong>(Clients.Keys))
			{
				if (!keep(clientId))
					Clients.Remove(clientId);
			}
		}

		internal static void Clear(ulong clientId)
		{
			if (Clients.TryGetValue(clientId, out ClientBacklog backlog) && backlog.Replaying)
				AbortReplay(clientId, backlog);
			else
				Clients.Remove(clientId);
			ReadyManager.CancelPendingReadyCommit(clientId);
		}

		private static void AbortReplay(ulong clientId, ClientBacklog backlog)
		{
			Clients.Remove(clientId);
			object connection = backlog.ReplayPlayer?.Connection;
			if (connection != null)
				PacketSender.DropConnection(connection);
			if (MultiplayerSession.IsHost)
				NetworkConfig.TransportServer?.KickClient(clientId);
			backlog.Completion = null;
		}

		internal static void ClearAll()
		{
			Clients.Clear();
			ReadyManager.CancelAllPendingReadyCommits();
		}
		internal static int CountForTests(ulong clientId)
			=> Clients.TryGetValue(clientId, out ClientBacklog backlog) ? backlog.Packets.Count : 0;

		internal static bool IsReplayingForTests(ulong clientId)
			=> Clients.TryGetValue(clientId, out ClientBacklog backlog) && backlog.Replaying;
	}
}
