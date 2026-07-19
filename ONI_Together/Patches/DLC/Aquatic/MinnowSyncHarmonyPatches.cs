using System;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Aquatic
{
	[HarmonyPatch(typeof(MinnowImperativePOIStates.Instance), nameof(MinnowImperativePOIStates.Instance.StartSM))]
	internal static class MinnowPoiStartPatch
	{
		internal static void Postfix(MinnowImperativePOIStates.Instance __instance) => MinnowPoiSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(MinnowImperativePOIStates.Instance), nameof(MinnowImperativePOIStates.Instance.OnSidescreenButtonPressed))]
	internal static class MinnowPoiButtonPatch
	{
		internal static bool Prefix(MinnowImperativePOIStates.Instance __instance)
		{
			if (!MultiplayerSession.IsClient) return true;
			int netId = MinnowPoiSync.EnsureNetId(__instance.gameObject);
			if (netId != 0)
				PacketSender.SendToAllOtherPeers(new MinnowPoiRequestPacket
				{
					TargetNetId = netId,
					Operation = MinnowPoiOperation.ToggleDelivery
				});
			return false;
		}

		internal static void Postfix(MinnowImperativePOIStates.Instance __instance) => MinnowPoiSync.SendState(__instance);
	}

	[HarmonyPatch]
	internal static class MinnowQuestPopupPatch
	{
		internal static MethodBase TargetMethod() => MinnowPoiSync.ResolveShowQuestPopupMethod();

		internal static void Prefix(MinnowImperativePOIStates.Instance __instance)
		{
			if (!MultiplayerSession.IsClient) return;
			if (MultiplayerSession.InSession)
			{
				int netId = MinnowPoiSync.EnsureNetId(__instance.gameObject);
				if (netId != 0)
					PacketSender.SendToAllOtherPeers(new MinnowPoiRequestPacket
					{
						TargetNetId = netId,
						Operation = MinnowPoiOperation.Discover
					});
			}
		}

		internal static void Postfix(MinnowImperativePOIStates.Instance __instance)
		{
			if (MultiplayerSession.IsHostInSession)
				MinnowPoiSync.MarkDiscovered(__instance);
			MinnowPoiSync.SendState(__instance);
		}
	}

	[HarmonyPatch]
	internal static class MinnowCompletionPatch
	{
		internal static MethodBase TargetMethod() => MinnowPoiSync.ResolveCompletionAcknowledgedMethod();

		internal static bool Prefix(MinnowImperativePOIStates.Instance __instance)
		{
			if (!MultiplayerSession.IsClient) return true;
			__instance.ClearCompletedNotification();
			int netId = MinnowPoiSync.EnsureNetId(__instance.gameObject);
			if (netId != 0)
				PacketSender.SendToAllOtherPeers(new MinnowPoiRequestPacket
				{
					TargetNetId = netId,
					Operation = MinnowPoiOperation.AcknowledgeCompletion
				});
			return false;
		}

		internal static void Postfix(MinnowImperativePOIStates.Instance __instance) => MinnowPoiSync.SendState(__instance);
	}

	[HarmonyPatch]
	internal static class MinnowEnoughMassPatch
	{
		internal static MethodBase TargetMethod() => MinnowPoiSync.ResolveHasEnoughMassMethod();

		internal static bool Prefix(ref bool __result)
		{
			if (MinnowPoiSync.ShouldRunGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost)) return true;
			__result = false;
			return false;
		}
	}

	[HarmonyPatch]
	internal static class MinnowRewardAuthorityPatch
	{
		internal static MethodBase TargetMethod() => MinnowPoiSync.ResolveSpawnRewardMethod();

		internal static bool Prefix()
			=> MinnowPoiSync.ShouldRunGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);
	}

	[HarmonyPatch]
	internal static class MinnowAchievementAuthorityPatch
	{
		internal static MethodBase TargetMethod() => MinnowPoiSync.ResolveUnlockAchievementMethod();

		internal static bool Prefix()
			=> MinnowPoiSync.ShouldRunGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);
	}

	[HarmonyPatch]
	internal static class MinnowSpawnPatch
	{
		internal static MethodBase TargetMethod() => MinnowPoiSync.ResolveSpawnMinnowMethod();

		internal static bool Prefix(
			MinnowImperativePOIStates.Instance __instance, out bool __state)
		{
			bool run = MinnowPoiSync.ShouldRunGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);
			__state = run && MultiplayerSession.IsHostInSession;
			if (__state)
			{
				NetworkIdentity.BeginManagedSpawn();
				try { MinnowPoiSync.BeginMinnowCapture(__instance); }
				catch
				{
					NetworkIdentity.EndManagedSpawn();
					__state = false;
					throw;
				}
			}
			return run;
		}

		internal static void Postfix(
			MinnowImperativePOIStates.Instance __instance, ref bool __state)
		{
			if (__state)
			{
				NetworkIdentity.EndManagedSpawn();
				__state = false;
				MinnowPoiSync.RearmMinnowSpawnBroadcast(__instance);
			}
			MinnowPoiSync.FinishMinnowCapture(__instance);
		}

		internal static Exception Finalizer(
			Exception __exception, MinnowImperativePOIStates.Instance __instance, bool __state)
		{
			if (!__state)
				return __exception;
			try
			{
				if (__exception != null) MinnowPoiSync.ClearMinnowCapture(__instance);
			}
			finally { NetworkIdentity.EndManagedSpawn(); }
			return __exception;
		}
	}

	[HarmonyPatch(typeof(MinionStartingStats), nameof(MinionStartingStats.Apply), typeof(GameObject))]
	internal static class MinnowStatsCapturePatch
	{
		internal static void Postfix(MinionStartingStats __instance, GameObject __0)
			=> MinnowPoiSync.CaptureMinnow(__instance, __0);
	}

	[HarmonyPatch(typeof(MinnowImperativePOIStates.Instance), nameof(MinnowImperativePOIStates.Instance.ShowCompletedNotification))]
	internal static class MinnowNotificationPatch
	{
		internal static void Postfix(MinnowImperativePOIStates.Instance __instance) => MinnowPoiSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(MinnowImperativePOIStates.Instance), nameof(MinnowImperativePOIStates.Instance.SetSelectable))]
	internal static class MinnowSelectablePatch
	{
		internal static void Postfix(MinnowImperativePOIStates.Instance __instance) => MinnowPoiSync.SendState(__instance);
	}
}
