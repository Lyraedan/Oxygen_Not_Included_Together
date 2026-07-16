using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Synchronization;
using ONI_Together.Scripts.Duplicants;
using System.Collections;
using Shared.Profiling;
using UnityEngine;

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
			networkIdentity = __result.AddOrGet<NetworkIdentity>();
			DebugConsole.Log("[NetworkIdentity] Injected into Duplicant");
		}

		__result.AddOrGet<OxySyncEntityPositionHandler>();
		//__result.AddOrGet<VitalStatsSyncer>();
		__result.AddOrGet<VitalStatsSyncComponent>();
	}

	public static void ToggleEffect(GameObject minion, string eventName, int contextHash, bool enable)
	{
		using var _ = Profiler.Scope();

		if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsClient)
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
			ContextHash = contextHash,
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

		go.AddOrGet<MinionMultiplayerInitializer>();
	}
}
