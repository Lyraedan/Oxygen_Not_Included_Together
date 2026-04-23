using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Networking.Components;
using ONI_MP.Menus;
using Steamworks;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_MP.Networking.Packets.World
{
    /// <summary>
    /// Client sends sync progress for host to visualize
    /// Host can monitor in real time how the save file download is progressing
    /// </summary>
    public class SyncProgressPacket : IPacket
    {
        // Tracks progress of all clients for host UI
        private static readonly Dictionary<ulong, ClientSyncInfo> ClientProgress = new Dictionary<ulong, ClientSyncInfo>();

        private struct ClientSyncInfo
        {
            public string ClientName;
            public int ProgressPercent;
            public int ReceivedChunks;
            public int TotalChunks;
        }
        public ulong ClientSteamID;      // Who is sending the progress
        public string ClientName;           // Readable player name
        public string FileName;             // Name of file being downloaded
        public int ReceivedChunks;          // How many chunks have been received
        public int TotalChunks;             // Total chunks in the file
        public int ProgressPercent;         // Calculated percentage (0-100)

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();

            writer.Write(ClientSteamID);
            writer.Write(ClientName);
            writer.Write(FileName);
            writer.Write(ReceivedChunks);
            writer.Write(TotalChunks);
            writer.Write(ProgressPercent);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();

            ClientSteamID = reader.ReadUInt64();
            ClientName = reader.ReadString();
            FileName = reader.ReadString();
            ReceivedChunks = reader.ReadInt32();
            TotalChunks = reader.ReadInt32();
            ProgressPercent = reader.ReadInt32();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            // Only host processes client progress
            if (!MultiplayerSession.IsHost)
                return;

            // Update client information
            ClientProgress[ClientSteamID] = new ClientSyncInfo
            {
                ClientName = ClientName,
                ProgressPercent = ProgressPercent,
                ReceivedChunks = ReceivedChunks,
                TotalChunks = TotalChunks
            };

            DebugConsole.Log($"[SyncProgress] {ClientName} sync progress: {ProgressPercent}% ({ReceivedChunks}/{TotalChunks} chunks)");

            // Update host display with progress from all clients
            UpdateHostProgressDisplay();
        }

        private static void UpdateHostProgressDisplay()
        {
            using var _ = Profiler.Scope();

            try
            {
                var progressLines = new List<string>();
                progressLines.Add(STRINGS.UI.MP_OVERLAY.SYNC.CLIENT_SYNC_PROGRESS);
                progressLines.Add("");

                foreach (var kvp in ClientProgress)
                {
                    var client = kvp.Key;
                    var info = kvp.Value;

                    // Show visual progress bar with player name
                    string progressBar = CreateProgressBar(info.ProgressPercent);
                    string clientName = info.ClientName;
                    bool isFriends = SteamFriends.HasFriend(client.AsCSteamID(), EFriendFlags.k_EFriendFlagImmediate);
                    if(isFriends)
                    {
                        // Display the friends name as we have them on our friends list
                        clientName = SteamFriends.GetFriendPersonaName(client.AsCSteamID());
                    }

                    string clientLine = string.Format(STRINGS.UI.MP_OVERLAY.SYNC.CLIENT_PROGRESS, clientName, progressBar, info.ProgressPercent);

                    if (info.ProgressPercent < 100)
                    {
                        clientLine += $" {string.Format(STRINGS.UI.MP_OVERLAY.SYNC.CLIENT_CHUNK_SYNC_DATA, info.ReceivedChunks, info.TotalChunks)}";
                    }
                    else
                    {
                        clientLine += $" {STRINGS.UI.MP_OVERLAY.SYNC.CLIENT_SYNC_COMPLETE}";
                    }

                    progressLines.Add(clientLine);
                }

                // If all completed, remove display after some seconds
                bool allComplete = true;
                foreach (var info in ClientProgress.Values)
                {
                    if (info.ProgressPercent < 100)
                    {
                        allComplete = false;
                        break;
                    }
                }

                if (allComplete && ClientProgress.Count > 0)
                {
                    progressLines.Add("");
                    progressLines.Add(STRINGS.UI.MP_OVERLAY.SYNC.ALL_CLIENTS_SYNCED);

                    // Clear progress display after completion
                    // Note: Could schedule removal after delay, but keeping simple for now
                }

                string fullDisplay = string.Join("\n", progressLines);
                MultiplayerOverlay.Show(fullDisplay);
            }
            catch (System.Exception ex)
            {
                DebugConsole.LogWarning($"[SyncProgress] Error updating host display: {ex}");
            }
        }

        private static string CreateProgressBar(int percent)
        {
            using var _ = Profiler.Scope();

            int barLength = 20;
            int filled = (percent * barLength) / 100;
            string bar = "";

            for (int i = 0; i < barLength; i++)
            {
                if (i < filled)
                    bar += STRINGS.UI.MP_OVERLAY.SYNC.PROGRESS_BAR_FILLED;  // ASCII - funciona sempre
                else
                    bar += STRINGS.UI.MP_OVERLAY.SYNC.PROGRESS_BAR_EMPTY;  // ASCII - funciona sempre
            }

            return string.Format(STRINGS.UI.MP_OVERLAY.SYNC.PROGRESS_BAR, bar);
        }

        /// <summary>
        /// Remove client from progress tracking (when client disconnects)
        /// </summary>
        public static void RemoveClientProgress(ulong clientId)
        {
            using var _ = Profiler.Scope();

            ClientProgress.Remove(clientId);
            if (ClientProgress.Count == 0)
            {
                MultiplayerOverlay.Close();
            }
        }
    }
}