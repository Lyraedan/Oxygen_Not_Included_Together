using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Bionic;

namespace ONI_Together.Patches.DLC.Bionic
{
	[HarmonyPatch(typeof(RemoteWorkTerminal), nameof(RemoteWorkTerminal.FutureDock), MethodType.Setter)]
	internal static class RemoteWorkerDockSelectionPatch
	{
		internal static bool Prefix(RemoteWorkTerminal __instance, RemoteWorkerDock value)
		{
			if (!MultiplayerSession.IsClient || RemoteWorkerDockSelectionSync.IsApplying)
				return true;
			RemoteWorkerDockSelectionSync.SendRequest(__instance, value);
			return false;
		}

		internal static void Postfix(RemoteWorkTerminal __instance)
		{
			if (!RemoteWorkerDockSelectionSync.IsApplying)
				RemoteWorkerDockSelectionSync.Broadcast(__instance);
		}
	}

	[HarmonyPatch(typeof(RemoteWorkerDock), "RequestNewWorker")]
	internal static class RemoteWorkerDockRequestWorkerPatch
	{
		internal static bool Prefix() => RemoteWorkerDockSync.ShouldRunGameplay(
			MultiplayerSession.InSession, MultiplayerSession.IsHost);
	}

	[HarmonyPatch(typeof(RemoteWorkerDock), "MakeNewWorker")]
	internal static class RemoteWorkerDockMakeWorkerPatch
	{
		internal static bool Prefix() => RemoteWorkerDockSync.ShouldRunGameplay(
			MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(RemoteWorkerDock __instance)
		{
			RemoteWorkerDockSync.Broadcast(__instance, immediate: true);
			GameScheduler.Instance?.Schedule("RemoteWorkerDock host outcome", 2.1f,
				_ => RemoteWorkerDockSync.Broadcast(__instance, immediate: true));
		}
	}

	[HarmonyPatch(typeof(RemoteWorkerDock), "OnSpawn")]
	internal static class RemoteWorkerDockSpawnPatch
	{
		internal static void Postfix(RemoteWorkerDock __instance)
		{
			RemoteWorkerDockSync.Broadcast(__instance, immediate: true);
			if (MultiplayerSession.IsClient)
				GameScheduler.Instance?.Schedule("RemoteWorkerDock pending state", 0.1f,
					_ => RemoteWorkerDockSync.RetryPending());
		}
	}

	[HarmonyPatch(typeof(RemoteWorkerDock), "OnCleanUp")]
	internal static class RemoteWorkerDockCleanupPatch
	{
		internal static void Prefix(RemoteWorkerDock __instance, out int __state)
			=> __state = __instance.GetNetIdentity()?.NetId ?? 0;

		internal static void Postfix(int __state) => RemoteWorkerDockSync.Cleanup(__state);
	}

	[HarmonyPatch(typeof(RemoteWorkerSM), "OnSpawn")]
	internal static class RemoteWorkerStateMachineSpawnPatch
	{
		[HarmonyReversePatch]
		[System.Runtime.CompilerServices.MethodImpl(
			System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		internal static void RunOriginalOnSpawn(RemoteWorkerSM __instance)
			=> throw new System.NotImplementedException("Harmony reverse patch stub");

		internal static bool Prefix(RemoteWorkerSM __instance)
			=> RemoteWorkerDockSync.PrepareClientWorkerSpawn(__instance);

		internal static void Postfix()
		{
			if (MultiplayerSession.IsClient)
				GameScheduler.Instance?.Schedule("RemoteWorkerDock pending worker", 0.1f,
					_ => RemoteWorkerDockSync.RetryPending());
		}
	}

	[HarmonyPatch(typeof(RemoteWorkerSM), nameof(RemoteWorkerSM.TickResources), typeof(float))]
	internal static class RemoteWorkerTickResourcesPatch
	{
		internal static bool Prefix() => RemoteWorkerDockAuthority.HostOwnsGameplay();

		internal static void Postfix(RemoteWorkerSM __instance)
			=> RemoteWorkerDockSync.BroadcastFromWorker(__instance, immediate: false);
	}

	[HarmonyPatch(typeof(RemoteWorkerSM.States), nameof(RemoteWorkerSM.States.Explode))]
	internal static class RemoteWorkerExplodePatch
	{
		internal static bool Prefix() => RemoteWorkerDockAuthority.HostOwnsGameplay();
	}

	[HarmonyPatch]
	internal static class RemoteWorkerMaintenanceTickPatch
	{
		internal static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(RemoteWorkerDock.WorkerRecharger), "OnWorkTick");
			yield return AccessTools.Method(typeof(RemoteWorkerDock.WorkerGunkRemover), "OnWorkTick");
			yield return AccessTools.Method(typeof(RemoteWorkerDock.WorkerOilRefiller), "OnWorkTick");
		}

