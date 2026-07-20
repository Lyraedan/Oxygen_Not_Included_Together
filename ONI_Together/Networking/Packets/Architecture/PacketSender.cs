using Epic.OnlineServices.P2P;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Transport;
using ONI_Together.Networking.Transport.Steam;
using Shared.Interfaces.Networking;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Shared.Profiling;
using UnityEngine;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync;
using ONI_Together.Networking.Overlay;

namespace ONI_Together.Networking
{
	public static class PacketSender
	{
		private class PacketUpdateRunner
		{
			private readonly float _updateIntervalS;
			private readonly Dictionary<object, float> _lastDispatchTime = [];

			public PacketUpdateRunner(int packetId, uint updateInterval)
			{
				_updateIntervalS = updateInterval / 1000f;
			}

			public bool CanDispatchNext(object connection)
			{
				using var _ = Profiler.Scope();

				if (!_lastDispatchTime.TryGetValue(connection, out var lastDispatchTime))
					return true;

				return Time.unscaledTime - lastDispatchTime >= _updateIntervalS;
			}

			public void RecordDispatch(object connection)
			{
				using var _ = Profiler.Scope();

				_lastDispatchTime[connection] = Time.unscaledTime;
			}
		}

		// Kilobytes
        public static float MAX_PACKET_SIZE_LAN = 0.5f; // 512 bytes (is multipled by 1024)
        public static int MAX_PACKET_SIZE_RELIABLE = 512;
		public static int MAX_PACKET_SIZE_UNRELIABLE = 1024;

        public static byte[] SerializePacketForSending(IPacket packet)
		{
			using var _ = Profiler.Scope();

			using (var ms = new System.IO.MemoryStream())
			using (var writer = new System.IO.BinaryWriter(ms))
			{
				int packet_type = PacketRegistry.GetPacketId(packet);
				writer.Write(packet_type);
				packet.Serialize(writer);
				return ms.ToArray();
			}
		}

		static Dictionary<int, PacketUpdateRunner> UpdateRunners = [];
		static Dictionary<object, Dictionary<int, List<byte[]>>> WaitingBulkPacketsPerReceiver = [];
		// Running byte total per (receiver, packetId) so LAN capacity checks stay O(1) per append.
		static Dictionary<object, Dictionary<int, int>> WaitingBulkPacketBytes = [];
		// Packet ids that belong to DragToolPacket subclasses — tagged lazily on first append
		// so the bulk flush site can record SyncStats.DragTool without needing the typed instance.
		static HashSet<int> DragToolBulkPacketIds = new HashSet<int>();
		public static void DispatchPendingBulkPackets()
		{
			using var _ = Profiler.Scope();

			var emptyConnections = new List<object>();
			foreach (var kvp in WaitingBulkPacketsPerReceiver)
			{
				var conn = kvp.Key;
				foreach (var packetId in kvp.Value.Keys.ToList())
				{
					DispatchPendingBulkPacketOfType(conn, packetId, true);
				}

				if (kvp.Value.Count == 0)
					emptyConnections.Add(conn);
			}

			foreach (var conn in emptyConnections)
			{
				WaitingBulkPacketsPerReceiver.Remove(conn);
				WaitingBulkPacketBytes.Remove(conn);
			}
		}

