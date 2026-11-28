using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for Suit Locker (atmo suit, jet suit lockers) synchronization.
	/// </summary>

	// A dupe takes its suit from a locker or returns it through the checkpoint reactables, which only
	// run on the host. Tell the clients, who replay the call; nothing inside it is sent on its own.
	[HarmonyPatch(typeof(SuitLocker), nameof(SuitLocker.EquipTo))]
	public static class SuitLocker_EquipTo_Patch
	{
		public static void Prefix(SuitLocker __instance, out bool __state)
		{
			__state = __instance.GetStoredOutfit() != null;
			SuitLockerEquipPacket.BeginHostCall();
		}

		public static void Postfix(SuitLocker __instance, Equipment equipment, bool __state)
		{
			if (__state) SuitLockerEquipPacket.Send(__instance, equipment, true);
		}

		public static void Finalizer() => SuitLockerEquipPacket.EndHostCall();
	}

	[HarmonyPatch(typeof(SuitLocker), nameof(SuitLocker.UnequipFrom))]
	public static class SuitLocker_UnequipFrom_Patch
	{
		public static void Prefix(Equipment equipment, out bool __state)
		{
			__state = equipment != null && equipment.GetAssignable(Db.Get().AssignableSlots.Suit) != null;
			SuitLockerEquipPacket.BeginHostCall();
		}

		public static void Postfix(SuitLocker __instance, Equipment equipment, bool __state)
		{
			if (__state) SuitLockerEquipPacket.Send(__instance, equipment, false);
		}

		public static void Finalizer() => SuitLockerEquipPacket.EndHostCall();
	}

	[HarmonyPatch(typeof(SuitLocker), nameof(SuitLocker.ConfigRequestSuit))]
	public static class SuitLocker_ConfigRequestSuit_Patch
	{
		public static void Postfix(SuitLocker __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InActiveSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "SuitLockerRequestSuit".GetHashCode(),
				Value = 1f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[SuitLocker_ConfigRequestSuit_Patch] Synced ConfigRequestSuit on {__instance.name}");
		}
	}

	[HarmonyPatch(typeof(SuitLocker), nameof(SuitLocker.ConfigNoSuit))]
	public static class SuitLocker_ConfigNoSuit_Patch
	{
		public static void Postfix(SuitLocker __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InActiveSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "SuitLockerNoSuit".GetHashCode(),
				Value = 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[SuitLocker_ConfigNoSuit_Patch] Synced ConfigNoSuit on {__instance.name}");
		}
	}

	[HarmonyPatch(typeof(SuitLocker), nameof(SuitLocker.DropSuit))]
	public static class SuitLocker_DropSuit_Patch
	{
		public static void Postfix(SuitLocker __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InActiveSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "SuitLockerDropSuit".GetHashCode(),
				Value = 1f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[SuitLocker_DropSuit_Patch] Synced DropSuit on {__instance.name}");
		}
	}
}
