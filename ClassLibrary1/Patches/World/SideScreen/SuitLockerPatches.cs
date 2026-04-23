using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for Suit Locker (atmo suit, jet suit lockers) synchronization.
	/// </summary>

	[HarmonyPatch(typeof(SuitLocker), nameof(SuitLocker.ConfigRequestSuit))]
	public static class SuitLocker_ConfigRequestSuit_Patch
	{
		public static void Postfix(SuitLocker __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

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
			if (!MultiplayerSession.InSession) return;

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
			if (!MultiplayerSession.InSession) return;

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
