using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Core;
using Shared.Profiling;
using System;
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

			if (!MultiplayerSession.InActiveSession)
				return true;
			if (!__instance.TryGetComponent<NetworkIdentity>(out var identity))
				return true;
			return MultiplayerSession.IsHost;
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

			if (!MultiplayerSession.InActiveSession)
				return true;
			if (__instance.TryGetComponent<NetworkIdentity>(out var identity))
				return MultiplayerSession.IsHost;
			return true;
		}
	}

	/// <summary>
	/// Prevents the generic PlayAnim stream from duplicating locomotion events.
	/// The semantic navigation packet is the sole owner of movement animation.
	/// </summary>
	internal static class NavigationAnimationScope
	{
		private static GameObject _owner;
		private static int _depth;

		internal static void Enter(GameObject owner)
		{
			if (_depth == 0)
				_owner = owner;
			_depth++;
		}

		internal static void Exit()
		{
			if (_depth > 0)
				_depth--;
			if (_depth == 0)
				_owner = null;
		}

		internal static bool Suppresses(GameObject owner)
		{
			return _depth > 0 && _owner == owner;
		}
	}

	internal static class NavigationSequence
	{
		private static readonly Dictionary<int, uint> Sequences = new();

		internal static uint Next(int netId)
		{
			Sequences.TryGetValue(netId, out uint sequence);
			sequence++;
			if (sequence == 0) sequence++;
			Sequences[netId] = sequence;
			return sequence;
		}

		internal static void Forget(int netId) => Sequences.Remove(netId);
		internal static void Clear() => Sequences.Clear();
	}

	[HarmonyPatch(typeof(Navigator), nameof(Navigator.BeginTransition), new[] { typeof(NavGrid.Transition) })]
	public static class Navigator_BeginTransition_Patch
	{
		private struct PatchState
		{
			internal bool ScopeActive;
			internal bool ShouldSend;
			internal int NetId;
			internal Vector3 SourcePosition;
		}

		static void Prefix(Navigator __instance, out PatchState __state)
		{
			using var _ = Profiler.Scope();
			__state = default;

			if (!IsHostNetworkDuplicant(__instance, out var identity))
				return;

			NavigationAnimationScope.Enter(__instance.gameObject);
			__state.ScopeActive = true;
			__state.ShouldSend = MultiplayerSession.ConnectedPlayers.Count > 0;
			__state.NetId = identity.NetId;
			__state.SourcePosition = __instance.transform.GetPosition();
		}

		static void Postfix(Navigator __instance, NavGrid.Transition transition, ref PatchState __state)
		{
			using var _ = Profiler.Scope();
			try
			{
				if (!__state.ShouldSend)
					return;

				var active = __instance.transitionDriver?.GetTransition;
				float speed = active != null && active.speed > 0f
					? active.speed
					: __instance.defaultSpeed;
				PacketSender.SendToAllClients(new NavigatorTransitionPacket
				{
					NetId = __state.NetId,
					Sequence = NavigationSequence.Next(__state.NetId),
					ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
					SourcePosition = __state.SourcePosition,
					TransitionId = transition.id,
					Speed = speed,
				}, PacketSendMode.ReliableImmediate);
			}
			finally
			{
				ReleaseScope(ref __state);
			}
		}

		static Exception Finalizer(Exception __exception, ref PatchState __state)
		{
			ReleaseScope(ref __state);
			return __exception;
		}

		private static void ReleaseScope(ref PatchState state)
		{
			if (!state.ScopeActive)
				return;
			NavigationAnimationScope.Exit();
			state.ScopeActive = false;
		}

		private static bool IsHostNetworkDuplicant(Navigator navigator, out NetworkIdentity identity)
		{
			identity = null;
			if (!MultiplayerSession.InActiveSession || !MultiplayerSession.IsHost)
				return false;
			if (!navigator.TryGetComponent(out identity) || identity.NetId == 0)
				return false;
			return navigator.TryGetComponent<KPrefabID>(out var prefabId)
				&& prefabId.HasTag(GameTags.BaseMinion);
		}
	}

	[HarmonyPatch(typeof(Navigator), nameof(Navigator.Stop), new[] { typeof(bool), typeof(bool) })]
	public static class Navigator_Stop_Patch
	{
		private struct PatchState
		{
			internal bool ScopeActive;
			internal bool ShouldSend;
			internal int NetId;
		}

		static void Prefix(Navigator __instance, out PatchState __state)
		{
			using var _ = Profiler.Scope();
			__state = default;

			if (!MultiplayerSession.InActiveSession || !MultiplayerSession.IsHost)
				return;
			if (!__instance.TryGetComponent<NetworkIdentity>(out var identity) || identity.NetId == 0)
				return;
			if (!__instance.TryGetComponent<KPrefabID>(out var prefabId) || !prefabId.HasTag(GameTags.BaseMinion))
				return;

			NavigationAnimationScope.Enter(__instance.gameObject);
			__state.ScopeActive = true;
			__state.ShouldSend = MultiplayerSession.ConnectedPlayers.Count > 0;
			__state.NetId = identity.NetId;
		}

		static void Postfix(Navigator __instance, bool play_idle, ref PatchState __state)
		{
			using var _ = Profiler.Scope();
			try
			{
				if (!__state.ShouldSend)
					return;

				PacketSender.SendToAllClients(new NavigatorTransitionPacket
				{
					NetId = __state.NetId,
					Sequence = NavigationSequence.Next(__state.NetId),
					ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
					IsStop = true,
					SourcePosition = __instance.transform.GetPosition(),
					StopNavType = __instance.CurrentNavType,
					PlayIdle = play_idle,
				}, PacketSendMode.ReliableImmediate);
			}
			finally
			{
				ReleaseScope(ref __state);
			}
		}

		static Exception Finalizer(Exception __exception, ref PatchState __state)
		{
			ReleaseScope(ref __state);
			return __exception;
		}

		private static void ReleaseScope(ref PatchState state)
		{
			if (!state.ScopeActive)
				return;
			NavigationAnimationScope.Exit();
			state.ScopeActive = false;
		}
	}
}
