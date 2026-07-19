using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC;

namespace ONI_Together.Patches.DLC.Prehistoric
{
	internal static class LargeImpactorSync
	{
		internal sealed class OutcomeCapture
		{
			internal int EventId;
			internal int WorldId;
			internal readonly HashSet<int> ExistingDestinationIds = new();
			internal readonly List<LargeImpactorPoiOutcome> Pois = new();
		}

		// ponytail: gameplay events run on the Unity main thread; use per-event storage if Klei parallelizes them.
		internal static OutcomeCapture ActiveCapture;

		public static void ResetSessionState() => ActiveCapture = null;

		internal static bool ShouldRunAuthoritativeGameplay(bool inSession, bool isHost)
		{
			return !inSession || isHost;
		}

		internal static void SendState(LargeImpactorStatus.Instance impactor)
		{
			if (!MultiplayerSession.IsHostInSession || impactor?.eventInstance == null)
				return;

			PacketSender.SendToAllClients(new LargeImpactorStatePacket
			{
				EventId = impactor.eventInstance.eventID.HashValue,
				WorldId = impactor.eventInstance.worldId,
				Health = impactor.Health,
				HasArrived = impactor.sm.HasArrived.Get(impactor),
				Phase = LargeImpactorStatePacket.GetPhase(impactor)
			});
		}

		internal static OutcomeCapture BeginOutcome(LargeImpactorEvent.StatesInstance eventSmi)
		{
			if (!MultiplayerSession.IsHostInSession || eventSmi?.eventInstance == null)
				return null;

			var capture = new OutcomeCapture
			{
				EventId = eventSmi.eventInstance.eventID.HashValue,
				WorldId = eventSmi.eventInstance.worldId
			};
			if (SpacecraftManager.instance?.destinations != null)
			{
				foreach (var destination in SpacecraftManager.instance.destinations)
					capture.ExistingDestinationIds.Add(destination.id);
			}

			ActiveCapture = capture;
			return capture;
		}

		internal static void RecordPoi(string prefabId, AxialI location)
		{
			if (ActiveCapture == null)
				return;

			ActiveCapture.Pois.Add(new LargeImpactorPoiOutcome
			{
				PrefabId = prefabId,
				Q = location.q,
				R = location.r
			});
		}

		internal static void FinishOutcome(OutcomeCapture capture)
		{
			if (capture == null)
				return;

			try
			{
				var packet = new LargeImpactorOutcomePacket
				{
					EventId = capture.EventId,
					WorldId = capture.WorldId,
					Pois = capture.Pois
				};
				if (SpacecraftManager.instance?.destinations != null)
				{
					foreach (var destination in SpacecraftManager.instance.destinations)
					{
						if (!capture.ExistingDestinationIds.Contains(destination.id))
							packet.Destinations.Add(LargeImpactorDestinationData.FromDestination(destination));
					}
				}

				if (packet.Pois.Count > LargeImpactorOutcomePacket.MaxPoiCount ||
				    packet.Destinations.Count > LargeImpactorOutcomePacket.MaxDestinationCount)
				{
					DebugConsole.LogWarning("[LargeImpactorSync] Outcome exceeded packet caps");
					return;
				}

				PacketSender.SendToAllClients(packet);
			}
			catch (Exception e)
			{
				DebugConsole.LogWarning($"[LargeImpactorSync] Failed to capture authoritative outcome: {e.Message}");
			}
			finally
			{
				ClearOutcome(capture);
			}
		}

		internal static void ClearOutcome(OutcomeCapture capture)
		{
			if (ReferenceEquals(ActiveCapture, capture))
				ActiveCapture = null;
		}
	}

	[HarmonyPatch(typeof(LargeImpactorStatus.Instance), nameof(LargeImpactorStatus.Instance.DealDamage))]
	internal static class LargeImpactorDealDamagePatch
	{
		internal static bool Prefix()
		{
			return LargeImpactorSync.ShouldRunAuthoritativeGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);
		}

		internal static void Postfix(LargeImpactorStatus.Instance __instance)
		{
			LargeImpactorSync.SendState(__instance);
		}
	}

	[HarmonyPatch(typeof(LargeImpactorStatus.Instance), nameof(LargeImpactorStatus.Instance.StartSM))]
	internal static class LargeImpactorStartSmPatch
	{
		internal static void Postfix(LargeImpactorStatus.Instance __instance)
		{
			LargeImpactorSync.SendState(__instance);
		}
	}

	[HarmonyPatch(typeof(LargeImpactorStatus), "SetHasArrived")]
	internal static class LargeImpactorSetHasArrivedPatch
	{
		internal static void Postfix(LargeImpactorStatus.Instance __0)
		{
			LargeImpactorSync.SendState(__0);
		}
	}

	[HarmonyPatch(
		typeof(LargeImpactorStatus),
		"CheckArrivalUpdate",
		new[] { typeof(LargeImpactorStatus.Instance), typeof(float) })]
	internal static class LargeImpactorCheckArrivalUpdatePatch
	{
		internal static bool Prefix(ref bool __result)
		{
			if (LargeImpactorSync.ShouldRunAuthoritativeGameplay(
				    MultiplayerSession.InSession, MultiplayerSession.IsHost))
				return true;

			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(LargeImpactorEvent), nameof(LargeImpactorEvent.HandleInterception))]
	internal static class LargeImpactorHandleInterceptionPatch
	{
		internal static bool Prefix(
			LargeImpactorEvent.StatesInstance __0,
			out LargeImpactorSync.OutcomeCapture __state)
		{
			__state = null;
			if (!LargeImpactorSync.ShouldRunAuthoritativeGameplay(
				    MultiplayerSession.InSession, MultiplayerSession.IsHost))
				return false;

			__state = LargeImpactorSync.BeginOutcome(__0);
			return true;
		}

		internal static void Postfix(LargeImpactorSync.OutcomeCapture __state)
		{
			LargeImpactorSync.FinishOutcome(__state);
		}

		internal static Exception Finalizer(Exception __exception, LargeImpactorSync.OutcomeCapture __state)
		{
			if (__exception != null)
				LargeImpactorSync.ClearOutcome(__state);
			return __exception;
		}
	}

	[HarmonyPatch(typeof(LargeImpactorEvent), nameof(LargeImpactorEvent.SpawnPOI))]
	internal static class LargeImpactorSpawnPoiPatch
	{
		internal static bool Prefix()
		{
			return LargeImpactorSync.ShouldRunAuthoritativeGameplay(
				       MultiplayerSession.InSession, MultiplayerSession.IsHost) ||
			       LargeImpactorOutcomePacket.IsApplying;
		}

		internal static void Postfix(string __0, AxialI __1)
		{
			LargeImpactorSync.RecordPoi(__0, __1);
		}
	}
}
