using HarmonyLib;
using KSerialization;
using ONI_MP.DebugTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Shared.Profiling;

namespace ONI_MP.Networking.Packets.Architecture
{
	public static class PacketRegistry
	{
		private static readonly Dictionary<int, Type> _PacketTypes = new ();

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

		private static void Register(Type packageType)
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
			}

			using var sha256 = SHA256.Create();
			byte[] hash = sha256.ComputeHash(ms.ToArray());
			return BitConverter.ToInt32(hash, 0);
		}

        public static void TryRegister(Type packetType, string nameOverride = "")
        {
	        using var _ = Profiler.Scope();

            try
            {
                Register(packetType);
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
