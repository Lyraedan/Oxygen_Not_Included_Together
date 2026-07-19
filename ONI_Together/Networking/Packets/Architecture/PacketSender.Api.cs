using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.Collections.Generic;

namespace ONI_Together.Networking
{
	public static partial class PacketSender
	{
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
