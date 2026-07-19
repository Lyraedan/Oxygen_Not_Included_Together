using HarmonyLib;
using KSerialization;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Networking.Packets.Events;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Networking.Packets.Tools;
using ONI_Together.Networking.Packets.Tools.Build;
using ONI_Together.Networking.Packets.Tools.Cancel;
using ONI_Together.Networking.Packets.Tools.Clear;
using ONI_Together.Networking.Packets.Tools.Deconstruct;
using ONI_Together.Networking.Packets.Tools.Dig;
using ONI_Together.Networking.Packets.Tools.Disinfect;
using ONI_Together.Networking.Packets.Tools.Move;
using ONI_Together.Networking.Packets.Tools.Prioritize;
using ONI_Together.Networking.Packets.World;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Architecture
{
	public enum ModApiAuthority : byte
	{
		HostToClientsOnly = 0,
		ClientToHost = 1,
		ClientBroadcast = 2
	}

	public static class PacketRegistry
	{
		public const ModApiAuthority DefaultModApiAuthority = ModApiAuthority.HostToClientsOnly;
		private static readonly Dictionary<int, Type> _PacketTypes = new ();
		private static readonly Dictionary<Type, ModApiAuthority> _ModApiAuthorities = new();

        public static bool HasRegisteredPacket(int type)
        {
	        using var _ = Profiler.Scope();

            return _PacketTypes.ContainsKey(type);
        }
		public static bool HasRegisteredPacket(Type type)
		{
			using var _ = Profiler.Scope();

			return _PacketTypes.ContainsKey(API_Helper.GetHashCode(type));
		}

		private static void Register(
			Type packageType,
			ModApiAuthority modApiAuthority = ModApiAuthority.HostToClientsOnly)
        {
	        using var _ = Profiler.Scope();

            int id = API_Helper.GetHashCode(packageType);
			var IPacketType = typeof(IPacket);
            if(IPacketType.IsAssignableFrom(packageType))
			{
                if (_PacketTypes.ContainsKey(id))
				{
					DebugConsole.LogWarning($"[PacketRegistry] Packet {packageType.Name} was already registered with {id}");
                    return;
                }

				_PacketTypes[id] = packageType;
				DebugConsole.LogSuccess($"[PacketRegistry] Registered {packageType.Name} => {id}");
			}
			///Inheritance checks will fail for mod api packets, so these get wrapped in a generated type derived from ModApiPacket<T> at runtime
			else if (API_Helper.ValidAsModApiPacket(packageType))
            {
				///gotta register both ids so they can be created from either the wrapped or unwrapped type id
				var wrappedType = API_Helper.CreateModApiPacketType(packageType);
				if (_PacketTypes.ContainsKey(id))
				{
					DebugConsole.LogWarning($"[PacketRegistry] ModAPI Packet {packageType.Name} was already registered with {id}");
					return;
				}
				_PacketTypes[id] = wrappedType;
				var wrappedId = API_Helper.GetHashCode(wrappedType);
				_PacketTypes[wrappedId] = wrappedType;
				_ModApiAuthorities[wrappedType] = modApiAuthority;
				DebugConsole.LogSuccess($"[PacketRegistry] Registered from ModAPI: {packageType.Name} => {id} (unwrapped), {wrappedId} (wrapped)");
			}
            else
                throw new InvalidOperationException($"Type {packageType.Name} does not implement IPacket interface");
        }
        public static IPacket Create(int type)
		{
			using var _ = Profiler.Scope();

			return _PacketTypes.TryGetValue(type, out var packetType)
					? (IPacket)Activator.CreateInstance(packetType)
					: throw new InvalidOperationException($"No packet registered for type {type}");
		}

        public static int GetPacketId(IPacket packet)
        {
	        using var scope = Profiler.Scope();

            var type = packet.GetType();
            int id = API_Helper.GetHashCode(type);

			if (!_PacketTypes.TryGetValue(id, out _))
                throw new InvalidOperationException($"Packet type {type.Name} with id {id} is not registered");

            return id;
        }

		public static void RegisterDefaults()
		{
			using var _ = Profiler.Scope();

           Shared.Helpers.PacketRegistrationHelper.AutoRegisterPackets(Assembly.GetExecutingAssembly(), (t=>TryRegister(t)), out int count, out var duration);
			DebugConsole.LogSuccess($"[PacketRegistry] Auto-registering {count} packets took {duration.TotalMilliseconds} ms");
		}

		public static int GetRegisteredPacketFingerprint()
		{
			using var _ = Profiler.Scope();

			int[] ids = _PacketTypes.Keys.OrderBy(id => id).ToArray();
			using var ms = new MemoryStream();
			using var writer = new BinaryWriter(ms);
			foreach (int id in ids)
			{
				writer.Write(id);
				Type packetType = _PacketTypes[id];
				writer.Write((byte)(_ModApiAuthorities.TryGetValue(packetType, out var authority)
					? authority
					: ModApiAuthority.HostToClientsOnly));
			}

			using var sha256 = SHA256.Create();
			byte[] hash = sha256.ComputeHash(ms.ToArray());
			return hash[0]
				| hash[1] << 8
				| hash[2] << 16
				| hash[3] << 24;
		}

		internal static bool CanClientDispatchModApi(IPacket packet, bool relayed)
		{
			if (packet is not IModApiPacket
			    || !_ModApiAuthorities.TryGetValue(packet.GetType(), out ModApiAuthority authority))
				return false;

			return CanClientDispatchModApi(authority, relayed);
		}

		internal static bool CanClientDispatchModApi(ModApiAuthority authority, bool relayed)
		{
			return relayed
				? authority == ModApiAuthority.ClientBroadcast
				: authority == ModApiAuthority.ClientToHost;
		}

		public static void TryRegister(Type packetType, string nameOverride = "")
			=> TryRegister(packetType, nameOverride, DefaultModApiAuthority);

		public static void TryRegister(Type packetType, string nameOverride, byte modApiAuthority)
			=> TryRegister(packetType, nameOverride, (ModApiAuthority)modApiAuthority);

		public static void TryRegister(
			Type packetType,
			string nameOverride,
			ModApiAuthority modApiAuthority)
        {
	        using var _ = Profiler.Scope();

            try
            {
				if (!Enum.IsDefined(typeof(ModApiAuthority), modApiAuthority))
					throw new ArgumentOutOfRangeException(nameof(modApiAuthority));
				Register(packetType, modApiAuthority);
            }
            catch (Exception e)
            {
                string name = string.IsNullOrEmpty(nameOverride)
                    ? packetType.Name
                    : nameOverride;

                DebugConsole.LogError($"Failed to register {name}: {e}");
            }
        }
    }
}
