using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class PlantMutationSync
	{
		private static int applyDepth;
		internal static bool IsApplying => applyDepth > 0;

		public static void ResetSessionState() => applyDepth = 0;

		internal static bool ShouldRunMutation(bool inSession, bool isHost, bool applying)
			=> applying || !inSession || isHost;

		internal static void Send(MutantPlant plant)
		{
			if (!MultiplayerSession.IsHostInSession || !TryCapture(plant, out PlantMutationStatePacket packet))
				return;
			PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}

		internal static bool TryApply(PlantMutationStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGetComponent(packet.PlantNetId, out MutantPlant plant) || plant == null ||
			    plant.SpeciesID.GetHashCode() != packet.SpeciesHash)
				return false;
			var info = new PlantSubSpeciesCatalog.SubSpeciesInfo(plant.SpeciesID, packet.MutationIds);
			if (info.ID.GetHashCode() != packet.SubSpeciesHash)
				return false;

			applyDepth++;
			try
			{
				plant.SetSubSpecies(new List<string>(packet.MutationIds));
				Traverse.Create(plant).Field("analyzed").SetValue(packet.Analyzed);
				if (packet.Analyzed && PlantSubSpeciesCatalog.Instance != null)
					PlantSubSpeciesCatalog.Instance.IdentifySubSpecies(plant.SubSpeciesID);
				plant.UpdateNameAndTags();
			}
			finally { applyDepth--; }
			return true;
		}

		private static bool TryCapture(MutantPlant plant, out PlantMutationStatePacket packet)
		{
			packet = null;
			if (plant == null)
				return false;
			NetworkIdentity identity = plant.gameObject.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			packet = new PlantMutationStatePacket
			{
				PlantNetId = identity.NetId,
				SpeciesHash = plant.SpeciesID.GetHashCode(),
				SubSpeciesHash = plant.SubSpeciesID.GetHashCode(),
				Analyzed = Traverse.Create(plant).Field("analyzed").GetValue<bool>(),
				MutationIds = plant.MutationIDs == null
					? new List<string>()
					: new List<string>(plant.MutationIDs)
			};
			return packet.IsWireValid();
		}
	}

	[HarmonyPatch(typeof(MutantPlant), nameof(MutantPlant.Mutate))]
	internal static class MutantPlantMutatePatch
	{
		internal static bool Prefix() => PlantMutationSync.ShouldRunMutation(
			MultiplayerSession.InSession, MultiplayerSession.IsHost, PlantMutationSync.IsApplying);
		internal static void Postfix(MutantPlant __instance) => PlantMutationSync.Send(__instance);
	}

	[HarmonyPatch(typeof(MutantPlant), nameof(MutantPlant.Analyze))]
	internal static class MutantPlantAnalyzePatch
	{
		internal static bool Prefix() => PlantMutationSync.ShouldRunMutation(
			MultiplayerSession.InSession, MultiplayerSession.IsHost, PlantMutationSync.IsApplying);
		internal static void Postfix(MutantPlant __instance) => PlantMutationSync.Send(__instance);
	}

	[HarmonyPatch(typeof(MutantPlant), nameof(MutantPlant.SetSubSpecies))]
	internal static class MutantPlantSetSubSpeciesPatch
	{
		internal static bool Prefix() => PlantMutationSync.ShouldRunMutation(
			MultiplayerSession.InSession, MultiplayerSession.IsHost, PlantMutationSync.IsApplying);
		internal static void Postfix(MutantPlant __instance) => PlantMutationSync.Send(__instance);
	}

	[HarmonyPatch(typeof(MutantPlant), "OnSpawn")]
	internal static class MutantPlantSpawnPatch
	{
		internal static void Postfix(MutantPlant __instance) => PlantMutationSync.Send(__instance);
	}
}
