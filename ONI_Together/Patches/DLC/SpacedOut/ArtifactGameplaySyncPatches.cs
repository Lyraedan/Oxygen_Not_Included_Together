using System.Collections;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using UnityEngine;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class ArtifactGameplaySync
	{
		internal static bool ShouldRunAuthoritative(bool inSession, bool isHost, bool isArtifactSystem)
			=> !isArtifactSystem || !inSession || isHost;

		internal static bool GetPedestalSpawnGuardValue(
			bool original,
			bool inSession,
			bool isHost)
			=> inSession && !isHost || original;

		internal static bool NeedsPedestalAttachment(bool stored, bool occupantMatches)
			=> !stored || !occupantMatches;

		internal static void BroadcastPedestal(PedestalArtifactSpawner spawner)
		{
			if (!MultiplayerSession.IsHostInSession || spawner == null)
				return;
			bool spawned = Traverse.Create(spawner).Field("artifactSpawned").GetValue<bool>();
			GameObject artifact = FindArtifact(spawner.GetComponent<Storage>());
			SendSpawnState(spawner.gameObject, ArtifactSpawnSource.Pedestal, spawned, artifact);
		}

		internal static void SpawnSatelliteArtifact(GameObject satellite)
		{
			if (satellite == null || ArtifactSelector.Instance == null)
				return;
			string id = ArtifactSelector.Instance.GetUniqueArtifactID();
			GameObject prefab = Assets.GetPrefab(id);
			GameObject artifact = prefab == null ? null : Util.KInstantiate(prefab, satellite.transform.position);
			if (artifact == null)
				return;
			artifact.GetComponent<KPrefabID>()?.AddTag(GameTags.TerrestrialArtifact, serialize: true);
			artifact.SetActive(true);
			SendSpawnState(satellite, ArtifactSpawnSource.Satellite, true, artifact);
		}

		internal static void BroadcastOneTime(ClusterGridOneTimeResourceSpawner.Instance spawner)
		{
			ArtifactPOIStates.Instance poi = spawner?.gameObject.GetSMI<ArtifactPOIStates.Instance>();
			if (!MultiplayerSession.IsHostInSession || poi == null)
				return;
			NetworkIdentity identity = spawner.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			if (identity.NetId == 0)
				return;
			PacketSender.SendToAllClients(new ArtifactPoiOneTimeStatePacket
			{
				PoiNetId = identity.NetId,
				HasSpawnedResources = spawner.sm.HasSpawnedResources.Get(spawner)
			}, PacketSendMode.ReliableImmediate);
			ArtifactPoiSync.Broadcast(poi);
		}

		internal static void ApplyOrRetry(ArtifactSpawnStatePacket packet)
		{
			if (!TryApply(packet) && Game.Instance != null)
				Game.Instance.StartCoroutine(Retry(packet));
		}

		internal static void ApplyOrRetry(ArtifactPoiOneTimeStatePacket packet)
		{
			if (!TryApply(packet) && Game.Instance != null)
				Game.Instance.StartCoroutine(Retry(packet));
		}

		private static bool TryApply(ArtifactSpawnStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(packet.SourceNetId, out NetworkIdentity source))
				return false;
			PedestalArtifactSpawner pedestal = null;
			if (packet.Source == ArtifactSpawnSource.Pedestal)
			{
				pedestal = source.GetComponent<PedestalArtifactSpawner>();
				if (pedestal == null)
					return false;
			}
			else if (source.GetComponent<SetLocker>() == null ||
			         source.gameObject.PrefabID().ToString() != PropSurfaceSatellite3Config.ID)
			{
				return false;
			}
			GameObject artifact = null;
			if (packet.Spawned)
			{
				if (!NetworkIdentityRegistry.TryGet(packet.ArtifactNetId, out NetworkIdentity identity) ||
				    identity.gameObject.PrefabID().ToString() != packet.ArtifactId ||
				    identity.GetComponent<SpaceArtifact>() == null ||
				    identity.GetComponent<KPrefabID>() == null)
					return false;
				artifact = identity.gameObject;
			}
			if (!ArtifactPoiSync.ApplySelector(packet.Selector))
				return false;
			if (pedestal != null)
				Traverse.Create(pedestal).Field("artifactSpawned").SetValue(packet.Spawned);
			if (!packet.Spawned)
				return true;
			if (!ArtifactEntitySync.TryApply(packet.ArtifactNetId, packet.ArtifactId,
				    packet.ArtifactCharmed, packet.TerrestrialArtifact))
				return false;
			return pedestal == null || AttachPedestalArtifact(pedestal, artifact);
		}

		private static bool AttachPedestalArtifact(
			PedestalArtifactSpawner pedestal,
			GameObject artifact)
		{
			Storage storage = pedestal.GetComponent<Storage>();
			SingleEntityReceptacle receptacle = pedestal.GetComponent<SingleEntityReceptacle>();
			if (storage?.items == null || receptacle == null || artifact == null)
				return false;
			bool stored = storage.items.Contains(artifact);
			bool occupantMatches = receptacle.Occupant == artifact;
			if (!NeedsPedestalAttachment(stored, occupantMatches))
				return true;
			if (receptacle.Occupant != null)
				receptacle.OrderRemoveOccupant();
			if (!storage.items.Contains(artifact))
				storage.Store(artifact);
			if (!storage.items.Contains(artifact))
				return false;
			receptacle.ForceDeposit(artifact);
			return storage.items.Contains(artifact) && receptacle.Occupant == artifact;
		}

		private static bool TryApply(ArtifactPoiOneTimeStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(packet.PoiNetId, out NetworkIdentity identity))
				return false;
			ClusterGridOneTimeResourceSpawner.Instance spawner =
				identity.gameObject.GetSMI<ClusterGridOneTimeResourceSpawner.Instance>();
			if (spawner == null || identity.gameObject.GetSMI<ArtifactPOIStates.Instance>() == null)
				return false;
			if (spawner.sm.HasSpawnedResources.Get(spawner) != packet.HasSpawnedResources)
				spawner.sm.HasSpawnedResources.Set(packet.HasSpawnedResources, spawner);
			return true;
		}

		private static void SendSpawnState(
			GameObject source,
			ArtifactSpawnSource sourceKind,
			bool spawned,
			GameObject artifact)
		{
			ArtifactSelectorStateData selector = ArtifactPoiSync.CaptureSelector();
			if (!MultiplayerSession.IsHostInSession || source == null ||
			    spawned != (artifact != null) || selector?.IsWireValid() != true)
				return;
			NetworkIdentity sourceIdentity = source.AddOrGet<NetworkIdentity>();
			sourceIdentity.RegisterIdentity();
			NetworkIdentity artifactIdentity = artifact?.AddOrGet<NetworkIdentity>();
			artifactIdentity?.RegisterIdentity();
			artifactIdentity?.EnsureAuthoritativeSpawnBroadcast();
			if (sourceIdentity.NetId == 0 || (spawned && artifactIdentity?.NetId == 0))
				return;
			PacketSender.SendToAllClients(new ArtifactSpawnStatePacket
			{
				SourceNetId = sourceIdentity.NetId,
				Source = sourceKind,
				Spawned = spawned,
				ArtifactNetId = artifactIdentity?.NetId ?? 0,
				ArtifactId = artifact?.PrefabID().ToString() ?? "",
				ArtifactCharmed = artifact?.HasTag(GameTags.CharmedArtifact) == true,
				TerrestrialArtifact = artifact?.HasTag(GameTags.TerrestrialArtifact) == true,
				Selector = selector
			}, PacketSendMode.ReliableImmediate);
		}

		private static GameObject FindArtifact(Storage storage)
		{
			if (storage?.items == null)
				return null;
			foreach (GameObject item in storage.items)
				if (item?.GetComponent<SpaceArtifact>() != null)
					return item;
			return null;
		}

		private static IEnumerator Retry(ArtifactSpawnStatePacket packet)
		{
			for (int attempt = 0; attempt < 12; attempt++)
			{
				yield return null;
				if (!MultiplayerSession.InSession || MultiplayerSession.IsHost || TryApply(packet))
					yield break;
			}
		}

		private static IEnumerator Retry(ArtifactPoiOneTimeStatePacket packet)
		{
			for (int attempt = 0; attempt < 12; attempt++)
			{
				yield return null;
				if (!MultiplayerSession.InSession || MultiplayerSession.IsHost || TryApply(packet))
					yield break;
			}
		}
	}

	internal static class ArtifactEntitySync
	{
		internal static bool TryApply(int netId, string id, bool charmed, bool terrestrial)
		{
			if (!NetworkIdentityRegistry.TryGet(netId, out NetworkIdentity identity) ||
			    identity.gameObject.PrefabID().ToString() != id)
				return false;
			SpaceArtifact artifact = identity.GetComponent<SpaceArtifact>();
			KPrefabID prefab = identity.GetComponent<KPrefabID>();
			if (artifact == null || prefab == null)
				return false;
			if (terrestrial)
				prefab.AddTag(GameTags.TerrestrialArtifact, serialize: true);
			else
				prefab.RemoveTag(GameTags.TerrestrialArtifact);
			if (artifact.HasTag(GameTags.CharmedArtifact) == charmed)
				return true;
			if (!charmed)
			{
				artifact.RemoveCharm();
				return true;
			}
			prefab.AddTag(GameTags.CharmedArtifact, serialize: true);
			Traverse.Create(artifact).Field("loadCharmed").SetValue(true);
			artifact.UpdateStatusItem();
			Traverse.Create(artifact).Method("SetEntombedDecor").GetValue();
			Traverse.Create(artifact).Method("UpdateAnim").GetValue();
			return true;
		}
	}

	[HarmonyPatch(typeof(PedestalArtifactSpawner), "OnSpawn")]
	internal static class ArtifactPedestalSpawnPatch
	{
		internal static void Prefix(PedestalArtifactSpawner __instance, out bool __state)
		{
			var field = Traverse.Create(__instance).Field("artifactSpawned");
			__state = field.GetValue<bool>();
			field.SetValue(ArtifactGameplaySync.GetPedestalSpawnGuardValue(
				__state, MultiplayerSession.InSession, MultiplayerSession.IsHost));
		}

		internal static void Postfix(PedestalArtifactSpawner __instance, bool __state)
		{
			if (MultiplayerSession.InSession && !MultiplayerSession.IsHost)
				Traverse.Create(__instance).Field("artifactSpawned").SetValue(__state);
			ArtifactGameplaySync.BroadcastPedestal(__instance);
		}
	}

	[HarmonyPatch(typeof(PropSurfaceSatellite3Config), "OnLockerLooted", typeof(GameObject))]
	internal static class ArtifactSatelliteLootPatch
	{
		internal static bool Prefix(GameObject inst)
		{
			if (!MultiplayerSession.InSession)
				return true;
			if (MultiplayerSession.IsHost)
				ArtifactGameplaySync.SpawnSatelliteArtifact(inst);
			return false;
		}
	}

	[HarmonyPatch(typeof(ClusterGridOneTimeResourceSpawner.Instance),
		nameof(ClusterGridOneTimeResourceSpawner.Instance.SpawnResources))]
	internal static class ArtifactPoiOneTimeSpawnPatch
	{
		internal static bool Prefix(ClusterGridOneTimeResourceSpawner.Instance __instance)
			=> ArtifactGameplaySync.ShouldRunAuthoritative(
				MultiplayerSession.InSession,
				MultiplayerSession.IsHost,
				__instance?.gameObject.GetSMI<ArtifactPOIStates.Instance>() != null);

		internal static void Postfix(ClusterGridOneTimeResourceSpawner.Instance __instance)
			=> ArtifactGameplaySync.BroadcastOneTime(__instance);
	}
}
