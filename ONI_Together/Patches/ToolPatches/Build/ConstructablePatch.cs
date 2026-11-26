using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Build;
using System.Linq;
using Shared.Profiling;
using ONI_Together.Misc;

[HarmonyPatch(typeof(Constructable), nameof(Constructable.FinishConstruction))]
public static class ConstructablePatch
{
	public static void Prefix(Constructable __instance, WorkerBase workerForGameplayEvent)
	{
		using var _ = Profiler.Scope();

		try
		{
			if (!MultiplayerSession.IsHost || !MultiplayerSession.InActiveSession)
				return;

			if (__instance == null || __instance.IsNullOrDestroyed())
				return;

			var building = __instance.GetComponent<Building>();
			if (building == null || building.Def == null)
				return;

			int cell = Grid.PosToCell(__instance.transform.position);
			if (!Grid.IsValidCell(cell))
				return;

			var def = building.Def;

			var materialTags = __instance.SelectedElementsTags?
				.Where(tag => tag.IsValid)
				.Select(tag => tag.ToString())
				.ToList() ?? new System.Collections.Generic.List<string>();

			var primaryElement = __instance.GetComponent<PrimaryElement>();
			float temp = primaryElement != null && primaryElement.Temperature > 1f
				? primaryElement.Temperature
				: def.Temperature;

			var rotatable = __instance.GetComponent<Rotatable>();
			var orientation = rotatable != null ? rotatable.GetOrientation() : Orientation.Neutral;

			var facade = __instance.GetComponent<BuildingFacade>()?.CurrentFacade;
			if (string.IsNullOrEmpty(facade))
				facade = "DEFAULT_FACADE";

			// Capture connection directions for wires/pipes
			UtilityConnections utilityConnectionFlags = (UtilityConnections)0;
			var tileVis = __instance.GetComponent<KAnimGraphTileVisualizer>();
			if (tileVis != null)
			{
				utilityConnectionFlags = tileVis.Connections;
			}

			int workerId = workerForGameplayEvent != null ? workerForGameplayEvent.GetNetId() : 0;
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

			PacketSender.SendToAllClients(packet);
			DebugConsole.Log($"[Host] Sent BuildCompletePacket for {def.PrefabID} at cell {cell}");
		}
		catch (System.Exception ex)
		{
			DebugConsole.LogError($"[ConstructablePatch.Prefix] Failed to sync finished building: {ex}");
		}
	}
}

