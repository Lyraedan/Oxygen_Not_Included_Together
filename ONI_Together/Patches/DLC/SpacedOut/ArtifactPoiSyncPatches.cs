using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using UnityEngine;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class ArtifactPoiSync
	{
		internal static bool ShouldRunGameplay(bool inSession, bool isHost) => !inSession || isHost;

		internal static NetworkIdentity EnsurePersistentIdentity(GameObject gameObject)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(gameObject);

		internal static bool TryCapture(ArtifactPOIStates.Instance smi, out ArtifactPoiStatePacket packet)
		{
			packet = null;
			ClusterGridEntity entity = smi?.GetComponent<ClusterGridEntity>();
			StarmapHexCellInventory inventory = smi?.HexCellInventory;
			if (entity == null || inventory?.Items == null ||
			    inventory.Items.Count > ArtifactInventoryStatePacket.MaxItemCount)
				return false;
			NetworkIdentity identity = EnsureIdentity(smi.gameObject);
			ulong lifecycleRevision = EnsureLifecycle(identity);
			packet = new ArtifactPoiStatePacket
			{
				TargetNetId = identity.NetId,
				LifecycleRevision = lifecycleRevision,
				LocationQ = entity.Location.q,
				LocationR = entity.Location.r,
				PoiCharge = smi.poiCharge,
				NumHarvests = Traverse.Create(smi).Field("numHarvests").GetValue<int>(),
				ArtifactToHarvest = smi.artifactToHarvest ?? "",
				Items = CaptureItems(inventory.Items),
				Selector = CaptureSelector()
			};
			return packet.IsWireValid();
		}

		internal static bool TryApply(ArtifactPoiStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() || !TryResolvePoi(packet, out var smi))
				return false;
			ClusterGridEntity entity = smi?.GetComponent<ClusterGridEntity>();
			AxialI location = AxialCoordinateSync.FromQr(packet.LocationQ, packet.LocationR);
			if (smi == null || entity == null || entity.Location != location)
				return false;
			List<StarmapHexCellInventory.SerializedItem> items = BuildItems(packet.Items);
			if (items == null || !ApplySelector(packet.Selector))
				return false;
			smi.poiCharge = packet.PoiCharge;
			smi.artifactToHarvest = string.IsNullOrEmpty(packet.ArtifactToHarvest)
				? null : packet.ArtifactToHarvest;
			Traverse.Create(smi).Field("numHarvests").SetValue(packet.NumHarvests);
			smi.HexCellInventory.Items = items;
			smi.HexCellInventory.gameObject.Trigger(-1697596308);
			return true;
		}

		private static bool TryResolvePoi(
			ArtifactPoiStatePacket packet, out ArtifactPOIStates.Instance smi)
		{
			smi = null;
			ulong current = NetworkIdentityRegistry.GetLastLifecycleRevision(packet.TargetNetId);
			if (!CanBindLifecycle(
				    current,
				    NetworkIdentityRegistry.IsLifecycleTombstoned(packet.TargetNetId),
				    packet.LifecycleRevision))
			{
				return false;
			}
			if (NetworkIdentityRegistry.TryGet(packet.TargetNetId, out NetworkIdentity existing))
				smi = existing.gameObject.GetSMI<ArtifactPOIStates.Instance>();
			else
				smi = FindUniquePoi(AxialCoordinateSync.FromQr(packet.LocationQ, packet.LocationR));
			if (!MatchesLocation(smi, packet.LocationQ, packet.LocationR))
				return false;
			return NetworkIdentityRegistry.TryBindAuthoritativeLifecycle(
				smi.gameObject, packet.TargetNetId, packet.LifecycleRevision);
		}

		private static ArtifactPOIStates.Instance FindUniquePoi(AxialI location)
		{
			if (ClusterGrid.Instance == null ||
			    !ClusterGrid.Instance.cellContents.TryGetValue(location, out var entities))
				return null;
			ArtifactPOIStates.Instance match = null;
			foreach (ClusterGridEntity entity in entities)
			{
				ArtifactPOIStates.Instance candidate =
					entity?.gameObject.GetSMI<ArtifactPOIStates.Instance>();
				if (candidate == null || !NetworkIdentityRegistry.IsAvailableBindingCandidate(
					    candidate.gameObject))
					continue;
				if (match != null)
					return null;
				match = candidate;
			}
			return match;
		}

		private static bool MatchesLocation(
			ArtifactPOIStates.Instance smi, int q, int r)
		{
			ClusterGridEntity entity = smi?.GetComponent<ClusterGridEntity>();
			return entity != null && entity.Location == AxialCoordinateSync.FromQr(q, r);
		}

		internal static bool CanBindLifecycle(
			ulong currentRevision, bool tombstoned, ulong incomingRevision)
		{
			return incomingRevision != 0 && currentRevision <= incomingRevision &&
			       (currentRevision != incomingRevision || !tombstoned);
		}

		private static List<ArtifactInventoryItemData> CaptureItems(
			IEnumerable<StarmapHexCellInventory.SerializedItem> items)
		{
			var result = new List<ArtifactInventoryItemData>();
			foreach (StarmapHexCellInventory.SerializedItem item in items)
				result.Add(new ArtifactInventoryItemData
				{
					Id = item.ID.ToString(), Mass = item.Mass, State = item.StateMask
				});
			return result;
		}

		private static List<StarmapHexCellInventory.SerializedItem> BuildItems(
			IEnumerable<ArtifactInventoryItemData> items)
		{
			var result = new List<StarmapHexCellInventory.SerializedItem>();
			foreach (ArtifactInventoryItemData item in items)
			{
				Tag id = new(item.Id);
				if (item.State == Element.State.Vacuum ? Assets.TryGetPrefab(id) == null :
				    ElementLoader.GetElement(id) == null)
					return null;
				result.Add(new StarmapHexCellInventory.SerializedItem(id, item.Mass, item.State));
			}
			return result;
		}

		internal static ArtifactSelectorStateData CaptureSelector()
		{
			if (ArtifactSelector.Instance == null)
				return null;
			var traverse = Traverse.Create(ArtifactSelector.Instance);
			var placed = traverse.Field("placedArtifacts")
				.GetValue<Dictionary<ArtifactType, List<string>>>();
			return new ArtifactSelectorStateData
			{
				Terrestrial = Copy(placed, ArtifactType.Terrestrial),
				Space = Copy(placed, ArtifactType.Space),
				Any = Copy(placed, ArtifactType.Any),
				AnalyzedTerrestrialCount = ArtifactSelector.Instance.AnalyzedArtifactCount,
				AnalyzedSpaceCount = ArtifactSelector.Instance.AnalyzedSpaceArtifactCount,
				AnalyzedIds = new List<string>(ArtifactSelector.Instance.GetAnalyzedArtifactIDs())
			};
		}

		internal static bool ApplySelector(ArtifactSelectorStateData state)
		{
			if (ArtifactSelector.Instance == null || state?.IsWireValid() != true)
				return false;
			var placed = new Dictionary<ArtifactType, List<string>>
			{
				[ArtifactType.Terrestrial] = new List<string>(state.Terrestrial),
				[ArtifactType.Space] = new List<string>(state.Space),
				[ArtifactType.Any] = new List<string>(state.Any)
			};
			var traverse = Traverse.Create(ArtifactSelector.Instance);
			traverse.Field("placedArtifacts").SetValue(placed);
			traverse.Field("analyzedArtifactCount").SetValue(state.AnalyzedTerrestrialCount);
			traverse.Field("analyzedSpaceArtifactCount").SetValue(state.AnalyzedSpaceCount);
			traverse.Field("analyzedArtifatIDs").SetValue(new List<string>(state.AnalyzedIds));
			return true;
		}

		private static List<string> Copy(
			Dictionary<ArtifactType, List<string>> source,
			ArtifactType type)
			=> source != null && source.TryGetValue(type, out var ids) ? new List<string>(ids) : new();

		private static NetworkIdentity EnsureIdentity(GameObject go)
		{
			NetworkIdentity identity = EnsurePersistentIdentity(go);
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			return identity;
		}

		private static ulong EnsureLifecycle(NetworkIdentity identity)
		{
			if (identity == null || identity.NetId == 0)
				return 0;
			if (identity.LifecycleRevision == 0)
				identity.LifecycleRevision = NetworkIdentityRegistry.BeginLifecycle(identity.NetId);
			return identity.LifecycleRevision;
		}

		internal static void Broadcast(ArtifactPOIStates.Instance smi)
		{
			if (MultiplayerSession.IsHostInSession && TryCapture(smi, out var packet))
				PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}
	}

	[HarmonyPatch(typeof(ArtifactPOIStates), nameof(ArtifactPOIStates.SpawnArtifactOnHexCellIfFullyCharged))]
	internal static class ArtifactPoiSpawnPatch
	{
		internal static bool Prefix()
			=> ArtifactPoiSync.ShouldRunGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(ArtifactPOIStates.Instance smi) => ArtifactPoiSync.Broadcast(smi);
	}

	[HarmonyPatch(typeof(ArtifactPOIStates.Instance), nameof(ArtifactPOIStates.Instance.RechargePOI))]
	internal static class ArtifactPoiRechargePatch
	{
		internal static bool Prefix()
			=> ArtifactPoiSync.ShouldRunGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(ArtifactPOIStates.Instance __instance)
			=> ArtifactPoiSync.Broadcast(__instance);
	}
}
