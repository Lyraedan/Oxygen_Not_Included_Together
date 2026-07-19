using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	internal static class CometDetectorTargetIdentity
	{
		internal static int GetClustercraftNetId(Clustercraft craft)
		{
			RocketModuleCluster module = craft?.ModuleInterface?.GetPrimaryPilotModule(out bool hasPilot);
			if (module == null) return 0;
			NetworkIdentity identity = module.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			return identity.NetId;
		}

		internal static int GetLaunchManagerNetId(LaunchConditionManager manager)
		{
			if (manager == null) return 0;
			NetworkIdentity identity = manager.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			return identity.NetId;
		}
	}

	/// <summary>
	/// Patches for Comet Detector (Space Scanner) synchronization.
	/// Handles both DLC (ClusterCometDetector) and base game (CometDetector).
	/// </summary>

	// ==================== DLC (Spaced Out) Patches ====================

	/// <summary>
	/// Patch for ClusterCometDetector state changes (meteors, ballistic, rocket tracking)
	/// </summary>
	[HarmonyPatch(typeof(ClusterCometDetector.Instance), nameof(ClusterCometDetector.Instance.SetDetectorState))]
	public static class ClusterCometDetector_SetDetectorState_Patch
	{
		public static void Postfix(ClusterCometDetector.Instance __instance, ClusterCometDetector.Instance.ClusterCometDetectorState newState)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var go = __instance.gameObject;
			if (go == null) return;

			var identity = go.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(go),
				ConfigHash = NetworkingHash.ForConfigKey("ClusterCometDetectorState"),
				Value = (float)(int)newState,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[ClusterCometDetector_SetDetectorState_Patch] Synced state={newState} on {go.name}");
		}
	}

	/// <summary>
	/// Patch for ClusterCometDetector clustercraft target (which rocket to track)
	/// </summary>
	[HarmonyPatch(typeof(ClusterCometDetector.Instance), nameof(ClusterCometDetector.Instance.SetClustercraftTarget))]
	public static class ClusterCometDetector_SetClustercraftTarget_Patch
	{
		public static void Postfix(ClusterCometDetector.Instance __instance, Clustercraft target)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var go = __instance.gameObject;
			if (go == null) return;

			var identity = go.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Get the clustercraft NetId if available
			int targetNetId = CometDetectorTargetIdentity.GetClustercraftNetId(target);
			if (target != null && targetNetId == 0) return;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(go),
				ConfigHash = NetworkingHash.ForConfigKey("ClusterCometDetectorTarget"),
				Value = 0f,
				ReferenceNetId = targetNetId,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[ClusterCometDetector_SetClustercraftTarget_Patch] Synced target={target?.Name ?? "null"} (NetId={targetNetId}) on {go.name}");
		}
	}

	// ==================== Base Game Patches ====================

	/// <summary>
	/// Patch for CometDetector target craft (base game - non-DLC)
	/// </summary>
	[HarmonyPatch(typeof(CometDetector.Instance), nameof(CometDetector.Instance.SetTargetCraft))]
	public static class CometDetector_SetTargetCraft_Patch
	{
		public static void Postfix(CometDetector.Instance __instance, LaunchConditionManager target)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var go = __instance.gameObject;
			if (go == null) return;

			var identity = go.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Get the target craft NetId if available
			int targetNetId = CometDetectorTargetIdentity.GetLaunchManagerNetId(target);
			if (target != null && targetNetId == 0) return;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(go),
				ConfigHash = NetworkingHash.ForConfigKey("CometDetectorTarget"),
				Value = 0f,
				ReferenceNetId = targetNetId,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[CometDetector_SetTargetCraft_Patch] Synced target NetId={targetNetId} on {go.name}");
		}
	}
}
