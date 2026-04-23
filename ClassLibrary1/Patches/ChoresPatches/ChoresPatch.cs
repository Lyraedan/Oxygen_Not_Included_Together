using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ChoresPatches
{
	public static class ChoresPatch
	{
		public static void SendAssignmentPacket(Chore __instance)
		{
			using var _ = Profiler.Scope();

			// Only the host/server sends assignment packets
			if (MultiplayerSession.IsClient || !MultiplayerSession.InSession)
				return;

			// Get the assigned duplicant's GameObject
			var dupeGO = __instance.driver?.gameObject;
			if (dupeGO == null)
			{
				DebugConsole.LogWarning("[Chores] Cannot send chore assignment: driver GameObject is null.");
				return;
			}

			// Ensure the dupe has a NetworkIdentity component
			if (!dupeGO.TryGetComponent(out NetworkIdentity netComponent))
			{
				DebugConsole.LogWarning($"[Chores] Duplicant {dupeGO.name} has no NetworkIdentity; skipping chore assignment packet.");
				return;
			}

			// Validate the chore type
			var choreType = __instance.choreType;
			if (choreType == null)
			{
				DebugConsole.LogWarning("[Chores] Cannot send chore assignment: chore type is null.");
				return;
			}

			// Build and send the packet
			var packet = new ChoreAssignmentPacket
			{
				NetId = netComponent.NetId,
				ChoreTypeId = choreType.Id,
				TargetPosition = __instance.gameObject?.transform.position ?? Vector3.zero,
				TargetCell = Grid.PosToCell(__instance.gameObject),
				TargetPrefabId = __instance.gameObject?.PrefabID().Name ?? ""
			};

			PacketSender.SendToAll(packet);

			DebugConsole.Log($"[Chores] Sent ChoreAssignmentPacket: NetId={packet.NetId}, ChoreId={packet.ChoreTypeId}, Type={choreType.Name}:{choreType.Id}");
		}
	}


	[HarmonyPatch(typeof(StandardChoreBase), nameof(StandardChoreBase.Begin))]
	public static class StandardChoreBase_Begin_Patch
	{
		public static void Postfix(Chore __instance)
		{
			using var _ = Profiler.Scope();

			return; // Disabled for now
			ChoresPatch.SendAssignmentPacket(__instance);
		}
	}

	/* Disabled because they all use base.Begin which we patched above but they do all have unique Begin methods, every other chore uses the standard base begin
	[HarmonyPatch(typeof(AttackChore), nameof(AttackChore.Begin))]
	public static class AttackChorePatch { public static void Postfix(AttackChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(BionicMassOxygenAbsorbChore), nameof(BionicMassOxygenAbsorbChore.Begin))]
	public static class BionicMassOxygenAbsorbChorePatch { public static void Postfix(BionicMassOxygenAbsorbChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch]
	public static class BuildChorePatch
	{
			// This tells Harmony which method to patch (a specific closed generic version)
			static MethodBase TargetMethod()
			{
					return typeof(WorkChore<Constructable>).GetMethod("Begin");
			}

			public static void Postfix(WorkChore<Constructable> __instance, Chore.Precondition.Context context)
			{
					ChoresPatch.SendAssignmentPacket(__instance);
			}
	}

	[HarmonyPatch(typeof(DeliverFoodChore), nameof(DeliverFoodChore.Begin))]
	public static class DeliverFoodChorePatch { public static void Postfix(DeliverFoodChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch]
	public static class DisinfectChorePatch
	{
			static MethodBase TargetMethod()
			{
					return typeof(WorkChore<Disinfectable>).GetMethod("Begin");
			}

			public static void Postfix(WorkChore<Disinfectable> __instance, Chore.Precondition.Context context)
			{
					ChoresPatch.SendAssignmentPacket(__instance);
			}
	}

	[HarmonyPatch(typeof(EatChore), nameof(EatChore.Begin))]
	public static class EatChorePatch { public static void Postfix(EatChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(EquipChore), nameof(EquipChore.Begin))]
	public static class EquipChorePatch { public static void Postfix(EquipChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(FetchAreaChore), nameof(FetchAreaChore.Begin))]
	public static class FetchAreaChorePatch { public static void Postfix(FetchAreaChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(FetchChore), nameof(FetchChore.Begin))]
	public static class FetchChorePatch { public static void Postfix(FetchChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(FindAndConsumeOxygenSourceChore), nameof(FindAndConsumeOxygenSourceChore.Begin))]
	public static class FindAndConsumeOxygenSourceChorePatch { public static void Postfix(FindAndConsumeOxygenSourceChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(FixedCaptureChore), nameof(FixedCaptureChore.Begin))]
	public static class FixedCaptureChorePatch { public static void Postfix(FixedCaptureChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(FoodFightChore), nameof(FoodFightChore.Begin))]
	public static class FoodFightChorePatch { public static void Postfix(FoodFightChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch]
	public static class HarvestChorePatch
	{
			static MethodBase TargetMethod()
			{
					return typeof(WorkChore<Harvestable>).GetMethod("Begin");
			}

			public static void Postfix(WorkChore<Harvestable> __instance, Chore.Precondition.Context context)
			{
					ChoresPatch.SendAssignmentPacket(__instance);
			}
	}

	[HarmonyPatch]
	public static class MopChorePatch
	{
			static MethodBase TargetMethod()
			{
					return typeof(WorkChore<Moppable>).GetMethod("Begin");
			}

			public static void Postfix(WorkChore<Moppable> __instance, Chore.Precondition.Context context)
			{
					ChoresPatch.SendAssignmentPacket(__instance);
			}
	}

	[HarmonyPatch(typeof(MournChore), nameof(MournChore.Begin))]
	public static class MournChorePatch { public static void Postfix(MournChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(MovePickupableChore), nameof(MovePickupableChore.Begin))]
	public static class MovePickupableChorePatch { public static void Postfix(MovePickupableChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(PartyChore), nameof(PartyChore.Begin))]
	public static class PartyChorePatch { public static void Postfix(PartyChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(RancherChore), nameof(RancherChore.Begin))]
	public static class RancherChorePatch { public static void Postfix(RancherChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch]
	public static class RepairChorePatch
	{
			static MethodBase TargetMethod()
			{
					return typeof(WorkChore<Repairable>).GetMethod("Begin");
			}

			public static void Postfix(WorkChore<Repairable> __instance, Chore.Precondition.Context context)
			{
					ChoresPatch.SendAssignmentPacket(__instance);
			}
	}

	[HarmonyPatch(typeof(RescueIncapacitatedChore), nameof(RescueIncapacitatedChore.Begin))]
	public static class RescueIncapacitatedChorePatch { public static void Postfix(RescueIncapacitatedChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(RescueSweepBotChore), nameof(RescueSweepBotChore.Begin))]
	public static class RescueSweepBotChorePatch { public static void Postfix(RescueSweepBotChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(SeekAndInstallBionicUpgradeChore), nameof(SeekAndInstallBionicUpgradeChore.Begin))]
	public static class SeekAndInstallBionicUpgradeChorePatch { public static void Postfix(SeekAndInstallBionicUpgradeChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch]
	public static class SweepChorePatch
	{
			static MethodBase TargetMethod()
			{
					return typeof(WorkChore<Pickupable>).GetMethod("Begin");
			}

			public static void Postfix(WorkChore<Pickupable> __instance, Chore.Precondition.Context context)
			{
					ChoresPatch.SendAssignmentPacket(__instance);
			}
	}

	[HarmonyPatch(typeof(TakeMedicineChore), nameof(TakeMedicineChore.Begin))]
	public static class TakeMedicineChorePatch { public static void Postfix(TakeMedicineChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(UseSolidLubricantChore), nameof(UseSolidLubricantChore.Begin))]
	public static class UseSolidLubricantChorePatch { public static void Postfix(UseSolidLubricantChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(VomitChore), nameof(VomitChore.Begin))]
	public static class VomitChorePatch { public static void Postfix(VomitChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }

	[HarmonyPatch(typeof(WaterCoolerChore), nameof(WaterCoolerChore.Begin))]
	public static class WaterCoolerChorePatch { public static void Postfix(WaterCoolerChore __instance) => ChoresPatch.SendAssignmentPacket(__instance); }
	*/
}
