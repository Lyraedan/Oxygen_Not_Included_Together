using System;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.World.Handlers;
using ONI_Together.Scripts.Creatures;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Patches
{
	[HarmonyPatch(typeof(BaseRoverConfig), nameof(BaseRoverConfig.BaseRover))]
	public static class RoverPrefabSyncPatch
	{
		public static void Postfix(GameObject __result)
		{
			if (__result == null) return;
			__result.AddOrGet<NetworkIdentity>();
			__result.AddOrGet<EntityPositionHandler>();
			__result.AddOrGet<AnimStateSyncer>();
			__result.AddOrGet<CreatureMultiplayerInitializer>();
		}
	}

	[HarmonyPatch(typeof(BaseRoverConfig), nameof(BaseRoverConfig.OnSpawn))]
	public static class RoverSpawnSyncPatch
	{
		public static void Postfix(GameObject inst)
		{
			if (!MultiplayerSession.IsHostInSession || inst == null) return;
			var identity = inst.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			if (identity.NetId == 0) return;

			PacketSender.SendToAllClients(new SpawnPrefabPacket(
				identity.NetId,
				inst.PrefabID().GetHashCode(),
				inst.transform.position));
		}
	}

	[HarmonyPatch(typeof(MorbRoverMaker.Instance), nameof(MorbRoverMaker.Instance.SpawnRover))]
	public static class ClientRoverSpawnGuardPatch
	{
		public static bool Prefix()
		{
			// Runtime rover creation is sent by RoverSpawnSyncPatch. Allowing the
			// client story machine to create one locally would duplicate it.
			return !MultiplayerSession.IsClient;
		}
	}

	[HarmonyPatch(typeof(StateMachineController), nameof(StateMachineController.StartSMIS))]
	public static class MissingStateMachineSyncPatch
	{
		public static void Postfix(StateMachineController __instance)
		{
			if (__instance == null) return;
			foreach (var smi in __instance.GetAllSMI<StateMachine.Instance>())
			{
				if (!RuntimeStateSyncer.IsSupported(smi)) continue;
				__instance.gameObject.AddOrGet<NetworkIdentity>();
				__instance.gameObject.AddOrGet<RuntimeStateSyncer>();
				break;
			}
		}
	}

	[HarmonyPatch(typeof(BuildingHP), nameof(BuildingHP.DoDamage))]
	public static class BuildingDamageSyncPatch
	{
		public static bool Prefix()
		{
			return !MultiplayerSession.IsClient || BuildingConfigPacket.IsApplyingPacket;
		}

		public static void Postfix(BuildingHP __instance)
		{
			if (!MultiplayerSession.IsHostInSession || __instance == null) return;
			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			if (identity.NetId == 0) return;

			PacketSender.SendToAllClients(new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = KnownIssueStateHandler.HitPointsKey.GetHashCode(),
				Value = __instance.HitPoints
			}, PacketSendMode.ReliableImmediate);
		}
	}

	[HarmonyPatch(typeof(EmptyConduitWorkable), nameof(EmptyConduitWorkable.MarkForEmptying))]
	public static class EmptyConduitMarkSyncPatch
	{
		public static void Postfix(EmptyConduitWorkable __instance)
		{
			Send(__instance, true);
		}

		internal static void Send(EmptyConduitWorkable workable, bool marked)
		{
			if (!MultiplayerSession.InSession || BuildingConfigPacket.IsApplyingPacket || workable == null) return;
			var identity = workable.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			if (identity.NetId == 0) return;

			PacketSender.SendToAllOtherPeers(new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(workable.gameObject),
				ConfigHash = KnownIssueStateHandler.EmptyConduitKey.GetHashCode(),
				Value = marked ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			});
		}
	}

	[HarmonyPatch(typeof(EmptyConduitWorkable), nameof(EmptyConduitWorkable.CancelEmptying))]
	public static class EmptyConduitCancelSyncPatch
	{
		public static void Postfix(EmptyConduitWorkable __instance)
		{
			EmptyConduitMarkSyncPatch.Send(__instance, false);
		}
	}

	[HarmonyPatch(typeof(LaunchableRocketCluster.StatesInstance), nameof(LaunchableRocketCluster.StatesInstance.IsNotGroundBound))]
	public static class RocketGroundStateGuardPatch
	{
		public static bool Prefix(LaunchableRocketCluster.StatesInstance __instance, ref bool __result)
		{
			var module = __instance?.GetComponent<RocketModuleCluster>();
			var craftInterface = module?.CraftInterface;
			var craft = craftInterface?.GetComponent<Clustercraft>();
			if (module != null && craftInterface != null && craft != null) return true;
			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(ComplexFabricatorSideScreen), nameof(ComplexFabricatorSideScreen.HasAllRecipeRequirements))]
	public static class ComplexFabricatorRecipeGuardPatch
	{
		public static bool Prefix(ComplexFabricatorSideScreen __instance, ComplexRecipe recipe, ref bool __result)
		{
			var fabricator = __instance?.targetFab;
			if (recipe != null && fabricator != null && !fabricator.IsNullOrDestroyed() &&
				fabricator.inStorage != null && fabricator.buildStorage != null && fabricator.GetMyWorld() != null)
				return true;

			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(ComplexFabricatorSideScreen), nameof(ComplexFabricatorSideScreen.RefreshIngredientAvailabilityVis))]
	public static class ComplexFabricatorRefreshGuardPatch
	{
		public static bool Prefix(ComplexFabricatorSideScreen __instance)
		{
			return __instance != null && __instance.targetFab != null &&
				!__instance.targetFab.IsNullOrDestroyed() && __instance.recipeCategoryToggleMap != null;
		}
	}

	[HarmonyPatch(typeof(CreatureSimTemperatureTransfer), nameof(CreatureSimTemperatureTransfer.unsafeUpdateAverageKiloWattsExchanged))]
	public static class CreatureTemperatureGuardPatch
	{
		public static bool Prefix(CreatureSimTemperatureTransfer __instance)
		{
			if (MultiplayerSession.IsClient) return false;
			return __instance != null && __instance.primaryElement != null &&
				__instance.average_kilowatts_exchanged != null && Game.Instance != null &&
				Game.Instance.simData != null && Sim.IsValidHandle(__instance.simHandle);
		}
	}
}
