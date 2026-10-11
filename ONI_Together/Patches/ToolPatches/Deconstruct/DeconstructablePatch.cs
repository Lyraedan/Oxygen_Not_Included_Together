using System.Linq;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Tools.Deconstruct;
using Shared.Profiling;

namespace ONI_Together.Patches.ToolPatches.Deconstruct
{
	[HarmonyPatch(typeof(Deconstructable), "OnCompleteWork")]
	public static class DeconstructablePatch
	{
		public static void Prefix(Deconstructable __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost || !MultiplayerSession.InActiveSession)
				return;

			int cell = __instance.NaturalBuildingCell();
			int objectLayer = (int) ObjectLayer.Building;
			if (__instance.TryGetComponent<Building>(out var building))
			{
				objectLayer = (int) building.Def.ObjectLayer;
			}
            else if (__instance.TryGetComponent<OccupyArea>(out var area) && area.objectLayers.Any())
            {
                objectLayer = (int)area.objectLayers.FirstOrDefault();
            }
            else if (__instance.TryGetComponent<MoverLayerOccupier>(out var occupier) && occupier.objectLayers.Any())
            {
                objectLayer = (int)occupier.objectLayers.FirstOrDefault();
            }

            var packet = new DeconstructCompletePacket { Cell = cell, ObjectLayer = objectLayer };
            PacketSender.SendToAllClients(packet);

			DebugConsole.Log($"[DeconstructComplete] Host sent DeconstructCompletePacket for cell {cell}");
		}
	}
}
