using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components.Entities;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
using Shared.Profiling;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Patches.Navigation
{
	[HarmonyPatch(typeof(Navigator), nameof(Navigator.AdvancePath))]
	public static class NavigatorPatch
	{
		static bool Prefix(Navigator __instance)
		{
			using var _ = Profiler.Scope();

			if (NavigatorPatchUtil.AllowDefault(__instance, out var _))
				return true;

			if (MultiplayerSession.IsClient)
			{
				// Host-driven transitions can complete while Navigator still reports moving,
				// but transitionDriver has already been ended. If target is stale/non-null,
				// blocking AdvancePath here leaves the client stuck in moving forever with
				// no active transition updates. Force cleanup path only.
				if (__instance.IsMoving() && __instance.transitionDriver?.GetTransition == null)
				{
					__instance.target = null;
					return true;
				}

				// If target is already null, allow Stop/fail cleanup but keep blocking
				// local pathfinding for client authorization.
				if (__instance.target == null)
					return true;
			}

			return false;
		}
	}

	[HarmonyPatch(typeof(Navigator), nameof(Navigator.GoTo), new[] {
		typeof(KMonoBehaviour), typeof(CellOffset[]), typeof(NavTactic)
})]
	public static class Navigator_GoTo_Target_Patch
	{
		static bool Prefix(Navigator __instance)
		{
			using var _ = Profiler.Scope();

			return NavigatorPatchUtil.AllowDefault(__instance, out var _);
		}
	}

	[HarmonyPatch(typeof(Navigator), nameof(Navigator.BeginTransition))]
	public static class Navigator_BeginTransition_Patch
	{
		static void Postfix(Navigator __instance, NavGrid.Transition transition)
		{
			using var _ = Profiler.Scope();

			if (!NavigatorPatchUtil.AllowDefault(__instance, out var syncer) || syncer == null)
				return;

			var activeTransition = __instance.transitionDriver?.GetTransition;
			if (activeTransition == null)
				return;

			syncer.RequestSyncTransition(false, new NavigatorSyncer.Transition
			{
				Id = transition.id,
				StartPosition = __instance.transform.position,
				Speed = activeTransition.speed,
				AnimSpeed = activeTransition.animSpeed,
				StartNavType = (byte)transition.start
			});

			// var packet = new NavigatorTransitionPacket
			// {
			// 	NetId = identity.NetId,
			// 	Sequence = NavigatorPatchUtil.NextSequence(identity.NetId),
			// 	IsStop = false,
			// 	PosX = __instance.transform.position.x,
			// 	PosY = __instance.transform.position.y,
			// 	TransitionId = transition.id,
			// 	Speed = activeTransition.speed,
			// 	AnimSpeed = activeTransition.animSpeed,
			// 	StartNavType = (byte)transition.start,
			// 	EndNavType = (byte)transition.end
			// };

			// PacketSender.SendToAllClients(packet, sendType: PacketSendMode.Unreliable);
		}
	}

	[HarmonyPatch(typeof(Navigator), nameof(Navigator.Stop))]
	public static class Navigator_Stop_Patch
	{
		static void Postfix(Navigator __instance, bool arrived_at_destination, bool play_idle)
		{
			using var _ = Profiler.Scope();

			// Not sure why this is needed, keep it for now.
			if (!play_idle)
				return;

			if (!NavigatorPatchUtil.AllowDefault(__instance, out var syncer) || syncer == null)
				return;

			syncer.RequestSyncTransition(true, new NavigatorSyncer.Transition
			{
				StartPosition = __instance.transform.position,
				StartNavType = (byte)__instance.CurrentNavType
			});

			// var packet = new NavigatorTransitionPacket
			// {
			// 	NetId = identity.NetId,
			// 	Sequence = NavigatorPatchUtil.NextSequence(identity.NetId),
			// 	IsStop = true,
			// 	PosX = __instance.transform.position.x,
			// 	PosY = __instance.transform.position.y,
			// 	EndNavType = (byte)__instance.CurrentNavType
			// };

			// PacketSender.SendToAllClients(packet, sendType: PacketSendMode.Reliable);
		}
	}

	[HarmonyPatch(typeof(Navigator), "SimEveryTick")]
	public static class Navigator_ClientDrainPendingTransitions_Patch
	{
		static void Postfix(Navigator __instance)
		{
			using var _ = Profiler.Scope();

			// if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsHost)
			if (!MultiplayerSession.IsClient)
				return;

			if (!__instance.TryGetComponent<NavigatorSyncer>(out var syncer) || syncer == null)
				return;

			syncer.TryDispatchPending();
		}
	}

	internal static class NavigatorPatchUtil
	{
		public static bool AllowDefault(Navigator navigator, out NavigatorSyncer syncer)
		{
			using var _ = Profiler.Scope();

			syncer = null;

			if (navigator == null)
				return true;

			if (!MultiplayerSession.InActiveSession)
				return true;

			if (!navigator.TryGetComponent<KPrefabID>(out var prefabId) || prefabId == null)
				return true;

			if (!prefabId.HasTag(GameTags.BaseMinion) && !prefabId.HasTag(GameTags.Creature) && navigator.GetComponent<CreatureBrain>() == null)
				return true;

			if (!navigator.TryGetComponent<NavigatorSyncer>(out var sync))
			{
				DebugConsole.LogAssert($"[NavigatorPatchUtil] NavigatorSyncer is missing on {navigator.gameObject?.GetProperName()}");
				return true;
			}

			// The client machine should ignore their own navigation and use the host's authorization.
			if (MultiplayerSession.IsClient)
				return false;
			
			if (MultiplayerSession.IsHost && MultiplayerSession.SessionHasPlayers)
				syncer = sync;

			return true;
		}
	}
}