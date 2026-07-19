using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Patches.World.SideScreen
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
				ConfigHash = NetworkingHash.ForConfigKey("BottleEmptierAllowManualPump"),
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
				ConfigHash = NetworkingHash.ForConfigKey("AutomationOnly"),
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
				ConfigHash = NetworkingHash.ForConfigKey("DirectionControl"),
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
				ConfigHash = NetworkingHash.ForConfigKey("IceMachineElement"),
				StringValue = option.tag.Name,
				ConfigType = BuildingConfigType.String
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}
}
