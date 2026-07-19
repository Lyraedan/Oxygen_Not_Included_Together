using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Build;
using System.Linq;
using Shared.Profiling;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

[HarmonyPatch(typeof(Constructable), nameof(Constructable.FinishConstruction))]
public static class ConstructablePatch
{
	public static void Prefix(
		Constructable __instance,
		WorkerBase workerForGameplayEvent,
		out BuildCompletePacket __state)
	{
		using var _ = Profiler.Scope();
		__state = null;

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

		int workerId = workerForGameplayEvent?.GetNetId() ?? 0;
		var packet = new BuildCompletePacket
		{
			Cell = cell,
			PrefabID = def.PrefabID,
			Orientation = orientation,
			MaterialTags = materialTags,
			Temperature = temp,
			FacadeID = facade,
			UtilityConnectionFlags = utilityConnectionFlags,
			ObjectLayer = def.ObjectLayer,
			WorkerNetId = workerId
		};
		__state = packet;

		PacketSender.SendToAllClients(packet);
		DebugConsole.Log($"[Host] Sent BuildCompletePacket for {def.PrefabID} at cell {cell}");
	}

	public static void Postfix(BuildCompletePacket __state)
	{
		if (__state == null || !MultiplayerSession.IsHostInSession)
			return;
		GameObject built = FindCompletedBuilding(__state);
		if (built == null)
			return;
		NetworkIdentity identity = built.AddOrGet<NetworkIdentity>();
		if (identity.NetId == 0)
			identity.RegisterIdentity();
		SpawnPrefabPacket lifecycle = SpawnPrefabPacket.FromIdentity(identity);
		if (lifecycle == null)
			return;
		lifecycle.BindExistingOnly = true;
		PacketSender.SendToAllClients(lifecycle, PacketSendMode.ReliableImmediate);
	}

	private static GameObject FindCompletedBuilding(BuildCompletePacket state)
	{
		int[] cells =
		{
			state.Cell, Grid.CellLeft(state.Cell), Grid.CellRight(state.Cell),
			Grid.CellAbove(state.Cell), Grid.CellBelow(state.Cell)
		};
		foreach (int cell in cells)
		{
			if (!Grid.IsValidCell(cell))
				continue;
			GameObject candidate = Grid.Objects[cell, (int)state.ObjectLayer];
			if (candidate?.GetComponent<BuildingComplete>()?.Def?.PrefabID == state.PrefabID)
				return candidate;
		}
		return null;
	}
}
