using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Tools.Build;
using System.Linq;
using Shared.Profiling;

[HarmonyPatch(typeof(Constructable), "FinishConstruction")]
public static class ConstructablePatch
{
	public static void Prefix(Constructable __instance)
	{
		using var _ = Profiler.Scope();

		if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession)
			return;

		var building = __instance.GetComponent<Building>();
		if (building == null || building.Def == null)
			return;

		int cell = Grid.PosToCell(__instance.transform.position);
		var def = building.Def;

		var materialTags = __instance.SelectedElementsTags?.Select(tag => tag.ToString()).ToList()
											 ?? new System.Collections.Generic.List<string>();

		float temp = __instance.GetComponent<PrimaryElement>()?.Temperature ?? def.Temperature;

		var rotatable = __instance.GetComponent<Rotatable>();
		var orientation = rotatable != null ? rotatable.GetOrientation() : Orientation.Neutral;

		var facade = __instance.GetComponent<BuildingFacade>()?.CurrentFacade ?? "DEFAULT_FACADE";

		// Capture connection directions for wires/pipes
		bool connectsUp = false, connectsDown = false, connectsLeft = false, connectsRight = false;
		var tileVis = __instance.GetComponent<KAnimGraphTileVisualizer>();
		if (tileVis != null)
		{
			var connections = tileVis.Connections;
			connectsUp = (connections & UtilityConnections.Up) != 0;
			connectsDown = (connections & UtilityConnections.Down) != 0;
			connectsLeft = (connections & UtilityConnections.Left) != 0;
			connectsRight = (connections & UtilityConnections.Right) != 0;
			DebugConsole.Log($"[ConstructablePatch] Captured connections for {def.PrefabID}: Up={connectsUp}, Down={connectsDown}, Left={connectsLeft}, Right={connectsRight}");
		}

		var packet = new BuildCompletePacket
		{
			Cell = cell,
			PrefabID = def.PrefabID,
			Orientation = orientation,
			MaterialTags = materialTags,
			Temperature = temp,
			FacadeID = facade,
			ConnectsUp = connectsUp,
			ConnectsDown = connectsDown,
			ConnectsLeft = connectsLeft,
			ConnectsRight = connectsRight,
			ObjectLayer = def.ObjectLayer
		};

		PacketSender.SendToAllClients(packet);
		DebugConsole.Log($"[Host] Sent BuildCompletePacket for {def.PrefabID} at cell {cell}");
	}
}

