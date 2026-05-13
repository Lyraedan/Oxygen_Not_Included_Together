using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Build;
using System.Linq;
using Shared.Profiling;
using ONI_MP.Misc;

[HarmonyPatch(typeof(Constructable), nameof(Constructable.FinishConstruction))]
public static class ConstructablePatch
{
	public static void Prefix(Constructable __instance, WorkerBase workerForGameplayEvent)
	{
		using var _ = Profiler.Scope();

		if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
			return;

		var building = __instance.GetComponent<Building>();
		if (building == null || building.Def == null)
			return;

		int cell = Grid.PosToCell(__instance.transform.position);
		var def = building.Def;

		var materialTags = __instance.SelectedElementsTags?.Select(tag => tag.ToString()).ToList() ?? new System.Collections.Generic.List<string>();

		float temp = __instance.GetComponent<PrimaryElement>()?.Temperature ?? def.Temperature;

		var rotatable = __instance.GetComponent<Rotatable>();
		var orientation = rotatable != null ? rotatable.GetOrientation() : Orientation.Neutral;

		var facade = __instance.GetComponent<BuildingFacade>()?.CurrentFacade ?? "DEFAULT_FACADE";

        // Handle utility connections
        UtilityConnections utilityConnectionFlags = (UtilityConnections)0;
        // Capture connection directions for wires/pipes
        var tileVis = __instance.GetComponent<KAnimGraphTileVisualizer>();
		if (tileVis != null)
		{
			utilityConnectionFlags = tileVis.Connections;
		}

        BuildingUtils.GetLayerInfo(building, out var objectLayer, out var isReplacement);

		/*
        IHaveUtilityNetworkMgr mgr = def.BuildingComplete.GetComponent<IHaveUtilityNetworkMgr>();
        if (mgr != null)
		{
			var networkManager = mgr.GetNetworkManager();
			if(networkManager != null)
			{
                utilityConnectionFlags = networkManager.GetConnections(cell, false);
            }
		}*/

		int workerId = workerForGameplayEvent.GetNetId();
		var packet = new BuildCompletePacket
		{
			Cell = cell,
			PrefabID = def.PrefabID,
			Orientation = orientation,
			MaterialTags = materialTags,
			Temperature = temp,
			FacadeID = facade,
			UtilityConnectionFlags = utilityConnectionFlags,
			ObjectLayer = objectLayer,
			IsReplacement = isReplacement,
			WorkerNetId = workerId
		};

		PacketSender.SendToAllClients(packet);
		DebugConsole.Log($"[Host] Sent BuildCompletePacket for {def.PrefabID} at cell {cell}");
	}
}