		static void DispatchPendingBulkPacketOfType(object conn, int packetId, bool intervalRun = false)
		{
			using var _ = Profiler.Scope();

			if (!WaitingBulkPacketsPerReceiver.TryGetValue(conn, out var allPendingPackets)
				|| !allPendingPackets.TryGetValue(packetId, out var pendingPackets)
				|| !pendingPackets.Any())
			{
				return;
			}
			if (intervalRun && UpdateRunners.TryGetValue(packetId, out var intervalRunner) && !intervalRunner.CanDispatchNext(conn))
				return;

			int flushCount = pendingPackets.Count;
			int flushBytes = 0;
			WaitingBulkPacketBytes.TryGetValue(conn, out var byteTotals);
			if (byteTotals != null && byteTotals.TryGetValue(packetId, out var bt))
				flushBytes = bt;
			var swFlush = System.Diagnostics.Stopwatch.StartNew();
			SendToConnection(conn, new BulkSenderPacket(packetId, pendingPackets), PacketSendMode.ReliableImmediate);
			swFlush.Stop();
			pendingPackets.Clear();
			allPendingPackets.Remove(packetId);
			if (byteTotals != null)
			{
				byteTotals[packetId] = 0;
				byteTotals.Remove(packetId);
			}
			if (UpdateRunners.TryGetValue(packetId, out var runner))
				runner.RecordDispatch(conn);
			if (DragToolBulkPacketIds.Contains(packetId))
				SyncStats.RecordSync(SyncStats.DragTool, flushCount, flushBytes, (float)swFlush.Elapsed.TotalMilliseconds);
		}
		public static void AppendPendingBulkPacket(object conn, IPacket packet, IBulkablePacket bp)
		{
			using var _ = Profiler.Scope();

			int packetId = PacketRegistry.GetPacketId(packet);
			int maxPacketNumberPerPacket = bp.MaxPackSize;

			if (packet is ONI_Together.Networking.Packets.Tools.DragToolPacket)
				DragToolBulkPacketIds.Add(packetId);

			if (!UpdateRunners.ContainsKey(packetId))
			{
				UpdateRunners[packetId] = new PacketUpdateRunner(packetId, bp.IntervalMs);
			}

			if (!WaitingBulkPacketsPerReceiver.TryGetValue(conn, out var bulkPacketWaitingData))
			{
				WaitingBulkPacketsPerReceiver[conn] = [];
				bulkPacketWaitingData = WaitingBulkPacketsPerReceiver[conn];
			}
			if (!bulkPacketWaitingData.TryGetValue(packetId, out var pendingPackets))
			{
				bulkPacketWaitingData[packetId] = new List<byte[]>(maxPacketNumberPerPacket);
				pendingPackets = bulkPacketWaitingData[packetId];
			}
			var serialized = packet.SerializeToByteArray();

			var tracker = NetIdActivityTracker.Instance;
			if (tracker != null)
			{
				int netId = NetIdActivityTracker.GetNetIdFromPacket(packet);
				if (netId > 0)
					tracker.RecordActivity(netId, serialized.Length + 4);
			}

			pendingPackets.Add(serialized);

			if (!WaitingBulkPacketBytes.TryGetValue(conn, out var byteTotals))
			{
				byteTotals = [];
				WaitingBulkPacketBytes[conn] = byteTotals;
			}
			if (!byteTotals.TryGetValue(packetId, out var runningTotal))
				runningTotal = 4; // +4 for the packetId int header
			runningTotal += serialized.Length;
			byteTotals[packetId] = runningTotal;

			bool atCapacity = false;
			if (NetworkConfig.IsLanConfig())
			{
				float maxSize = MAX_PACKET_SIZE_LAN * 1024f;
				if (runningTotal >= maxSize)
				{
					atCapacity = true;
				}
			}

			if (pendingPackets.Count >= maxPacketNumberPerPacket || atCapacity)
			{
				DispatchPendingBulkPacketOfType(conn, packetId);
			}
		}
		public static byte[] SerializeToByteArray(this IPacket packet)
		{
			using var _ = Profiler.Scope();

			using var ms = new System.IO.MemoryStream();
			using var writer = new System.IO.BinaryWriter(ms);
			packet.Serialize(writer);
			return ms.ToArray();
		}

		/// <summary>
		/// Send to one connection by HSteamNetConnection handle.
		/// </summary>
		///

