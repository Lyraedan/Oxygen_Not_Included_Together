using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class ArtifactHarvestSync
	{
		internal static bool TryCapture(
			ArtifactHarvestModule.StatesInstance smi,
			out ArtifactInventoryStatePacket state)
		{
			state = null;
			if (!TryGetModuleAndLocation(smi, out int moduleNetId, out AxialI location))
				return false;
			StarmapHexCellInventory inventory = ClusterGrid.Instance.AddOrGetHexCellInventory(location);
			if (inventory?.Items == null || inventory.Items.Count > ArtifactInventoryStatePacket.MaxItemCount)
				return false;
			state = new ArtifactInventoryStatePacket
			{
				ModuleNetId = moduleNetId,
				LocationQ = location.q,
				LocationR = location.r,
				Items = CaptureItems(inventory.Items)
			};
			return state.IsWireValid();
		}

		internal static bool TryApply(ArtifactInventoryStatePacket state)
		{
			if (state == null || !state.IsWireValid() || !TryResolveInventory(state, out var inventory))
				return false;
			var items = new List<StarmapHexCellInventory.SerializedItem>(state.Items.Count);
			foreach (ArtifactInventoryItemData item in state.Items)
			{
				Tag id = new(item.Id);
				if (!IsKnownItem(id, item.State))
					return false;
				items.Add(new StarmapHexCellInventory.SerializedItem(id, item.Mass, item.State));
			}
			inventory.Items = items;
			inventory.gameObject.Trigger(-1697596308);
			return true;
		}

		private static List<ArtifactInventoryItemData> CaptureItems(
			IEnumerable<StarmapHexCellInventory.SerializedItem> items)
		{
			var result = new List<ArtifactInventoryItemData>();
			foreach (StarmapHexCellInventory.SerializedItem item in items)
			{
				result.Add(new ArtifactInventoryItemData
				{
					Id = item.ID.ToString(),
					Mass = item.Mass,
					State = item.StateMask
				});
			}
			return result;
		}

		private static bool TryGetModuleAndLocation(
			ArtifactHarvestModule.StatesInstance smi,
			out int moduleNetId,
			out AxialI location)
		{
			moduleNetId = smi?.gameObject.GetNetIdentity()?.NetId ?? 0;
			location = default;
			RocketModuleCluster module = smi?.GetComponent<RocketModuleCluster>();
			Clustercraft craft = module?.CraftInterface?.GetComponent<Clustercraft>();
			if (moduleNetId == 0 || craft == null)
				return false;
			location = craft.Location;
			return true;
		}

		private static bool TryResolveInventory(
			ArtifactInventoryStatePacket state,
			out StarmapHexCellInventory inventory)
		{
			inventory = null;
			if (!NetworkIdentityRegistry.TryGetComponent(state.ModuleNetId, out RocketModuleCluster module))
				return false;
			Clustercraft craft = module?.CraftInterface?.GetComponent<Clustercraft>();
			AxialI location = AxialCoordinateSync.FromQr(state.LocationQ, state.LocationR);
			if (craft == null || craft.Location != location)
				return false;
			inventory = ClusterGrid.Instance.AddOrGetHexCellInventory(location);
			return inventory != null;
		}

		private static bool IsKnownItem(Tag id, Element.State state)
			=> state == Element.State.Vacuum
				? Assets.TryGetPrefab(id) != null
				: ElementLoader.GetElement(id) != null;
	}

	[HarmonyPatch(typeof(ArtifactHarvestModule.StatesInstance),
		nameof(ArtifactHarvestModule.StatesInstance.HarvestFromHexCell))]
	internal static class ArtifactHarvestFromHexPatch
	{
		internal static bool Prefix()
			=> !MultiplayerSession.InSession || !MultiplayerSession.IsClient;

		internal static void Postfix(ArtifactHarvestModule.StatesInstance __instance)
		{
			if (!MultiplayerSession.IsHostInSession ||
			    !ArtifactHarvestSync.TryCapture(__instance, out ArtifactInventoryStatePacket state))
				return;
			PacketSender.SendToAllClients(state, PacketSendMode.ReliableImmediate);
		}
	}
}
