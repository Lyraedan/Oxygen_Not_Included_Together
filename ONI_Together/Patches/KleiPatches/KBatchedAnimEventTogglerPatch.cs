using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using Shared.Profiling;

[HarmonyPatch]
public static class KBatchedAnimEventTogglerPatch
{
	[HarmonyPatch(typeof(KBatchedAnimEventToggler), "Enable")]
	[HarmonyPrefix]
	private static void Prefix_Enable(KBatchedAnimEventToggler __instance, object data)
	{
		using var _ = Profiler.Scope();

		TrySendEffectPacket(__instance, true);
	}

	[HarmonyPatch(typeof(KBatchedAnimEventToggler), "Disable")]
	[HarmonyPrefix]
	private static void Prefix_Disable(KBatchedAnimEventToggler __instance, object data)
	{
		using var _ = Profiler.Scope();

		TrySendEffectPacket(__instance, false);
	}

	private static void TrySendEffectPacket(KBatchedAnimEventToggler toggler, bool enable)
	{
		using var _ = Profiler.Scope();

		if (!toggler.isActiveAndEnabled || toggler.eventSource == null)
			return;

		if (!MultiplayerSession.IsHost)
			return;

		var identity = toggler.GetComponentInParent<NetworkIdentity>();
		if (identity == null)
			return;

		var handler = toggler.GetComponentInParent<AnimEventHandler>();
		if (handler == null)
			return;

		try
		{
			var context = handler.GetContext();
			if (!context.IsValid)
				return;

			string contextStr = context.ToString();
			if (string.IsNullOrEmpty(contextStr))
				return;

			var eventName = enable ? toggler.enableEvent : toggler.disableEvent;

			if (eventName == "LaserOn" || eventName == "LaserOff")
				return;

			DuplicantPatch.ToggleEffect(identity.gameObject, eventName, contextStr, enable);
		}
		catch (System.Exception)
		{
			// Silently ignore - animation context may not be ready yet
		}
	}
}
