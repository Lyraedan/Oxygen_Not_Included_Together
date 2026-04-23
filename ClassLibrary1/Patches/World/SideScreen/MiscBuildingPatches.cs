using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World.SideScreen
{
	/// <summary>
	/// Small miscellaneous building patches: Automatable, DirectionControl, IceMachine, BottleEmptier
	/// </summary>

	[HarmonyPatch(typeof(BottleEmptier), nameof(BottleEmptier.OnChangeAllowManualPumpingStationFetching))]
	public static class BottleEmptier_ManualPump_Patch
	{
		public static void Postfix(BottleEmptier __instance)
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
				ConfigHash = "BottleEmptierManualPump".GetHashCode(),
				Value = __instance.allowManualPumpingStationFetching ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	[HarmonyPatch(typeof(Automatable), nameof(Automatable.SetAutomationOnly))]
	public static class Automatable_SetAutomationOnly_Patch
	{
		public static void Postfix(Automatable __instance, bool only)
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
				ConfigHash = "AutomationOnly".GetHashCode(),
				Value = only ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[Automatable] Synced AutomationOnly={only} for {__instance.name}");
		}
	}

	[HarmonyPatch(typeof(DirectionControl), nameof(DirectionControl.SetAllowedDirection))]
	public static class DirectionControl_SetAllowedDirection_Patch
	{
		public static void Postfix(DirectionControl __instance, WorkableReactable.AllowedDirection new_direction)
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
				ConfigHash = "DirectionControl".GetHashCode(),
				Value = (int)new_direction,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[DirectionControl] Synced direction={new_direction} for {__instance.name}");
		}
	}

	[HarmonyPatch(typeof(IceMachine), nameof(IceMachine.OnOptionSelected))]
	public static class IceMachine_OnOptionSelected_Patch
	{
		public static void Postfix(IceMachine __instance, FewOptionSideScreen.IFewOptionSideScreen.Option option)
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
				ConfigHash = "IceMachineElement".GetHashCode(),
				StringValue = option.tag.Name,
				ConfigType = BuildingConfigType.String
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}
}
