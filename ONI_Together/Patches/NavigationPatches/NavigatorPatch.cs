using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components.Entities;
using Shared.Profiling;
using System;

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

	[HarmonyPatch(typeof(Navigator), nameof(Navigator.GoTo), [typeof(KMonoBehaviour), typeof(CellOffset[]), typeof(NavTactic)])]
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

			syncer.RequestSyncTransition(
				false,
				new NavigatorSyncer.Transition
				{
					Id = transition.id,
					StartPosition = __instance.transform.position,
					Speed = activeTransition.speed,
					AnimSpeed = activeTransition.animSpeed,
					StartNavType = (byte)transition.start
				});
		}
	}

	[HarmonyPatch(typeof(Navigator), nameof(Navigator.Stop))]
	public static class Navigator_Stop_Patch
	{
		/*
		 * Vanilla successful-arrival flow:
		 *
		 * Stop(true, true)
		 *   -> cleanup
		 *   -> idle
		 *   -> normal.arrived
		 *       -> DestinationReached
		 *       -> nested Stop(false, true)
		 *       -> StartWork
		 *   -> return
		 *
		 * Publishing every Stop from Postfix produced:
		 *
		 * nested Stop RPC
		 * -> StartWork RPC
		 * -> outer Stop RPC
		 *
		 * The outer Stop RPC was therefore late and could replace the
		 * client's newly started work animation with Navigator idle.
		 *
		 * Instead, publish a successful arrival from its Prefix. This
		 * guarantees that its navigation Stop is sent before synchronous
		 * DestinationReached processing can reach StartWork.
		 *
		 * While the outer arrival Stop remains on the call stack, its
		 * NavigatorSyncer marks itself as being inside an arrival Stop.
		 * The nested Stop(false, true) is therefore not published.
		 */

		static void Prefix(
			Navigator __instance,
			bool arrived_at_destination,
			bool play_idle,
			out NavigatorSyncer __state)
		{
			__state = null;

			/*
			 * Only successful arrival needs special ordering.
			 *
			 * Non-arrival Stops retain the existing Postfix publication.
			 */
			if (!arrived_at_destination)
				return;

			if (!NavigatorPatchUtil.AllowDefault(__instance, out var syncer) || syncer == null)
				return;

			__state = syncer;

			/*
			 * Mark the complete outer Stop call as the arrival scope.
			 *
			 * DestinationReached and the nested Stop occur synchronously
			 * before this outer Stop returns.
			 */
			syncer.BeginArrivalStop();

			/*
			 * Preserve the old play_idle publication condition.
			 *
			 * If play_idle is false, we still retain the arrival scope so
			 * any synchronous nested cleanup can be identified, but there
			 * is no Stop RPC to publish.
			 */
			if (!play_idle)
				return;

			PublishStop(__instance, syncer);
		}

		static void Postfix(
			Navigator __instance,
			bool arrived_at_destination,
			bool play_idle,
			NavigatorSyncer __state)
		{
			using var _ = Profiler.Scope();

			try
			{
				/*
				 * Successful arrival was already handled by Prefix.
				 *
				 * Do not publish it again after DestinationReached /
				 * StartWork processing.
				 */
				if (arrived_at_destination)
					return;

				if (!play_idle)
					return;

				if (!NavigatorPatchUtil.AllowDefault(__instance, out var syncer) || syncer == null)
					return;

				/*
				 * A Stop(false, true) for this entity while its outer
				 * successful-arrival Stop is still active is the synchronous
				 * nested cleanup Stop.
				 *
				 * The outer arrival has already been published, so publishing
				 * this would create the duplicate Stop that caused the
				 * original ordering problem.
				 */
				if (syncer.IsInsideArrivalStop)
					return;

				/*
				 * Independent Stop(false, true) calls still publish exactly
				 * as before.
				 */
				PublishStop(__instance, syncer);
			}
			finally
			{
				/*
				 * Only the outer arrival Prefix sets __state.
				 *
				 * Nested Stop(false, true) calls have __state == null and
				 * therefore cannot close the outer scope.
				 */
				__state?.EndArrivalStop();
			}
		}

		/*
		 * Harmony Finalizer is needed because Postfix is not guaranteed to
		 * complete the scope if vanilla Stop throws.
		 *
		 * EndArrivalStop is safe to call again after the normal Postfix path
		 * because NavigatorSyncer clamps the depth at zero.
		 */
		static Exception Finalizer(Exception __exception, NavigatorSyncer __state)
		{
			if (__exception != null && __state != null)
				__state.EndArrivalStop();

			return __exception;
		}

		private static void PublishStop(Navigator navigator, NavigatorSyncer syncer)
		{
			syncer.RequestSyncTransition(
				true,
				new NavigatorSyncer.Transition
				{
					StartPosition = navigator.transform.position,
					StartNavType = (byte)navigator.CurrentNavType
				});
		}
	}

	[HarmonyPatch(typeof(Navigator), "SimEveryTick")]
	public static class Navigator_ClientDrainPendingTransitions_Patch
	{
		static void Postfix(Navigator __instance)
		{
			using var _ = Profiler.Scope();

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

			if (!prefabId.HasTag(GameTags.BaseMinion)
				&& !prefabId.HasTag(GameTags.Creature)
				&& navigator.GetComponent<CreatureBrain>() == null)
			{
				return true;
			}

			if (!navigator.TryGetComponent<NavigatorSyncer>(out var sync))
			{
				DebugConsole.LogAssert(
					$"[NavigatorPatchUtil] NavigatorSyncer is missing " +
					$"on {navigator.gameObject?.GetProperName()}");

				return true;
			}

			// The client machine should ignore its own navigation and use
			// the host's authorization.
			if (MultiplayerSession.IsClient)
				return false;

			if (MultiplayerSession.IsHost && MultiplayerSession.SessionHasPlayers)
				syncer = sync;

			return true;
		}
	}
}