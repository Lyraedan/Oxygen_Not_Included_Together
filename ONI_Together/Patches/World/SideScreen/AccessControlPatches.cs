using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for AccessControl (door permissions) synchronization
	/// </summary>

	[HarmonyPatch(typeof(AccessControl), nameof(AccessControl.SetDefaultPermission))]
	public static class AccessControl_SetDefaultPermission_Patch
	{
		public static void Postfix(AccessControl __instance, Tag groupTag, AccessControl.Permission permission)
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
				ConfigHash = NetworkingHash.ForConfigKey("AccessControlDefault"),
				Value = (float)(int)permission,
				ConfigType = BuildingConfigType.String,
				StringValue = groupTag.Name
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	[HarmonyPatch(typeof(AccessControl), nameof(AccessControl.SetPermission), typeof(MinionAssignablesProxy), typeof(AccessControl.Permission))]
	public static class AccessControl_SetPermission_Patch
	{
		public static void Postfix(AccessControl __instance, MinionAssignablesProxy key, AccessControl.Permission permission)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			int minionNetId = 0;
			var targetGO = key.GetTargetGameObject();
			if (targetGO != null)
			{
				var minionIdentity = targetGO.GetComponent<NetworkIdentity>();
				if (minionIdentity != null)
				{
					minionNetId = minionIdentity.NetId;
				}
			}

			if (minionNetId == 0) return;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AccessControlMinion"),
				Value = (int)permission,
				ReferenceNetId = minionNetId,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[AccessControl_SetPermission_Patch] Synced: door={__instance.name}, minionNetId={minionNetId}");
		}
	}

	[HarmonyPatch(typeof(AccessControl), nameof(AccessControl.ClearPermission), typeof(MinionAssignablesProxy))]
	public static class AccessControl_ClearPermission_Patch
	{
		public static void Postfix(AccessControl __instance, MinionAssignablesProxy key)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			int minionNetId = 0;
			var targetGO = key.GetTargetGameObject();
			if (targetGO != null)
			{
				var minionIdentity = targetGO.GetComponent<NetworkIdentity>();
				if (minionIdentity != null)
				{
					minionNetId = minionIdentity.NetId;
				}
			}

			if (minionNetId == 0) return;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("AccessControlClear"),
				Value = 0f,
				ReferenceNetId = minionNetId,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[AccessControl_ClearPermission_Patch] Cleared for minionNetId={minionNetId} on {__instance.name}");
		}
	}

	/// <summary>
	/// Patch for robot tag-based permissions (FetchDrone, ScoutRover, MorbRover)
	/// </summary>
	[HarmonyPatch(typeof(AccessControl), nameof(AccessControl.SetPermission), typeof(Tag), typeof(AccessControl.Permission))]
	public static class AccessControl_SetPermission_Robot_Patch
	{
		public static void Postfix(AccessControl __instance, Tag gameTag, AccessControl.Permission permission)
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
				ConfigHash = NetworkingHash.ForConfigKey("AccessControlRobot"),
				Value = (float)(int)permission,
				ConfigType = BuildingConfigType.String,
				StringValue = gameTag.Name
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[AccessControl_SetPermission_Robot_Patch] Synced: door={__instance.name}, robotTag={gameTag.Name}, permission={permission}");
		}
	}

	/// <summary>
	/// Patch for clearing robot permissions (reset to default)
	/// </summary>
	[HarmonyPatch(typeof(AccessControl), nameof(AccessControl.ClearPermission), typeof(Tag), typeof(Tag))]
	public static class AccessControl_ClearPermission_Robot_Patch
	{
		public static void Postfix(AccessControl __instance, Tag tag, Tag default_key)
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
				ConfigHash = NetworkingHash.ForConfigKey("AccessControlRobotClear"),
				Value = 0f,
				ConfigType = BuildingConfigType.String,
				StringValue = tag.Name
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[AccessControl_ClearPermission_Robot_Patch] Cleared robot permission for {tag.Name} on {__instance.name}");
		}
	}
}