		public static bool SendToConnection(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
		{
			using var _ = Profiler.Scope();

			if (packet is IBulkablePacket bp)
			{
				AppendPendingBulkPacket(conn, packet, bp);
				return true;
			}

			var tracker = NetIdActivityTracker.Instance;
			if (tracker != null)
			{
				int netId = NetIdActivityTracker.GetNetIdFromPacket(packet);
				if (netId > 0)
				{
					byte[] serialized = SerializePacketForSending(packet);
					tracker.RecordActivity(netId, serialized.Length);
				}
			}

			return NetworkConfig.TransportPacketSender.SendToConnection(conn, packet, sendType);
		}

		/// <summary>
		/// Send a packet to a player by their SteamID.
		/// </summary>
		public static bool SendToPlayer(ulong steamID, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
		{
			using var _ = Profiler.Scope();

			// Prevent host from sending packets to itself (can cause loops and errors)
			if (MultiplayerSession.IsHost && steamID == MultiplayerSession.HostUserID)
			{
				DebugConsole.LogWarning($"[PacketSender] Host attempted to send packet {packet.GetType().Name} to itself - blocked");
				return false;
			}

			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(steamID, out var player) || player.Connection == null)
			{
				DebugConsole.LogWarning($"[PacketSender] No connection found for SteamID {steamID}");
				return false;
			}

			return SendToConnection(player.Connection, packet, sendType);
		}

		private static bool CanBroadcastTo(MultiplayerPlayer player)
		{
			using var _ = Profiler.Scope();

			if (player == null || player.Connection == null)
			{
				return false;
			}

			if (!MultiplayerSession.IsHost || player.PlayerId == MultiplayerSession.HostUserID)
			{
				return true;
			}

			return player.ProtocolVerified;
		}

		public static void SendToHost(IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.HostUserID.IsValid())
			{
				DebugConsole.LogWarning($"[PacketSender] Failed to send to host. Host is invalid.");
				return;
			}
			SendToPlayer(MultiplayerSession.HostUserID, packet, sendType);
		}

		// Throttle counter for per-connection send failures so a transport storm
		// does not flood the log. First 5 errors are logged in full, then 1/100 after.
		private static long _sendErrorCount;

		private static void TrySendToConnection(MultiplayerPlayer player, IPacket packet, PacketSendMode sendType)
		{
			try
			{
				SendToConnection(player.Connection, packet, sendType);
			}
			catch (Exception ex)
			{
				// A throw from the transport layer (e.g. Riptide pendingMessages key collision)
				// must not skip remaining connections in the broadcast. Log-and-continue.
				long n = ++_sendErrorCount;
				if (n <= 5 || n % 100 == 0)
				{
					DebugConsole.LogError($"[PacketSender] Send to player {player.PlayerId} failed (packet={packet.GetType().Name}, #{n}): {ex}");
				}
			}
		}

		/// Original single-exclude overload
		public static void SendToAll(IPacket packet, ulong? exclude = null, PacketSendMode sendType = PacketSendMode.Reliable)
		{
			using var _ = Profiler.Scope();

            // Only send this packet if its being observed by a someone
            if (packet is IViewportCullable vp && WorldStateSyncer.Instance != null)
            {
                int cell = vp.GetViewportCell();
                foreach (var player in MultiplayerSession.ConnectedPlayers.Values)
                {
                    if (exclude.HasValue && player.PlayerId == exclude.Value) continue;
                    if (!CanBroadcastTo(player)) continue;
                    if (WorldStateSyncer.Instance.IsCellInPlayerViewport(player.PlayerId, cell))
                        TrySendToConnection(player, packet, sendType);
                }
                return;
            }

            foreach (var player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (exclude.HasValue && player.PlayerId == exclude.Value)
					continue;

				if (CanBroadcastTo(player))
					TrySendToConnection(player, packet, sendType);
			}
		}

        public static void SendToGroup(int groupId, IPacket packet, PacketSendMode sendType = PacketSendMode.Reliable)
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.IsHost)
            {
                DebugConsole.LogWarning("[PacketSender] Only the host can send to groups. Tried sending: " + packet.GetType());
                return;
            }

            if (groupId == -1)
            {
                SendToAll(packet, MultiplayerSession.HostUserID, sendType);
                return;
            }

