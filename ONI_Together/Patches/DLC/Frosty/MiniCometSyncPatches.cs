using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.Frosty;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Frosty
{
	[HarmonyPatch(typeof(MiniCometConfig), "CreatePrefab")]
	internal static class MiniCometPrefabIdentityPatch
	{
		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	[HarmonyPatch(typeof(SpaceTreeSeedCometConfig), "CreatePrefab")]
	internal static class SpaceTreeSeedCometPrefabIdentityPatch
	{
		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	internal static class MiniCometSync
	{
		internal const int MaxPendingStates = 256;
		internal const int PendingRetryLimit = 20;

		private sealed class PendingState
		{
			internal MiniCometStatePacket Packet;
			internal int RetriesRemaining;
		}

		private static readonly Dictionary<int, PendingState> PendingStates = new();
		private static bool retryScheduled;
		private static int retryGeneration;
		internal static int PendingCount => PendingStates.Count;

		public static void ResetSessionState()
		{
			PendingStates.Clear();
			retryScheduled = false;
			retryGeneration++;
		}

		internal static bool TryCapture(MiniComet comet, out MiniCometStatePacket state)
		{
			state = null;
			NetworkIdentity identity = EnsureIdentity(comet?.gameObject);
			int targetNetId = identity?.NetId ?? 0;
			PrimaryElement primary = comet?.GetComponent<PrimaryElement>();
			KBatchedAnimController anim = comet?.GetComponent<KBatchedAnimController>();
			if (targetNetId == 0 || primary?.Element == null || anim == null)
				return false;

			state = new MiniCometStatePacket
			{
				TargetNetId = targetNetId,
				Position = comet.transform.GetPosition(),
				Offset = Traverse.Create(comet).Field("offsetPosition").GetValue<Vector3>(),
				Velocity = comet.Velocity,
				Rotation = anim.Rotation,
				Element = primary.Element.id,
				Mass = primary.Mass,
				Temperature = primary.Temperature,
				DiseaseIndex = primary.DiseaseIdx,
				DiseaseCount = Math.Max(0, primary.DiseaseCount),
				Targeted = comet.Targeted
			};
			return state.IsWireValid();
		}

		internal static void Receive(MiniCometStatePacket state)
		{
			if (TryApply(state))
			{
				PendingStates.Remove(state.TargetNetId);
				return;
			}
			QueuePending(state);
			ScheduleRetry();
		}

		internal static bool TryApply(MiniCometStatePacket state)
		{
			if (state == null || !state.IsWireValid() || ElementLoader.FindElementByHash(state.Element) == null)
				return false;

			return NetworkIdentityRegistry.TryGetComponent(state.TargetNetId, out MiniComet comet) &&
			       comet != null && ApplyState(comet, state);
		}

		internal static void QueuePending(MiniCometStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid())
				return;
			if (PendingStates.TryGetValue(packet.TargetNetId, out PendingState pending))
			{
				pending.Packet = packet;
				pending.RetriesRemaining = PendingRetryLimit;
				return;
			}
			if (PendingStates.Count >= MaxPendingStates)
				EvictPending();
			PendingStates[packet.TargetNetId] = new PendingState
			{
				Packet = packet,
				RetriesRemaining = PendingRetryLimit
			};
		}

		internal static void RetryPending()
		{
			foreach (var entry in new List<KeyValuePair<int, PendingState>>(PendingStates))
			{
				PendingState pending = entry.Value;
				if (TryApply(pending.Packet) || --pending.RetriesRemaining <= 0)
					PendingStates.Remove(entry.Key);
			}
			if (PendingStates.Count > 0)
				ScheduleRetry();
		}

		private static NetworkIdentity EnsureIdentity(GameObject gameObject)
		{
			if (gameObject == null)
				return null;
			NetworkIdentity identity = gameObject.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			identity.EnsureAuthoritativeSpawnBroadcast();
			return identity;
		}

		private static void EvictPending()
		{
			foreach (int netId in PendingStates.Keys)
			{
				PendingStates.Remove(netId);
				return;
			}
		}

		private static void ScheduleRetry()
		{
			if (retryScheduled || GameScheduler.Instance == null)
				return;
			retryScheduled = true;
			int generation = retryGeneration;
			GameScheduler.Instance.Schedule("MiniComet generic bind", 0.1f, _ =>
			{
				if (generation != retryGeneration)
					return;
				retryScheduled = false;
				RetryPending();
			});
		}

		private static bool ApplyState(MiniComet comet, MiniCometStatePacket state)
		{
			PrimaryElement primary = comet.GetComponent<PrimaryElement>();
			KBatchedAnimController anim = comet.GetComponent<KBatchedAnimController>();
			if (primary == null || anim == null)
				return false;

			FrostySyncGuard.Run(() =>
			{
				primary.SetElement(state.Element);
				primary.SetMassTemperature(state.Mass, state.Temperature);
				if (primary.DiseaseCount != 0)
					primary.ModifyDiseaseCount(-primary.DiseaseCount, "ONI Together mini comet sync");
				if (state.DiseaseCount > 0 && state.DiseaseIndex != byte.MaxValue)
					primary.AddDisease(state.DiseaseIndex, state.DiseaseCount, "ONI Together mini comet sync");
				comet.transform.SetPosition(state.Position);
				comet.Velocity = state.Velocity;
				comet.Targeted = state.Targeted;
				Traverse.Create(comet).Field("offsetPosition").SetValue(state.Offset);
				anim.Offset = state.Offset;
				anim.Rotation = state.Rotation;
			});
			return true;
		}
	}

	[HarmonyPatch(typeof(MiniComet), nameof(MiniComet.RandomizeVelocity))]
	internal static class MiniCometRandomVelocityPatch
	{
		internal static bool Prefix()
			=> FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsClient;
	}

	[HarmonyPatch(typeof(MiniComet), nameof(MiniComet.OnSpawn))]
	internal static class MiniCometSpawnPatch
	{
		internal static void Postfix(MiniComet __instance)
		{
			if (FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !MiniCometSync.TryCapture(__instance, out MiniCometStatePacket state))
				return;
			PacketSender.SendToAllClients(state, PacketSendMode.ReliableImmediate);
		}
	}

	[HarmonyPatch(typeof(MiniComet), "Explode", typeof(Vector3), typeof(int), typeof(int), typeof(Element))]
	internal static class MiniCometImpactPatch
	{
		internal static bool Prefix()
			=> FrostySyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsClient;
	}
}
