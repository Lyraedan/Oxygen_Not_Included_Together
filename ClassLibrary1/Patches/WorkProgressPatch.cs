using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.World;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches
{
[HarmonyPatch(typeof(Workable), nameof(Workable.WorkTick))]
public static class WorkProgressPatch
{
	private static Dictionary<int, float> nextSendTime = new Dictionary<int, float>();
	private const float SEND_INTERVAL = 0.5f;

	public static void Postfix(Workable __instance)
	{
		using var _ = Profiler.Scope();

		if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
			return;

		if (__instance.IsNullOrDestroyed())
			return;

		if (ShouldSkip(__instance))
			return;

		float workTime = __instance.GetWorkTime();
		if (workTime <= 0f || float.IsInfinity(workTime) || float.IsNaN(workTime))
			return;

		float percentComplete = __instance.GetPercentComplete();
		if (float.IsNaN(percentComplete) || float.IsInfinity(percentComplete))
			return;

		int workableNetId = __instance.GetNetId();
		if (workableNetId == 0)
			return;

		string workableType = __instance.GetType().AssemblyQualifiedName;
		if (string.IsNullOrEmpty(workableType))
			return;

		int trackingKey = GetTrackingKey(workableNetId, workableType);
		float now = Time.time;

		if (nextSendTime.TryGetValue(trackingKey, out float next) && now < next)
			return;

		nextSendTime[trackingKey] = now + SEND_INTERVAL;
		PacketSender.SendToAllClients(new WorkableProgressPacket(__instance), PacketSendMode.Unreliable);
	}

	public static void ClearTracking()
	{
		using var _ = Profiler.Scope();

		nextSendTime.Clear();
	}

	private static bool ShouldSkip(Workable workable)
	{
		return workable is DefragmentationZone
			|| workable.GetType().Name == "RancherWorkable"
			|| workable is LiquidPumpingStation;
	}

	private static int GetTrackingKey(int workableNetId, string workableType)
	{
		return unchecked((workableNetId * 397) ^ workableType.GetHashCode());
	}
}

}