using JetBrains.Annotations;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together_API.Networking
{
	public enum ModApiAuthority : byte
	{
		HostToClientsOnly = 0,
		ClientToHost = 1,
		ClientBroadcast = 2
	}

	public static class PacketRegistryAPI
	{
		static bool Init()
		{
			using var _ = Profiler.Scope();

			if (typesInitialized)
				return true;

			if (!ReflectionHelper.TryCreateDelegate<TryRegisterPacketDelegate>(
				    "ONI_Together.Networking.Packets.Architecture.PacketRegistry, ONI_Together",
				    "TryRegister",
				    [typeof(Type), typeof(string), typeof(byte)],
				    out _TryRegister))
				return false;
			typesInitialized = true;
			return true;
		}

		static bool typesInitialized = false;
		static TryRegisterPacketDelegate? _TryRegister = null;
		delegate void TryRegisterPacketDelegate(Type packetType, string nameOverride, byte authority);


		/// <summary>
		/// Registers a packet type with the packet registry.
		/// Do not call earlier than "OnAllModsLoaded" Harmony event or the main mod type might not exist yet.
		/// </summary>
		/// <param name="packetType"></param>
		public static void TryRegister(Type packetType, string nameOverride = null)
			=> TryRegister(packetType, ModApiAuthority.HostToClientsOnly, nameOverride);

		/// <summary>
		/// Registers a packet with an explicit direction allowed for non-host senders.
		/// </summary>
		public static void TryRegister(
			Type packetType,
			ModApiAuthority authority,
			string nameOverride = null)
		{
			using var _ = Profiler.Scope();

			if (!Init())
				return;
			_TryRegister(packetType, nameOverride, (byte)authority);
		}

		/// <summary>
		/// Automatically registers all packets that inherit IPackage to the multiplayer mod
		/// </summary>
		/// <param name="assembly"></param>
		public static void AutoRegisterAll(
			Assembly? assembly = null,
			ModApiAuthority authority = ModApiAuthority.HostToClientsOnly)
		{
			using var _ = Profiler.Scope();

			if(assembly == null)
				assembly = Assembly.GetExecutingAssembly();

			PacketRegistrationHelper.AutoRegisterPackets(
				assembly, t => TryRegister(t, authority), out int count, out var duration);

			Debug.Log($"[MP-API]: Registered {count} network packets in assembly {assembly.GetName()}, taking {duration.TotalMilliseconds} milliseconds");
		}
	}
}