            foreach (var playerId in InterestGroupManager.GetGroupMemberIds(groupId))
            {
                if (playerId == MultiplayerSession.HostUserID) continue;
                if (!MultiplayerSession.ConnectedPlayers.TryGetValue(playerId, out var player)) continue;
                if (!CanBroadcastTo(player)) continue;
                TrySendToConnection(player, packet, sendType);
            }
        }

        public static void SendToAllClients(IPacket packet, PacketSendMode sendType = PacketSendMode.Reliable)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
			{
				DebugConsole.LogWarning("[PacketSender] Only the host can send to all clients. Tried sending: " + packet.GetType());
				return;
			}
			SendToAll(packet, MultiplayerSession.HostUserID, sendType);
		}

		public static void SendToAllExcluding(IPacket packet, HashSet<ulong> excludedIds, PacketSendMode sendType = PacketSendMode.Reliable)
		{
			using var _ = Profiler.Scope();

            if (packet is IViewportCullable vp && WorldStateSyncer.Instance != null)
            {
                int cell = vp.GetViewportCell();
                foreach (var player in MultiplayerSession.ConnectedPlayers.Values)
                {
                    if (excludedIds != null && excludedIds.Contains(player.PlayerId)) continue;
                    if (!CanBroadcastTo(player)) continue;
                    if (WorldStateSyncer.Instance.IsCellInPlayerViewport(player.PlayerId, cell))
                        TrySendToConnection(player, packet, sendType);
                }
                return;
            }

            foreach (var player in MultiplayerSession.ConnectedPlayers.Values)
			{
				if (excludedIds != null && excludedIds.Contains(player.PlayerId))
					continue;

				if (CanBroadcastTo(player))
					TrySendToConnection(player, packet, sendType);
			}
		}

		/// <summary>
		/// Sends a packet to all other players.
		/// Forces the packet origin to be on the host itself
		/// if sent from the host, it goes to all clients.
		/// otherwise it is wrapped in a HostBroadcastPacket and sent to the host for rebroadcasting.
		///
		/// </summary>
		/// <param name="packet"></param>
		public static void SendToAllOtherPeersFromHost(IPacket packet)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
			{
				DebugConsole.LogWarning("[PacketSender] Not in a multiplayer session, cannot send to other peers");
				return;
			}
			//DebugConsole.Log("[PacketSender] Sending packet to all other peers: " + packet.GetType().Name + " from host");

			if (MultiplayerSession.IsHost)
				SendToAllClients(packet);
			else
				SendToHost(new HostBroadcastPacket(packet, Utils.NilUlong()));
		}

		public static void SendToAllOtherPeersFromHost_API(object api_packet)
		{
			using var _ = Profiler.Scope();

			var type = api_packet.GetType();
			if (!PacketRegistry.HasRegisteredPacket(type))
			{
				DebugConsole.LogError($"[PacketSender] Attempted to send unregistered packet type: {type.Name}");
				return;
			}
			if (!API_Helper.WrapApiPacket(api_packet, out var packet))
			{
				DebugConsole.LogError($"[PacketSender] Failed to wrap API packet of type: {type.Name}");
				return;
			}
			SendToAllOtherPeersFromHost(packet);
		}


		/// <summary>
		/// Sends a packet to all other players.
		/// if sent from the host, it goes to all clients.
		/// otherwise it is wrapped in a HostBroadcastPacket and sent to the host for rebroadcasting.
		/// </summary>
		/// <param name="packet"></param>
		public static void SendToAllOtherPeers(IPacket packet)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
			{
				DebugConsole.LogWarning("[PacketSender] Not in a multiplayer session, cannot send to other peers");
				return;
			}
			//DebugConsole.Log("[PacketSender] Sending packet to all other peers: " + packet.GetType().Name);

			if (MultiplayerSession.IsHost)
				SendToAllClients(packet);
			else if (packet is IBulkablePacket && packet is not IClientRelayable)
				SendToHost(packet);
			else
				SendToHost(new HostBroadcastPacket(packet, MultiplayerSession.LocalUserID));
		}

		public static void SendToAllOtherPeers_API(object api_packet)
		{
			using var _ = Profiler.Scope();

			var type = api_packet.GetType();
			if (!PacketRegistry.HasRegisteredPacket(type))
			{
				DebugConsole.LogError($"[PacketSender] Attempted to send unregistered packet type: {type.Name}");
				return;
			}
			if (!API_Helper.WrapApiPacket(api_packet, out var packet))
			{
				DebugConsole.LogError($"[PacketSender] Failed to wrap API packet of type: {type.Name}");
				return;
			}
			SendToAllOtherPeers(packet);
		}

		/// <summary>
		/// custom types, interfaces and enums are not directly usable across assembly boundaries
		/// </summary>
		/// <param name="api_packet">data object of the packet class that got registered with a ModApiPacket wrapper earlier</param>
		/// <param name="exclude"></param>
		/// <param name="sendType"></param>
		public static void SendToAll_API(object api_packet, ulong? exclude = null, int sendType = (int)PacketSendMode.Reliable)
		{
			using var _ = Profiler.Scope();

			var type = api_packet.GetType();
			if (!PacketRegistry.HasRegisteredPacket(type))
			{
				DebugConsole.LogError($"[PacketSender] Attempted to send unregistered packet type: {type.Name}");
				return;
			}
			if (!API_Helper.WrapApiPacket(api_packet, out var packet))
			{
				DebugConsole.LogError($"[PacketSender] Failed to wrap API packet of type: {type.Name}");
				return;
			}
			SendToAll(packet, exclude, (PacketSendMode)sendType);
		}

		public static void SendToAllClients_API(object api_packet, int sendType = (int)PacketSendMode.Reliable)
		{
			using var _ = Profiler.Scope();

			var type = api_packet.GetType();
			if (!PacketRegistry.HasRegisteredPacket(type))
			{
				DebugConsole.LogError($"[PacketSender] Attempted to send unregistered packet type: {type.Name}");
				return;
			}

			if (!API_Helper.WrapApiPacket(api_packet, out var packet))
			{
				DebugConsole.LogError($"[PacketSender] Failed to wrap API packet of type: {type.Name}");
				return;
			}
			SendToAllClients(packet, (PacketSendMode)sendType);
		}

		public static void SendToAllExcluding_API(object api_packet, HashSet<ulong> excludedIds, int sendType = (int)PacketSendMode.Reliable)
		{
			using var _ = Profiler.Scope();

			var type = api_packet.GetType();
			if (!PacketRegistry.HasRegisteredPacket(type))
			{
				DebugConsole.LogError($"[PacketSender] Attempted to send unregistered packet type: {type.Name}");
				return;
			}

			if (!API_Helper.WrapApiPacket(api_packet, out var packet))
			{
				DebugConsole.LogError($"[PacketSender] Failed to wrap API packet of type: {type.Name}");
				return;
			}
			SendToAllExcluding(packet, excludedIds, (PacketSendMode)sendType);
		}

		public static void SendToPlayer_API(ulong steamID, object api_packet, int sendType = (int)PacketSendMode.ReliableImmediate)
		{
			using var _ = Profiler.Scope();

			var type = api_packet.GetType();
			if (!PacketRegistry.HasRegisteredPacket(type))
			{
				DebugConsole.LogError($"[PacketSender] Attempted to send unregistered packet type: {type.Name}");
				return;
			}

			if (!API_Helper.WrapApiPacket(api_packet, out var packet))
			{
				DebugConsole.LogError($"[PacketSender] Failed to wrap API packet of type: {type.Name}");
				return;
			}
			SendToPlayer(steamID, packet, (PacketSendMode)sendType);
		}

		public static void SendToHost_API(object api_packet, int sendType = (int)PacketSendMode.ReliableImmediate)
		{
			using var _ = Profiler.Scope();

			var type = api_packet.GetType();
			if (!PacketRegistry.HasRegisteredPacket(type))
			{
				DebugConsole.LogError($"[PacketSender] Attempted to send unregistered packet type: {type.Name}");
				return;
			}

			if (!API_Helper.WrapApiPacket(api_packet, out var packet))
			{
				DebugConsole.LogError($"[PacketSender] Failed to wrap API packet of type: {type.Name}");
				return;
			}
			SendToHost(packet, (PacketSendMode)sendType);
		}

	}
}