		internal static bool Prefix(object __instance, WorkerBase worker, ref bool __result)
		{
			if (RemoteWorkerDockAuthority.HostOwnsGameplay())
				return true;
			if (__instance is RemoteWorkerDock.WorkerRecharger)
				__result = worker?.GetComponent<RemoteWorkerCapacitor>()?.Charge >=
					RemoteWorkerDockStatePacket.MaxCharge;
			else if (__instance is RemoteWorkerDock.WorkerGunkRemover)
				__result = (worker?.GetComponent<Storage>()?.GetMassAvailable(SimHashes.LiquidGunk) ?? 0f) <= 0f;
			else
				__result = (worker?.GetComponent<Storage>()?.GetMassAvailable(GameTags.LubricatingOil) ?? 0f) >=
					RemoteWorkerDockStatePacket.MaxWorkerResourceMass;
			return false;
		}

		internal static void Postfix(WorkerBase worker)
			=> RemoteWorkerDockSync.BroadcastFromWorker(worker?.GetComponent<RemoteWorkerSM>(), immediate: false);
	}

	[HarmonyPatch]
	internal static class RemoteWorkerDockVoidAuthorityPatch
	{
		internal static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(RemoteWorkerDock), nameof(RemoteWorkerDock.CollectChores));
			yield return AccessTools.Method(typeof(RemoteWorkerDock), nameof(RemoteWorkerDock.SetNextChore));
			yield return AccessTools.Method(typeof(RemoteWorkerDock), nameof(RemoteWorkerDock.StopWorking));
		}

