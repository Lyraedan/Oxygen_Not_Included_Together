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
    /// Patches for SingleEntityReceptacle synchronization (planters, incubators selecting items)
    /// </summary>
    [HarmonyPatch(typeof(SingleEntityReceptacle), nameof(SingleEntityReceptacle.OnSpawn))]
    public static class SingleEntityReceptacle_OnSpawn_Patch
    {
        public static void Postfix(SingleEntityReceptacle __instance)
        {
	        using var _ = Profiler.Scope();

            var receptacleIdentity = __instance.gameObject.AddOrGet<NetworkIdentity>();
            receptacleIdentity.RegisterIdentity();
        }
    }

    [HarmonyPatch(typeof(SingleEntityReceptacle), nameof(SingleEntityReceptacle.CreateOrder))]
	public static class SingleEntityReceptacle_CreateOrder_Patch
	{
		public static bool Prefix()
		{
			return !MultiplayerSession.InSession || MultiplayerSession.IsHost
			       || BuildingConfigPacket.IsApplyingPacket;
		}

		public static void Postfix(SingleEntityReceptacle __instance, Tag entityTag, Tag additionalFilterTag)
		{
			using var _ = Profiler.Scope();

            if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.IsNullOrDestroyed()) return;

            var identity = __instance.gameObject.GetComponent<NetworkIdentity>();
			if (!identity)
				return;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("ReceptacleOrder"),
				Value = 0,
				ConfigType = BuildingConfigType.String,
				StringValue = entityTag.IsValid ? entityTag.Name : "",
				SecondaryStringValue = additionalFilterTag.IsValid ? additionalFilterTag.Name : ""
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
        }
    }

	[HarmonyPatch(typeof(SingleEntityReceptacle), nameof(SingleEntityReceptacle.CancelActiveRequest))]
	public static class SingleEntityReceptacle_CancelActiveRequest_Patch
	{
		public static bool Prefix()
		{
			return !MultiplayerSession.InSession || MultiplayerSession.IsHost
			       || BuildingConfigPacket.IsApplyingPacket;
		}

		public static void Postfix(SingleEntityReceptacle __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.IsNullOrDestroyed()) return;

            var identity = __instance.gameObject.GetComponent<NetworkIdentity>();
			if (!identity)
				return;

            var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("ReceptacleCancelRequest"),
				Value = 1f,
				ConfigType = BuildingConfigType.Boolean
			};

            if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
        }
    }
}
