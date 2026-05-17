using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Helpers;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together_API.Networking
{
	public static class PacketSenderAPI
	{
		static bool Init()
		{
			using var _ = Profiler.Scope();

			if (typesInitialized)
				return true;

			if (!ReflectionHelper.TryCreateDelegate<SendToAllDelegate>("ONI_Together.Networking.PacketSender, ONI_Together", "SendToAll_API", [typeof(object), typeof(ulong?), typeof(int)], out _SendToAll))
				return false;

			if (!ReflectionHelper.TryCreateDelegate<SendToAllClientsDelegate>("ONI_Together.Networking.PacketSender, ONI_Together", "SendToAllClients_API", [typeof(object), typeof(int)], out _SendToAllClients))
				return false;

			if (!ReflectionHelper.TryCreateDelegate<SendToAllExcludingDelegate>("ONI_Together.Networking.PacketSender, ONI_Together", "SendToAllExcluding_API", [typeof(object), typeof(HashSet<ulong>), typeof(int)], out _SendToAllExcluding))
				return false;

			if (!ReflectionHelper.TryCreateDelegate<SendToPlayerDelegate>("ONI_Together.Networking.PacketSender, ONI_Together", "SendToPlayer_API", [typeof(ulong), typeof(object), typeof(int)], out _SendToPlayer))
				return false;

			if (!ReflectionHelper.TryCreateDelegate<SendToHostDelegate>("ONI_Together.Networking.PacketSender, ONI_Together", "SendToHost_API", [typeof(object), typeof(int)], out _SendToHost))
				return false;

			if (!ReflectionHelper.TryCreateDelegate<SendToAllOtherPeersDelegate>("ONI_Together.Networking.PacketSender, ONI_Together", "SendToAllOtherPeers_API", [typeof(object)], out _SendToAllOtherPeers))
				return false;

			typesInitialized = true;
			return true;
		}

		static bool typesInitialized = false;

		static SendToAllDelegate? _SendToAll = null;
		delegate void SendToAllDelegate(object packet, ulong? exclude = null, int sendType = (int)PacketSendMode.Reliable);

		static SendToAllClientsDelegate? _SendToAllClients = null;
		delegate void SendToAllClientsDelegate(object packet, int sendType = (int)PacketSendMode.Reliable);

		static SendToAllExcludingDelegate? _SendToAllExcluding = null;
		delegate void SendToAllExcludingDelegate(object packet, HashSet<ulong> excludedIds, int sendType = (int)PacketSendMode.Reliable);

		static SendToPlayerDelegate? _SendToPlayer = null;
		delegate void SendToPlayerDelegate(ulong steamID, object packet, int sendType = (int)PacketSendMode.ReliableImmediate);

		static SendToHostDelegate? _SendToHost = null;
		delegate void SendToHostDelegate(object packet, int sendType = (int)PacketSendMode.ReliableImmediate);

		static SendToAllOtherPeersDelegate? _SendToAllOtherPeers = null;
		delegate void SendToAllOtherPeersDelegate(object packet);

		/// Original single-exclude overload
		public static void SendToAll(IPacket packet, ulong? exclude = null, PacketSendMode sendType = PacketSendMode.Reliable)
		{
			using var _ = Profiler.Scope();

			Init();
			if (_SendToAll == null)
				return;
			_SendToAll(packet, exclude, (int)sendType);
		}

		public static void SendToAllClients(IPacket packet, PacketSendMode sendType = PacketSendMode.Reliable)
		{
			using var _ = Profiler.Scope();

			Init();
			if (_SendToAllClients == null)
				return;
			_SendToAllClients(packet, (int)sendType);
		}

		public static void SendToAllExcluding(IPacket packet, HashSet<ulong> excludedIds, PacketSendMode sendType = PacketSendMode.Reliable)
		{
			using var _ = Profiler.Scope();

			Init();
			if (_SendToAllExcluding == null)
				return;
			_SendToAllExcluding(packet, excludedIds, (int)sendType);
		}

		public static void SendToPlayer(ulong steamId, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
		{
			using var _ = Profiler.Scope();

			Init();
			if (_SendToPlayer == null)
				return;
			_SendToPlayer(steamId, packet, (int)sendType);
		}

		public static void SendToHost(IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
		{
			using var _ = Profiler.Scope();

			Init();
			if (_SendToHost == null)
				return;
			_SendToHost(packet, (int)sendType);
		}
		public static void SendToAllOtherPeers(IPacket packet)
		{
			using var _ = Profiler.Scope();

			Init();
			if (_SendToAllOtherPeers == null)
				return;
			_SendToAllOtherPeers(packet);
		}
	}
}
