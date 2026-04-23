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
	/// Patches for space/DLC buildings: GeoTuner, MissileLauncher, Gantry
	/// </summary>

	[HarmonyPatch(typeof(GeoTuner.Instance), nameof(GeoTuner.Instance.AssignFutureGeyser))]
	public static class GeoTuner_Instance_AssignFutureGeyser_Patch
	{
		public static void Postfix(GeoTuner.Instance __instance, Geyser newFutureGeyser)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			int geyserCell = (newFutureGeyser != null) ? Grid.PosToCell(newFutureGeyser.gameObject) : -1;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "GeoTunerGeyser".GetHashCode(),
				Value = geyserCell,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[GeoTuner] Synced geyser assignment: cell={geyserCell}");
		}
	}

	[HarmonyPatch(typeof(GeoTunerSideScreen), nameof(GeoTunerSideScreen.SetTarget))]
	public static class GeoTunerSideScreen_SetTarget_Patch
	{
		public static void Postfix(GeoTunerSideScreen __instance, GameObject target)
		{
			using var _ = Profiler.Scope();

			if (__instance.targetGeotuner == null) return;
			__instance.RefreshOptions();
		}
	}

	[HarmonyPatch(typeof(MissileLauncher.Instance), nameof(MissileLauncher.Instance.ChangeAmmunition))]
	public static class MissileLauncher_Instance_ChangeAmmunition_Patch
	{
		public static void Postfix(MissileLauncher.Instance __instance, Tag tag, bool allowed)
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
				ConfigHash = "MissileLauncherAmmo".GetHashCode(),
				Value = allowed ? 1f : 0f,
				ConfigType = BuildingConfigType.String,
				StringValue = tag.Name
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[MissileLauncher] Synced ammo: tag={tag.Name}, allowed={allowed}");
		}
	}

	[HarmonyPatch(typeof(MissileSelectionSideScreen), nameof(MissileSelectionSideScreen.SetTarget))]
	public static class MissileSelectionSideScreen_SetTarget_Patch
	{
		public static void Postfix(MissileSelectionSideScreen __instance, GameObject target)
		{
			using var _ = Profiler.Scope();

			if (__instance.targetMissileLauncher == null) return;
			__instance.Refresh();
		}
	}

	[HarmonyPatch(typeof(Gantry), nameof(Gantry.Toggle))]
	public static class Gantry_Toggle_Patch
	{
		public static void Postfix(Gantry __instance)
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
				ConfigHash = "GantryToggle".GetHashCode(),
				Value = __instance.IsSwitchedOn ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}
}
