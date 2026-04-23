using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Animation;
using ONI_MP.Networking.Synchronization;
using ONI_MP.Scripts.Duplicants;
using System.Collections;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World
{
[HarmonyPatch(typeof(BaseMinionConfig), nameof(BaseMinionConfig.BaseMinion))]
public static class DuplicantPatch
{
	public static void Postfix(GameObject __result)
	{
		using var _ = Profiler.Scope();

		var saveRoot = __result.GetComponent<SaveLoadRoot>();
		if (saveRoot != null)
			saveRoot.TryDeclareOptionalComponent<NetworkIdentity>();

		var networkIdentity = __result.GetComponent<NetworkIdentity>();
		if (networkIdentity == null)
		{
			__result.AddOrGet<NetworkIdentity>();
			DebugConsole.Log("[NetworkIdentity] Injected into Duplicant");
		}

		__result.AddOrGet<EntityPositionHandler>();
		__result.AddOrGet<VitalStatsSyncer>();
	}

	public static void ToggleEffect(GameObject minion, string eventName, string context, bool enable)
	{
		using var _ = Profiler.Scope();

		if (!MultiplayerSession.InSession || MultiplayerSession.IsClient)
			return;

		if (!minion.TryGetComponent(out NetworkIdentity net))
		{
			DebugConsole.LogWarning("[ToggleEffect] Minion is missing NetworkIdentity");
			return;
		}

		var packet = new ToggleMinionKanimEffectPacket
		{
			NetId = net.NetId,
			Enable = enable,
			Context = context,
			Event = eventName
		};

		PacketSender.SendToAllClients(packet);
	}
}

[HarmonyPatch(typeof(BaseMinionConfig), nameof(BaseMinionConfig.BaseOnSpawn))]
public static class DuplicantSpawnPatch
{
	public static void Postfix(GameObject go)
	{
		using var _ = Profiler.Scope();

		go.AddOrGet<MinionMultiplayerInitializer>(); // Doesn't work (yet)
		//CoroutineRunner.RunOne(DelayedOnSpawn(go));
	}

}

}