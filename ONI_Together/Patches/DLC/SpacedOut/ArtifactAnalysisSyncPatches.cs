using System.Collections;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using UnityEngine;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class ArtifactAnalysisSync
	{
		internal static bool ShouldRunCompletion(bool inSession, bool isHost)
			=> !inSession || isHost;

		internal static bool NeedsApply(int appliedRevision, int incomingRevision)
			=> incomingRevision > appliedRevision;

		internal static void Broadcast(
			ArtifactAnalysisStationWorkable workable,
			GameObject artifact,
			bool isWorking)
		{
			if (!MultiplayerSession.IsHostInSession || workable == null)
				return;
			ArtifactSelectorStateData selector = ArtifactPoiSync.CaptureSelector();
			if (selector?.IsWireValid() != true)
				return;
			NetworkIdentity stationIdentity = workable.gameObject.AddOrGet<NetworkIdentity>();
			stationIdentity.RegisterIdentity();
			NetworkIdentity artifactIdentity = artifact?.AddOrGet<NetworkIdentity>();
			artifactIdentity?.RegisterIdentity();
			artifactIdentity?.EnsureAuthoritativeSpawnBroadcast();
			WorkerBase worker = isWorking ? workable.GetWorker() : null;
			int workerNetId = worker?.gameObject.GetNetIdentity()?.NetId ?? 0;
			if (stationIdentity.NetId == 0 || (artifact != null && artifactIdentity?.NetId == 0) ||
			    (isWorking && workerNetId == 0))
				return;
			ArtifactAnalysisSyncMarker marker = workable.gameObject.AddOrGet<ArtifactAnalysisSyncMarker>();
			var packet = new ArtifactAnalysisStatePacket
			{
				StationNetId = stationIdentity.NetId,
				Revision = ++marker.Revision,
				WorkerNetId = workerNetId,
				WorkTimeRemaining = workable.WorkTimeRemaining,
				ArtifactNetId = artifactIdentity?.NetId ?? 0,
				ArtifactId = artifact?.PrefabID().ToString() ?? "",
				ArtifactCharmed = artifact?.HasTag(GameTags.CharmedArtifact) == true,
				TerrestrialArtifact = artifact?.HasTag(GameTags.TerrestrialArtifact) == true,
				Selector = selector
			};
			if (packet.IsWireValid())
				PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}

		internal static void ApplyOrRetry(ArtifactAnalysisStatePacket packet)
		{
			if (!TryApply(packet) && Game.Instance != null)
				Game.Instance.StartCoroutine(Retry(packet));
		}

		private static bool TryApply(ArtifactAnalysisStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGetComponent(
				    packet.StationNetId, out ArtifactAnalysisStationWorkable workable))
				return false;
			ArtifactAnalysisSyncMarker marker = workable.gameObject.AddOrGet<ArtifactAnalysisSyncMarker>();
			if (!NeedsApply(marker.AppliedRevision, packet.Revision))
				return true;
			GameObject artifact = null;
			if (packet.ArtifactNetId != 0)
			{
				if (!NetworkIdentityRegistry.TryGet(packet.ArtifactNetId, out NetworkIdentity identity))
					return false;
				artifact = identity.gameObject;
				if (artifact.PrefabID().ToString() != packet.ArtifactId)
					return false;
			}
			WorkerBase desiredWorker = null;
			if (packet.WorkerNetId != 0 &&
			    !NetworkIdentityRegistry.TryGetComponent(packet.WorkerNetId, out desiredWorker))
				return false;
			if (desiredWorker != null && (artifact == null || !workable.storage.items.Contains(artifact)))
				return false;

			if (artifact != null && !ArtifactEntitySync.TryApply(packet.ArtifactNetId,
				    packet.ArtifactId, packet.ArtifactCharmed, packet.TerrestrialArtifact))
				return false;
			if (!ArtifactPoiSync.ApplySelector(packet.Selector))
				return false;
			ApplyWorker(workable, desiredWorker);
			workable.WorkTimeRemaining = packet.WorkTimeRemaining;
			marker.AppliedRevision = packet.Revision;
			return true;
		}

		private static void ApplyWorker(
			ArtifactAnalysisStationWorkable workable,
			WorkerBase desiredWorker)
		{
			WorkerBase current = workable.GetWorker();
			if (current == desiredWorker)
				return;
			current?.StopWork();
			if (desiredWorker == null)
				return;
			if (desiredWorker.GetWorkable() != null)
				desiredWorker.StopWork();
			desiredWorker.StartWork(new WorkerBase.StartWorkInfo(workable));
		}

		private static GameObject FindStoredArtifact(ArtifactAnalysisStationWorkable workable)
		{
			if (workable?.storage?.items == null)
				return null;
			foreach (GameObject item in workable.storage.items)
				if (item?.GetComponent<SpaceArtifact>() != null)
					return item;
			return null;
		}

		private static IEnumerator Retry(ArtifactAnalysisStatePacket packet)
		{
			for (int attempt = 0; attempt < 12; attempt++)
			{
				yield return null;
				if (!MultiplayerSession.InSession || MultiplayerSession.IsHost || TryApply(packet))
					yield break;
			}
		}

		internal static GameObject CaptureArtifact(ArtifactAnalysisStationWorkable workable)
			=> FindStoredArtifact(workable);
	}

	internal sealed class ArtifactAnalysisSyncMarker : KMonoBehaviour
	{
		internal int Revision;
		internal int AppliedRevision = -1;
	}

	[HarmonyPatch(typeof(Workable), nameof(Workable.StartWork), typeof(WorkerBase))]
	internal static class ArtifactAnalysisStartWorkPatch
	{
		internal static void Postfix(Workable __instance)
		{
			if (__instance is ArtifactAnalysisStationWorkable workable)
				ArtifactAnalysisSync.Broadcast(
					workable, ArtifactAnalysisSync.CaptureArtifact(workable), true);
		}
	}

	[HarmonyPatch(typeof(Workable), nameof(Workable.StopWork), typeof(WorkerBase), typeof(bool))]
	internal static class ArtifactAnalysisStopWorkPatch
	{
		internal static void Postfix(Workable __instance)
		{
			if (__instance is ArtifactAnalysisStationWorkable workable)
				ArtifactAnalysisSync.Broadcast(
					workable, ArtifactAnalysisSync.CaptureArtifact(workable), false);
		}
	}

	[HarmonyPatch(typeof(Workable), nameof(Workable.CompleteWork), typeof(WorkerBase))]
	internal static class ArtifactAnalysisCompleteWorkPatch
	{
		internal static bool Prefix(Workable __instance, out GameObject __state)
		{
			__state = __instance is ArtifactAnalysisStationWorkable workable
				? ArtifactAnalysisSync.CaptureArtifact(workable) : null;
			return __instance is not ArtifactAnalysisStationWorkable ||
			       ArtifactAnalysisSync.ShouldRunCompletion(
				       MultiplayerSession.InSession, MultiplayerSession.IsHost);
		}

		internal static void Postfix(Workable __instance, GameObject __state)
		{
			if (__instance is ArtifactAnalysisStationWorkable workable)
				ArtifactAnalysisSync.Broadcast(workable, __state, false);
		}
	}
}
