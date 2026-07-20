using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using Shared.Profiling;

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

			if (!__instance.TryGetComponent<NetworkIdentity>(out var ni))
				return true;

			if (MultiplayerSession.IsHost)
				return true;

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

			if (!MultiplayerSession.InActiveSession)
				return true;

			if (__instance.TryGetComponent<NetworkIdentity>(out var netIdentity))
				return MultiplayerSession.IsHost;

			return true;
		}
	}

	/*

	[HarmonyPatch(typeof(Navigator), nameof(Navigator.BeginTransition))]
	public static class Navigator_BeginTransition_Patch
	{
		static void Postfix(Navigator __instance, NavGrid.Transition transition)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
				return;

			if (MultiplayerSession.ConnectedPlayers.Count == 0)
				return;

			if (!__instance.TryGetComponent<NetworkIdentity>(out var identity))
				return;

			if (!__instance.TryGetComponent<KPrefabID>(out var prefabId) || !prefabId.HasTag(GameTags.BaseMinion))
				return;

			var activeTransition = __instance.transitionDriver.GetTransition;
			if (activeTransition == null)
				return;

			var packet = new NavigatorTransitionPacket
			{
				NetId = identity.NetId,
				IsStop = false,
				SourcePosition = __instance.transform.GetPosition(),
				TransitionX = (sbyte)transition.x,
				TransitionY = (sbyte)transition.y,
				Speed = activeTransition.speed,
				AnimSpeed = activeTransition.animSpeed,
				Anim = transition.anim,
				PreAnim = transition.preAnim,
				IsLooping = transition.isLooping,
				StartNavType = (byte)transition.start,
				EndNavType = (byte)transition.end
			};

			PacketSender.SendToAllClients(packet, sendType: PacketSendMode.Unreliable);
		}
	}

	[HarmonyPatch(typeof(Navigator), nameof(Navigator.Stop))]
	public static class Navigator_Stop_Patch
	{
		static void Postfix(Navigator __instance, bool arrived_at_destination, bool play_idle)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
				return;

			if (MultiplayerSession.ConnectedPlayers.Count == 0)
				return;

			if (!__instance.TryGetComponent<NetworkIdentity>(out var identity))
				return;

			if (!__instance.TryGetComponent<KPrefabID>(out var prefabId) || !prefabId.HasTag(GameTags.BaseMinion))
				return;

			if (!play_idle)
				return;

			var packet = new NavigatorTransitionPacket
			{
				NetId = identity.NetId,
				IsStop = true,
				EndNavType = (byte)__instance.CurrentNavType
			};

			PacketSender.SendToAllClients(packet, sendType: PacketSendMode.Reliable);
		}
	}

	*/
}
