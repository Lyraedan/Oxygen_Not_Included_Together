using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World.SideScreen
{
    /// <summary>
    /// Patches for Artable (painting/sculpture) selection synchronization
    /// </summary>
    [HarmonyPatch(typeof(Artable), nameof(Artable.OnSpawn))]
    public static class Artable_OnSpawn_Patch
    {
        public static void Postfix(Artable __instance)
        {
	        using var _ = Profiler.Scope();

            var receptacleIdentity = __instance.gameObject.AddOrGet<NetworkIdentity>();
            receptacleIdentity.RegisterIdentity();
        }
    }

    [HarmonyPatch(typeof(Artable), nameof(Artable.SetUserChosenTargetState))]
	public static class Artable_SetUserChosenTargetState_Patch
	{
		public static void Postfix(Artable __instance, string stageID)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.GetComponent<NetworkIdentity>();
			if (!identity)
				return;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "ArtableState".GetHashCode(),
				ConfigType = BuildingConfigType.String,
				StringValue = stageID ?? ""
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[Artable_SetUserChosenTargetState_Patch] Synced art state={stageID} on {__instance.name}");
		}
	}

	[HarmonyPatch(typeof(Artable), nameof(Artable.SetDefault))]
	public static class Artable_SetDefault_Patch
	{
		public static void Postfix(Artable __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.GetComponent<NetworkIdentity>();
			if (!identity)
				return;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "ArtableDefault".GetHashCode(),
				ConfigType = BuildingConfigType.Boolean,
				Value = 0f
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[Artable_SetDefault_Patch] Synced art reset on {__instance.name}");
		}
	}
}
