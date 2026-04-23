using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Synchronization;
using ONI_MP.Scripts.Duplicants;
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

	static IEnumerator DelayedOnSpawn(GameObject go)
	{
		using var _ = Profiler.Scope();

		yield return new WaitForSecondsRealtime(1f);

        if (!go.HasTag(GameTags.BaseMinion)) yield return null;

        var identity = go.GetComponent<NetworkIdentity>();
        if (identity == null) yield return null;

        // If we are a client, disable the brain/chores so the dupe is just a puppet
        if (MultiplayerSession.IsClient)
        {
            // Disable AI/decision making components
            if (go.TryGetComponent<ChoreDriver>(out var driver)) driver.enabled = false;
            if (go.TryGetComponent<ChoreConsumer>(out var consumer)) consumer.enabled = false;
            if (go.TryGetComponent<MinionBrain>(out var brain)) brain.enabled = false;
            if (go.TryGetComponent<Navigator>(out var nav)) nav.enabled = false;

            // Disable sensors that might trigger behaviors
            if (go.TryGetComponent<Sensors>(out var sensors)) sensors.enabled = false;

            // Disable state machine controllers that could override animations
            var stateMachineControllers = go.GetComponents<StateMachineController>();
            foreach (var smc in stateMachineControllers)
            {
                if (smc != null) smc.enabled = false;
            }

            // go.AddOrGet<DuplicantClientController>();

            DebugConsole.Log($"[DuplicantSpawn] Client setup complete for {go.name} (NetId: {identity.NetId})");
        }
        else if (MultiplayerSession.IsHost || !MultiplayerSession.InSession)
        {
            // Add state sender for host to broadcast duplicant state to clients
            go.AddOrGet<DuplicantStateSender>();

            DebugConsole.Log($"[DuplicantSpawn] Host setup complete for {go.name} (NetId: {identity.NetId})");
        }
    }
}