		internal static bool Prefix() => RemoteWorkerDockAuthority.HostOwnsGameplay();
	}

	[HarmonyPatch(typeof(RemoteWorkerDock), nameof(RemoteWorkerDock.StartWorking))]
	internal static class RemoteWorkerDockStartWorkingPatch
	{
		internal static bool Prefix(ref bool __result)
		{
			if (RemoteWorkerDockAuthority.HostOwnsGameplay())
				return true;
			__result = false;
			return false;
		}

		internal static void Postfix(RemoteWorkerDock __instance)
			=> RemoteWorkerDockSync.Broadcast(__instance, immediate: true);
	}

	[HarmonyPatch(typeof(RemoteWorkerDock), nameof(RemoteWorkerDock.OnRemoteWorkTick))]
	internal static class RemoteWorkerDockWorkTickPatch
	{
		internal static bool Prefix(ref bool __result)
		{
			if (RemoteWorkerDockAuthority.HostOwnsGameplay())
				return true;
			__result = true;
			return false;
		}
	}

	[HarmonyPatch(typeof(RemoteWorkerDock), nameof(RemoteWorkerDock.RemoteWorker), MethodType.Setter)]
	internal static class RemoteWorkerDockRelationshipPatch
	{
		internal static bool Prefix() => RemoteWorkerDockAuthority.AllowsHostOutcomeMutation();

		internal static void Postfix(RemoteWorkerDock __instance)
			=> GameScheduler.Instance?.Schedule("RemoteWorkerDock relationship", 0.1f,
				_ => RemoteWorkerDockSync.Broadcast(__instance, immediate: true));
	}

	[HarmonyPatch]
	internal static class RemoteWorkerRelationshipStatePatch
	{
		internal static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.PropertySetter(typeof(RemoteWorkerSM), nameof(RemoteWorkerSM.Docked));
			yield return AccessTools.PropertySetter(typeof(RemoteWorkerSM), nameof(RemoteWorkerSM.HomeDepot));
			yield return AccessTools.PropertySetter(typeof(RemoteWorkerSM), nameof(RemoteWorkerSM.ActivelyControlled));
			yield return AccessTools.PropertySetter(typeof(RemoteWorkerSM), nameof(RemoteWorkerSM.ActivelyWorking));
			yield return AccessTools.PropertySetter(typeof(RemoteWorkerSM), nameof(RemoteWorkerSM.Available));
		}

		internal static bool Prefix() => RemoteWorkerDockAuthority.AllowsHostOutcomeMutation();

		internal static void Postfix(RemoteWorkerSM __instance)
			=> RemoteWorkerDockSync.BroadcastFromWorker(__instance, immediate: true);
	}

	[HarmonyPatch(typeof(RemoteWorkTerminal), nameof(RemoteWorkTerminal.CurrentDock), MethodType.Setter)]
	internal static class RemoteWorkTerminalCurrentDockPatch
	{
		internal static bool Prefix() => RemoteWorkerDockAuthority.AllowsHostOutcomeMutation();
	}

	[HarmonyPatch]
	internal static class RemoteWorkTerminalVoidAuthorityPatch
	{
		internal static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(RemoteWorkTerminal), "OnStartWork");
			yield return AccessTools.Method(typeof(RemoteWorkTerminal), "OnStopWork");
		}

		internal static bool Prefix() => RemoteWorkerDockAuthority.HostOwnsGameplay();
	}

	[HarmonyPatch(typeof(RemoteWorkTerminal), "OnWorkTick")]
	internal static class RemoteWorkTerminalTickAuthorityPatch
	{
		internal static bool Prefix(ref bool __result)
		{
			if (RemoteWorkerDockAuthority.HostOwnsGameplay())
				return true;
			__result = true;
			return false;
		}
	}

	[HarmonyPatch]
	internal static class RemoteChoreAuthorityPatch
	{
		internal static IEnumerable<MethodBase> TargetMethods()
		{
			System.Type contexts = typeof(List<Chore.Precondition.Context>);
			yield return RequireDeclared(nameof(RemoteChore.CollectChores),
				typeof(ChoreConsumerState), contexts, contexts, contexts, typeof(bool));
			yield return RequireDeclared(nameof(RemoteChore.PrepareChore),
				typeof(Chore.Precondition.Context).MakeByRefType());
			yield return RequireDeclared("End", typeof(string));
		}

		private static MethodInfo RequireDeclared(string name, params System.Type[] parameters)
			=> AccessTools.DeclaredMethod(typeof(RemoteChore), name, parameters) ??
			   throw new System.MissingMethodException(typeof(RemoteChore).FullName, name);

		internal static bool Prefix() => RemoteWorkerDockAuthority.HostOwnsGameplay();
	}

	internal static class RemoteWorkerDockAuthority
	{
		internal static bool HostOwnsGameplay() => RemoteWorkerDockSync.ShouldRunGameplay(
			MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static bool AllowsHostOutcomeMutation()
			=> AllowsHostOutcomeMutation(MultiplayerSession.InSession, MultiplayerSession.IsHost,
				RemoteWorkerDockSync.IsApplyingHostState || RemoteWorkerDockSelectionSync.IsApplying);

		internal static bool AllowsHostOutcomeMutation(bool inSession, bool isHost, bool applyingHostState)
			=> RemoteWorkerDockSync.ShouldRunGameplay(inSession, isHost) || applyingHostState;
	}

	internal sealed class DeferredRemoteWorkerActivation
	{
		private bool _pending;
		private bool _activated;

		internal void Defer()
		{
			if (!_activated)
				_pending = true;
		}

		internal bool TryActivate(bool relationshipReady, System.Action activate)
		{
			if (!_pending || _activated || !relationshipReady || activate == null)
				return false;
			_pending = false;
			_activated = true;
			activate();
			return true;
		}
	}
}
