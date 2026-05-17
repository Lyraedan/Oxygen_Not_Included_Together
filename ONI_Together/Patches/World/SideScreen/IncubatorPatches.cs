using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for EggIncubator and IncubatorSideScreen synchronization
	/// </summary>

	[HarmonyPatch(typeof(EggIncubator), nameof(EggIncubator.OnCopySettings))]
	public static class EggIncubator_OnCopySettings_Patch
	{
		public static void Postfix(EggIncubator __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "IncubatorAutoReplace".GetHashCode(),
				Value = __instance.autoReplaceEntity ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	[HarmonyPatch(typeof(IncubatorSideScreen), nameof(IncubatorSideScreen.SetTarget))]
	public static class IncubatorSideScreen_SetTarget_Patch
	{
		public static void Postfix(IncubatorSideScreen __instance, GameObject target)
		{
			using var _ = Profiler.Scope();

			if (target == null) return;

			var incubator = target.GetComponent<EggIncubator>();
			if (incubator == null) return;

			var originalOnClick = __instance.continuousToggle.onClick;

			__instance.continuousToggle.onClick = delegate
			{
				originalOnClick?.Invoke();

				if (BuildingConfigPacket.IsApplyingPacket) return;
				if (!MultiplayerSession.InSession) return;

				var identity = incubator.gameObject.AddOrGet<NetworkIdentity>();
				identity.RegisterIdentity();

				var packet = new BuildingConfigPacket
				{
					NetId = identity.NetId,
					Cell = Grid.PosToCell(incubator.gameObject),
					ConfigHash = "IncubatorAutoReplace".GetHashCode(),
					Value = incubator.autoReplaceEntity ? 1f : 0f,
					ConfigType = BuildingConfigType.Boolean
				};

				if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
				else PacketSender.SendToHost(packet);

				DebugConsole.Log($"[IncubatorSideScreen] Synced autoReplaceEntity={incubator.autoReplaceEntity}");
			};
		}
	}
}
