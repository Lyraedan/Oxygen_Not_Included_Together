using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class MissionControlSync
	{
		internal static bool TryApply(MissionControlStatePacket state)
		{
			if (state?.IsWireValid() != true ||
			    !NetworkIdentityRegistry.TryGetComponent(state.WorkableNetId,
				    out MissionControlClusterWorkable workable))
				return false;
			Clustercraft craft = null;
			if (state.CraftNetId != 0 && !TryResolveCraft(state.CraftNetId, out craft))
				return false;
			int currentCraftId = GetCraftNetId(workable.TargetClustercraft);
			float currentBuff = craft?.controlStationBuffTimeRemaining ?? 0f;
			if (!MissionControlStatePacket.NeedsApply(currentCraftId, currentBuff, state))
				return true;
			SpacedOutSyncGuard.Run(() =>
			{
				if (currentCraftId != state.CraftNetId)
					workable.TargetClustercraft = craft;
				if (craft != null)
					craft.controlStationBuffTimeRemaining = state.BuffTimeRemaining;
			});
			return true;
		}

		internal static void Broadcast(MissionControlClusterWorkable workable, Clustercraft craft)
		{
			if (!MultiplayerSession.IsHostInSession || workable == null)
				return;
			int workableNetId = workable.GetNetId();
			int craftNetId = GetCraftNetId(craft);
			if (workableNetId == 0 || craft != null && craftNetId == 0)
				return;
			PacketSender.SendToAllClients(new MissionControlStatePacket
			{
				WorkableNetId = workableNetId,
				CraftNetId = craftNetId,
				BuffTimeRemaining = craft?.controlStationBuffTimeRemaining ?? 0f
			}, PacketSendMode.ReliableImmediate);
		}

		private static int GetCraftNetId(Clustercraft craft)
		{
			RocketModuleCluster module = craft?.ModuleInterface?.GetPrimaryPilotModule(out _);
			return module?.GetNetIdentity()?.NetId ?? 0;
		}

		private static bool TryResolveCraft(int netId, out Clustercraft craft)
		{
			craft = null;
			if (!NetworkIdentityRegistry.TryGet(netId, out var identity))
				return false;
			craft = identity.GetComponent<Clustercraft>();
			if (craft != null)
				return true;
			craft = identity.GetComponent<RocketModuleCluster>()?.CraftInterface?.GetComponent<Clustercraft>();
			return craft != null;
		}
	}

	[HarmonyPatch(typeof(MissionControlCluster.Instance),
		nameof(MissionControlCluster.Instance.GetRandomBoostableClustercraft))]
	internal static class MissionControlSelectionPatch
	{
		internal static bool Prefix(ref Clustercraft __result)
		{
			if (!MultiplayerSession.IsClient)
				return true;
			__result = null;
			return false;
		}
	}

	[HarmonyPatch(typeof(MissionControlClusterWorkable),
		nameof(MissionControlClusterWorkable.TargetClustercraft), MethodType.Setter)]
	internal static class MissionControlTargetPatch
	{
		internal static bool Prefix()
			=> !MultiplayerSession.IsClient || SpacedOutSyncGuard.IsApplying;

		internal static void Postfix(MissionControlClusterWorkable __instance, Clustercraft value)
			=> MissionControlSync.Broadcast(__instance, value);
	}

	[HarmonyPatch(typeof(MissionControlCluster.Instance), nameof(MissionControlCluster.Instance.ApplyEffect))]
	internal static class MissionControlBoostPatch
	{
		internal static bool Prefix() => !MultiplayerSession.IsClient;

		internal static void Postfix(MissionControlCluster.Instance __instance, Clustercraft clustercraft)
			=> MissionControlSync.Broadcast(
				__instance.gameObject.GetComponent<MissionControlClusterWorkable>(), clustercraft);
	}
}
