using ONI_MP.DebugTools;
using ONI_MP.Menus;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.World;
using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Misc.World
{
	public static class SaveChunkAssembler
	{
		public static bool isDownloading = false;

		private class InProgressSave
		{
			public byte[] Data;
			public int ChunkSize;
			public int TotalChunks;
			public bool[] ChunkReceived;     // List of received chunks [true,false,true...]
			public System.DateTime LastCheckTime = System.DateTime.MinValue;
			public System.DateTime LastChunkReceived = System.DateTime.Now; // When last chunk was received
			public int MissingChunkRequestCount = 0;
			public int LastReportedProgress = -1;  // Last percentage sent to host

			public InProgressSave(int totalSize, int chunkSize)
			{
				using var _ = Profiler.Scope();

				ChunkSize = chunkSize;
				TotalChunks = (int)Math.Ceiling((double)totalSize / chunkSize);
				Data = new byte[totalSize];
				ChunkReceived = new bool[TotalChunks];
			}

			public int GetReceivedChunks()
			{
				using var _ = Profiler.Scope();

				int count = 0;
				for (int i = 0; i < ChunkReceived.Length; i++)
				{
					if (ChunkReceived[i]) count++;
				}
				return count;
			}

			public List<int> GetMissingChunks()
			{
				using var _ = Profiler.Scope();

				var missing = new List<int>();
				for (int i = 0; i < ChunkReceived.Length; i++)
				{
					if (!ChunkReceived[i]) missing.Add(i);
				}
				return missing;
			}

			public bool IsComplete()
			{
				using var _ = Profiler.Scope();

				for (int i = 0; i < ChunkReceived.Length; i++)
				{
					if (!ChunkReceived[i]) return false;
				}
				return true;
			}

			// Checks how many sequential chunks we have from the beginning
			public int GetMaxContiguousChunks()
			{
				using var _ = Profiler.Scope();

				for (int i = 0; i < ChunkReceived.Length; i++)
				{
					if (!ChunkReceived[i]) return i; // Stops at first gap
				}
				return ChunkReceived.Length; // All sequential
			}
		}

		private static readonly Dictionary<string, InProgressSave> InProgress = new Dictionary<string, InProgressSave>();

		public static void ReceiveChunk(SaveFileChunkPacket chunk)
		{
			using var _ = Profiler.Scope();

			if (!InProgress.TryGetValue(chunk.FileName, out var save))
			{
				// Determine chunk size from first received chunk
				int chunkSize = chunk.Chunk.Length;
				save = new InProgressSave(chunk.TotalSize, chunkSize);
				InProgress[chunk.FileName] = save;
				DebugConsole.Log($"[ChunkAssembler] Starting download of '{chunk.FileName}' ({Utils.FormatBytes(chunk.TotalSize)}) in {save.TotalChunks} chunks");

				// Send initial progress (0%) to host
				SendProgressToHost(chunk.FileName, 0, save.TotalChunks, 0);
				save.LastReportedProgress = 0;

				// Show initial progress bar (0%) to client
				string initialProgressBar = CreateClientProgressBar(0);
                string initialDisplay = string.Format(STRINGS.UI.MP_OVERLAY.CLIENT.DOWNLOADING_SAVE_FILE, initialProgressBar, "0", "0", save.TotalChunks);
				MultiplayerOverlay.Show(initialDisplay);
			}

			isDownloading = true;

			// Calculate which chunk based on offset (SEQUENCE-BASED APPROACH)
			int chunkIndex = chunk.Offset / save.ChunkSize;

			if (chunkIndex < 0 || chunkIndex >= save.TotalChunks)
			{
				DebugConsole.LogError($"[ChunkAssembler] Invalid chunk index {chunkIndex} for '{chunk.FileName}' (offset {chunk.Offset})");
				return;
			}

			// Copy chunk data
			Buffer.BlockCopy(chunk.Chunk, 0, save.Data, chunk.Offset, chunk.Chunk.Length);

			bool wasNewChunk = !save.ChunkReceived[chunkIndex];
			save.ChunkReceived[chunkIndex] = true;  // MARK IN LIST
			save.LastChunkReceived = System.DateTime.Now; // Update timestamp of last chunk

			if (wasNewChunk)
			{
				DebugConsole.Log($"[ChunkAssembler] Received chunk {chunkIndex + 1}/{save.TotalChunks} for '{chunk.FileName}'");
			}
			else
			{
				DebugConsole.LogWarning($"[ChunkAssembler] Received DUPLICATE chunk {chunkIndex + 1} for '{chunk.FileName}' - overwriting");
			}

			// SMART APPROACH: Calculate progress based on LIST of received chunks
			int receivedChunks = save.GetReceivedChunks();
			int percent = (receivedChunks * 100) / save.TotalChunks;

			// Create visual progress bar for client
			string progressBar = CreateClientProgressBar(percent);
			string progressDisplay = string.Format(STRINGS.UI.MP_OVERLAY.CLIENT.DOWNLOADING_SAVE_FILE, progressBar, percent, receivedChunks, save.TotalChunks);

			MultiplayerOverlay.Show(progressDisplay);

			// Send progress to host when percentage changes
			if (percent != save.LastReportedProgress && percent % 5 == 0) // Every 5% or first time
			{
				SendProgressToHost(chunk.FileName, receivedChunks, save.TotalChunks, percent);
				save.LastReportedProgress = percent;
			}

			// SMART APPROACH: Check if all chunks received
			if (save.IsComplete())
			{
				DebugConsole.Log($"[ChunkAssembler] Download COMPLETE for '{chunk.FileName}' - all {save.TotalChunks} chunks received!");

				// Send final progress (100%) to host
				SendProgressToHost(chunk.FileName, save.TotalChunks, save.TotalChunks, 100);

				// Show completion visual to client
				string completeProgressBar = CreateClientProgressBar(100);
                string completeDisplay = string.Format(STRINGS.UI.MP_OVERLAY.CLIENT.DOWNLOAD_COMPLETE, completeProgressBar, save.TotalChunks, save.TotalChunks);
				MultiplayerOverlay.Show(completeDisplay);

				InProgress.Remove(chunk.FileName);
				isDownloading = false;

				var fullSave = new WorldSave(chunk.FileName, save.Data);
				CoroutineRunner.RunOne(DelayedLoad(fullSave));
			}
			// REMOVED: Don't check for missing chunks immediately after each packet
			// Let the background periodic checker handle missing chunks based on inactivity timeout
		}

		private static void CheckForMissingChunks(string fileName, InProgressSave save)
		{
			using var _ = Profiler.Scope();

			// MUCH more patience - wait substantial time for chunks to arrive
			double waitTime = 30.0; // Always wait 30 seconds between checks

			if (System.DateTime.Now - save.LastCheckTime < System.TimeSpan.FromSeconds(waitTime))
				return;

			var missingChunks = save.GetMissingChunks();
			int receivedChunks = save.GetReceivedChunks();
			int contiguousChunks = save.GetMaxContiguousChunks();

			// Only consider problem if:
			// 1. Already received at least some chunks (> 5)
			// 2. Still has many missing (> 50% of total)
			if (missingChunks.Count > 0 && receivedChunks > 5 && missingChunks.Count > (save.TotalChunks / 2))
			{
				save.LastCheckTime = System.DateTime.Now;
				save.MissingChunkRequestCount++;

				DebugConsole.LogWarning($"[ChunkAssembler] Possible transfer stall for '{fileName}' - received {receivedChunks}/{save.TotalChunks}, missing {missingChunks.Count}, contiguous: {contiguousChunks}");

				// More attempts allowed
				if (save.MissingChunkRequestCount > 3)
				{
					DebugConsole.LogError($"[ChunkAssembler] Transfer appears stalled for '{fileName}' after 3 checks. Requesting full resend.", false);
					RequestSpecificChunks(fileName, missingChunks);
				}
				else
				{
					DebugConsole.Log($"[ChunkAssembler] Transfer proceeding slowly but still active. Check {save.MissingChunkRequestCount}/3");
				}
			}
			else
			{
				// Reset check time even if no action taken
				save.LastCheckTime = System.DateTime.Now;

				if (missingChunks.Count > 0)
				{
					DebugConsole.Log($"[ChunkAssembler] Transfer active for '{fileName}' - {receivedChunks}/{save.TotalChunks} chunks, {missingChunks.Count} missing (normal)");
				}
			}
		}

		private static void RequestSpecificChunks(string fileName, List<int> missingChunks)
		{
			using var _ = Profiler.Scope();

			// CURRENT IMPLEMENTATION: For now request full resend - future improvement = request specific chunks
			DebugConsole.LogWarning($"[ChunkAssembler] Requesting full resend due to missing chunks");

			// Clear current progress to restart fresh
			if (InProgress.ContainsKey(fileName))
			{
				DebugConsole.Log($"[ChunkAssembler] Clearing previous download progress for '{fileName}' before resend");
				InProgress.Remove(fileName);
			}

			var requestPacket = new SaveFileRequestPacket
			{
				Requester = MultiplayerSession.LocalUserID
			};

			PacketSender.SendToHost(requestPacket);
		}

		/// <summary>
		/// Public method to periodically check inactive transfers
		/// Called by the networking system instead of after each chunk
		/// </summary>
		public static void CheckInactiveTransfers()
		{
			using var _ = Profiler.Scope();

			foreach (var kvp in InProgress.ToArray())
			{
				string fileName = kvp.Key;
				InProgressSave save = kvp.Value;

				// Check if should check missing chunks based on inactivity time
				double timeSinceLastChunk = (System.DateTime.Now - save.LastChunkReceived).TotalSeconds;

				// Only check if enough time passed since last chunk received
				if (timeSinceLastChunk > 10.0) // 10 seconds of inactivity
				{
					CheckForMissingChunks(fileName, save);
				}
			}
		}

		/// <summary>
		/// Creates visual ASCII progress bar for the client
		/// </summary>
		private static string CreateClientProgressBar(int percent)
		{
			using var _ = Profiler.Scope();

			int barLength = 30;  // Larger bar for the client
			int filled = (percent * barLength) / 100;
			string bar = "";

			for (int i = 0; i < barLength; i++)
			{
				if (i < filled)
					bar += STRINGS.UI.MP_OVERLAY.SYNC.PROGRESS_BAR_FILLED;  // Filled
				else
					bar += STRINGS.UI.MP_OVERLAY.SYNC.PROGRESS_BAR_EMPTY;  // Empty
			}

			return string.Format(STRINGS.UI.MP_OVERLAY.SYNC.PROGRESS_BAR, bar);
		}

		/// <summary>
		/// Sends current sync progress for host to track
		/// </summary>
		private static void SendProgressToHost(string fileName, int receivedChunks, int totalChunks, int percent)
		{
			using var _ = Profiler.Scope();

			try
			{
				// Get local player name to display on host
				string playerName = Utils.GetLocalPlayerName();

				var progressPacket = new ONI_MP.Networking.Packets.World.SyncProgressPacket
				{
					ClientSteamID = MultiplayerSession.LocalUserID,
					ClientName = playerName,
					FileName = fileName,
					ReceivedChunks = receivedChunks,
					TotalChunks = totalChunks,
					ProgressPercent = percent
				};

				PacketSender.SendToHost(progressPacket);
				DebugConsole.Log($"[ChunkAssembler] Sent progress to host: {percent}% ({receivedChunks}/{totalChunks})");
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogWarning($"[ChunkAssembler] Failed to send progress to host: {ex}");
			}
		}

		private static System.Collections.IEnumerator DelayedLoad(WorldSave save)
		{
			using var _ = Profiler.Scope();

            yield return new WaitForSecondsRealtime(1f);
			SaveHelper.RequestWorldLoad(save);
		}
	}
}
