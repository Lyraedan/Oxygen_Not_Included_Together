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
			if (!MultiplayerSession.InActiveSession) return;

			var go = __instance.gameObject;
			if (go == null) return;

			var identity = go.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(go),
				ConfigHash = "ClusterCometDetectorState".GetHashCode(),
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
			if (!MultiplayerSession.InActiveSession) return;

			var go = __instance.gameObject;
			if (go == null) return;

			var identity = go.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Get the clustercraft NetId if available
			int targetNetId = -1;
			if (target != null)
			{
				var targetIdentity = target.gameObject.GetComponent<NetworkIdentity>();
				if (targetIdentity != null)
				{
					targetNetId = targetIdentity.NetId;
				}
			}

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(go),
				ConfigHash = "ClusterCometDetectorTarget".GetHashCode(),
				Value = 0f,
				SliderIndex = targetNetId, // Store target NetId in SliderIndex
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
			if (!MultiplayerSession.InActiveSession) return;

			var go = __instance.gameObject;
			if (go == null) return;

			var identity = go.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Get the target craft NetId if available
			int targetNetId = -1;
			if (target != null)
			{
				var targetIdentity = target.gameObject.GetComponent<NetworkIdentity>();
				if (targetIdentity != null)
				{
					targetNetId = targetIdentity.NetId;
				}
			}

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(go),
				ConfigHash = "CometDetectorTarget".GetHashCode(),
				Value = 0f,
				SliderIndex = targetNetId, // Store target NetId in SliderIndex (-1 means null/meteors)
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[CometDetector_SetTargetCraft_Patch] Synced target NetId={targetNetId} on {go.name}");
		}
	}
}
