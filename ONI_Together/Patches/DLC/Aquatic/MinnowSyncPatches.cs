using System;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using ONI_Together.Networking.Packets.Social;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Aquatic
{
	internal static class MinnowPoiSync
	{
		private static MinnowImperativePOIStates.Instance CapturingPoi;
		private static MinionStartingStats CapturedStats;
		private static GameObject CapturedMinion;

		public static void ResetSessionState()
		{
			CapturingPoi = null;
			CapturedStats = null;
			CapturedMinion = null;
		}

		internal static bool ShouldRunGameplay(bool inSession, bool isHost) => !inSession || isHost;

		internal static MethodBase ResolveShowQuestPopupMethod()
			=> AccessTools.DeclaredMethod(typeof(MinnowImperativePOIStates.Instance), "ShowQuestPopup");

		internal static MethodBase ResolveCompletionAcknowledgedMethod()
			=> AccessTools.DeclaredMethod(typeof(MinnowImperativePOIStates.Instance), "OnCompletionPopupAcknowledged");

		internal static MethodBase ResolveSpawnMinnowMethod()
			=> AccessTools.DeclaredMethod(typeof(MinnowImperativePOIStates.Instance), "SpawnMinnow");

		internal static MethodBase ResolveSpawnRewardMethod()
			=> AccessTools.DeclaredMethod(typeof(MinnowImperativePOIStates), "SpawnReward");

		internal static MethodBase ResolveUnlockAchievementMethod()
			=> AccessTools.DeclaredMethod(typeof(MinnowImperativePOIStates), "UnlockWinAchievement");

		internal static MethodBase ResolveHasEnoughMassMethod()
			=> AccessTools.DeclaredMethod(typeof(MinnowImperativePOIStates), "HasEnoughMass");

		internal static bool TryHandleRequest(MinnowPoiRequestPacket request)
		{
			if (request == null || !request.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(request.TargetNetId, out NetworkIdentity identity))
				return false;
			MinnowImperativePOIStates.Instance poi = identity.gameObject.GetSMI<MinnowImperativePOIStates.Instance>();
			if (poi == null)
				return false;

			bool applied = request.Operation switch
			{
				MinnowPoiOperation.Discover => MarkDiscovered(poi),
				MinnowPoiOperation.ToggleDelivery => ToggleDelivery(poi),
				MinnowPoiOperation.AcknowledgeCompletion => CompleteWithoutCinematic(poi),
				_ => false
			};
			if (applied)
				SendState(poi);
			return applied;
		}

		internal static bool MarkDiscovered(MinnowImperativePOIStates.Instance poi)
		{
			if (poi == null || poi.sm.isCompleted.Get(poi) || poi.sm.hasShownQuestPopup.Get(poi))
				return false;
			poi.sm.hasShownQuestPopup.Set(true, poi);
			return true;
		}

		private static bool ToggleDelivery(MinnowImperativePOIStates.Instance poi)
		{
			if (!poi.sm.hasShownQuestPopup.Get(poi) || poi.sm.isCompleted.Get(poi))
				return false;
			bool enabled = poi.sm.hasClickedSideScreen.Get(poi);
			poi.sm.hasClickedSideScreen.Set(!enabled, poi);
			return true;
		}

		private static bool CompleteWithoutCinematic(MinnowImperativePOIStates.Instance poi)
		{
			if (!poi.sm.isCompleted.Get(poi) || poi.sm.hasShownCompletedPopup.Get(poi))
				return false;
			poi.ClearCompletedNotification();
			ResolveSpawnRewardMethod()?.Invoke(null, new object[] { poi });
			int completed = MinnowImperativePOIStates.GetPOICompletedCount();
			if (SaveGame.Instance?.ColonyAchievementTracker != null)
			{
				var tracker = SaveGame.Instance.ColonyAchievementTracker;
				tracker.minnowQuestsCompleted = Mathf.Max(tracker.minnowQuestsCompleted, completed);
				tracker.allMinnowQuestsCompleted = completed >= 3;
			}
			poi.sm.hasShownCompletedPopup.Set(true, poi);
			if (completed >= 3 && !MinnowExists())
				ResolveSpawnMinnowMethod()?.Invoke(poi, null);
			poi.GoTo(poi.sm.off_poi_completed);
			return true;
		}

		private static bool MinnowExists()
		{
			foreach (MinionIdentity identity in global::Components.LiveMinionIdentities.Items)
				if (identity.personalityResourceId == "MINNOW") return true;
			return false;
		}

		internal static void SendState(MinnowImperativePOIStates.Instance poi)
		{
			if (!MultiplayerSession.IsHostInSession || poi == null)
				return;
			MinnowPoiSnapshot snapshot = CaptureSnapshot(poi);
			PacketSender.SendToAllClients(new MinnowPoiStatePacket
			{
				TargetNetId = snapshot.TargetNetId,
				Phase = snapshot.Phase,
				HasShownQuestPopup = snapshot.HasShownQuestPopup,
				HasShownCompletedPopup = snapshot.HasShownCompletedPopup,
				IsCompleted = snapshot.IsCompleted,
				DeliveryEnabled = snapshot.DeliveryEnabled,
				QuestsCompleted = snapshot.QuestsCompleted,
				AllQuestsCompleted = snapshot.AllQuestsCompleted
			}, PacketSendMode.ReliableImmediate);
		}

		internal static bool TryApplyState(MinnowPoiStatePacket state)
		{
			if (state == null || !state.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(state.TargetNetId, out NetworkIdentity identity))
				return false;
			MinnowImperativePOIStates.Instance poi = identity.gameObject.GetSMI<MinnowImperativePOIStates.Instance>();
			if (poi == null)
				return false;
			var target = new MinnowPoiSnapshot(
				state.TargetNetId, state.Phase, state.HasShownQuestPopup,
				state.HasShownCompletedPopup, state.IsCompleted, state.DeliveryEnabled,
				state.QuestsCompleted, state.AllQuestsCompleted);
			if (!NeedsApply(CaptureSnapshot(poi), target))
				return true;

			poi.sm.hasShownQuestPopup.Set(state.HasShownQuestPopup, poi);
			poi.sm.isCompleted.Set(state.IsCompleted, poi);
			poi.sm.hasClickedSideScreen.Set(state.DeliveryEnabled, poi);
			poi.sm.hasShownCompletedPopup.Set(state.HasShownCompletedPopup, poi);
			ApplyGlobalProgress(state.QuestsCompleted, state.AllQuestsCompleted);
			ApplyPhase(poi, state.Phase);
			if (state.HasShownCompletedPopup)
				poi.ClearCompletedNotification();
			return true;
		}

		internal static bool NeedsApply(MinnowPoiSnapshot current, MinnowPoiSnapshot target)
			=> !current.Equals(target);

		private static MinnowPoiSnapshot CaptureSnapshot(MinnowImperativePOIStates.Instance poi)
		{
			int netId = EnsureNetId(poi.gameObject);
			var tracker = SaveGame.Instance?.ColonyAchievementTracker;
			return new MinnowPoiSnapshot(
				netId,
				GetPhase(poi),
				poi.sm.hasShownQuestPopup.Get(poi),
				poi.sm.hasShownCompletedPopup.Get(poi),
				poi.sm.isCompleted.Get(poi),
				poi.sm.hasClickedSideScreen.Get(poi),
				tracker?.minnowQuestsCompleted ?? MinnowImperativePOIStates.GetPOICompletedCount(),
				tracker?.allMinnowQuestsCompleted ?? false);
		}

		private static MinnowPoiPhase GetPhase(MinnowImperativePOIStates.Instance poi)
		{
			if (poi.IsInsideState(poi.sm.off_poi_completed)) return MinnowPoiPhase.Completed;
			if (poi.IsInsideState(poi.sm.poi_completed_acknowledged)) return MinnowPoiPhase.CompletionAcknowledged;
			if (poi.IsInsideState(poi.sm.poi_completed_pending)) return MinnowPoiPhase.CompletionPending;
			if (poi.IsInsideState(poi.sm.on.working)) return MinnowPoiPhase.Working;
			if (poi.IsInsideState(poi.sm.on)) return MinnowPoiPhase.Waiting;
			return MinnowPoiPhase.Off;
		}

		private static void ApplyPhase(MinnowImperativePOIStates.Instance poi, MinnowPoiPhase phase)
		{
			StateMachine.BaseState target = phase switch
			{
				MinnowPoiPhase.Waiting => poi.sm.on.waiting,
				MinnowPoiPhase.Working => poi.sm.on.working,
				MinnowPoiPhase.CompletionPending => poi.sm.poi_completed_pending,
				MinnowPoiPhase.CompletionAcknowledged => poi.sm.poi_completed_acknowledged,
				MinnowPoiPhase.Completed => poi.sm.off_poi_completed,
				_ => poi.sm.off
			};
			if (!poi.IsInsideState(target))
				poi.GoTo(target);
		}

		private static void ApplyGlobalProgress(int completed, bool allCompleted)
		{
			if (SaveGame.Instance?.ColonyAchievementTracker == null)
				return;
			var tracker = SaveGame.Instance.ColonyAchievementTracker;
			tracker.minnowQuestsCompleted = completed;
			tracker.allMinnowQuestsCompleted = allCompleted;
		}

		internal static void BeginMinnowCapture(MinnowImperativePOIStates.Instance poi)
		{
			CapturingPoi = poi;
			CapturedStats = null;
			CapturedMinion = null;
		}

		internal static void CaptureMinnow(MinionStartingStats stats, GameObject minion)
		{
			if (CapturingPoi == null || stats?.personality?.Id != "MINNOW" || minion == null)
				return;
			CapturedStats = stats;
			CapturedMinion = minion;
		}

		internal static void FinishMinnowCapture(MinnowImperativePOIStates.Instance poi)
		{
			try
			{
				if (poi != CapturingPoi || CapturedStats == null || CapturedMinion == null)
					return;
				NetworkIdentity minionIdentity = CapturedMinion.AddOrGet<NetworkIdentity>();
				minionIdentity.RegisterIdentity();
				minionIdentity.EnsureAuthoritativeSpawnBroadcast();
				MinionResume resume = CapturedMinion.GetComponent<MinionResume>();
				var packet = new MinnowSpawnStatePacket
				{
					SourceNetId = EnsureNetId(poi.gameObject),
					MinionNetId = minionIdentity.NetId,
					LifecycleRevision = EnsureLifecycle(minionIdentity),
					Position = CapturedMinion.transform.GetPosition(),
					ArrivalTime = CapturedMinion.GetComponent<MinionIdentity>().arrivalTime,
					SkillPoints = resume?.AvailableSkillpoints ?? 0,
					EntityData = ImmigrantOptionEntry.FromGameDeliverable(CapturedStats)
				};
				if (packet.IsWireValid())
					PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
			}
			finally
			{
				ClearMinnowCapture(poi);
			}
		}

		internal static void ClearMinnowCapture(MinnowImperativePOIStates.Instance poi)
		{
			if (poi != CapturingPoi)
				return;
			CapturingPoi = null;
			CapturedStats = null;
			CapturedMinion = null;
		}

		internal static void RearmMinnowSpawnBroadcast(MinnowImperativePOIStates.Instance poi)
		{
			if (poi == CapturingPoi && CapturedMinion != null)
				CapturedMinion.AddOrGet<NetworkIdentity>().RearmAuthoritativeSpawnBroadcast();
		}

		internal static bool TryApplyMinnowSpawn(MinnowSpawnStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    packet.EntityData.ToGameDeliverable() is not MinionStartingStats stats)
				return false;
			ulong current = NetworkIdentityRegistry.GetLastLifecycleRevision(packet.MinionNetId);
			if (!MinnowSpawnStatePacket.CanApplyLifecycle(current,
				    NetworkIdentityRegistry.IsLifecycleTombstoned(packet.MinionNetId), packet.LifecycleRevision))
				return false;
			bool exists = NetworkIdentityRegistry.TryGet(packet.MinionNetId, out NetworkIdentity identity);
			if (exists && current < packet.LifecycleRevision)
			{
				NetworkIdentityRegistry.Unregister(identity, packet.MinionNetId);
				Util.KDestroyGameObject(identity.gameObject);
				exists = false;
			}
			GameObject minion = exists ? identity.gameObject : CreateMinnow(stats, packet.Position);
			if (minion == null || !ApplyMinnowState(minion, stats, packet) ||
			    !NetworkIdentityRegistry.TryBindAuthoritativeLifecycle(
				    minion, packet.MinionNetId, packet.LifecycleRevision))
			{
				if (!exists && minion != null) Util.KDestroyGameObject(minion);
				return false;
			}
			ApplyGlobalProgress(3, true);
			return true;
		}

		private static GameObject CreateMinnow(MinionStartingStats stats, Vector3 position)
		{
			GameObject prefab = Assets.GetPrefab(BaseMinionConfig.GetMinionIDForModel(stats.personality.model));
			if (prefab == null)
				return null;
			GameObject minion = Util.KInstantiate(prefab);
			minion.name = prefab.name;
			Immigration.Instance.ApplyDefaultPersonalPriorities(minion);
			minion.transform.SetLocalPosition(position);
			minion.SetActive(true);
			return minion;
		}

		private static bool ApplyMinnowState(
			GameObject minion, MinionStartingStats stats, MinnowSpawnStatePacket packet)
		{
			MinionIdentity identity = minion.GetComponent<MinionIdentity>();
			MinionResume resume = minion.GetComponent<MinionResume>();
			if (identity == null || resume == null)
				return false;
			minion.transform.SetLocalPosition(packet.Position);
			if (identity.personalityResourceId != "MINNOW")
				stats.Apply(minion);
			if (resume.TotalSkillPointsGained != packet.SkillPoints)
				resume.ForceSetSkillPoints(packet.SkillPoints);
			identity.arrivalTime = packet.ArrivalTime;
			minion.GetMyWorld().SetDupeVisited();
			return true;
		}

		internal static int EnsureNetId(GameObject gameObject)
		{
			NetworkIdentity identity = gameObject.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0) identity.RegisterIdentity();
			return identity.NetId;
		}

		private static ulong EnsureLifecycle(NetworkIdentity identity)
		{
			if (identity?.NetId == 0)
				return 0;
			if (identity.LifecycleRevision == 0)
				identity.LifecycleRevision = NetworkIdentityRegistry.BeginLifecycle(identity.NetId);
			return identity.LifecycleRevision;
		}
	}

}
