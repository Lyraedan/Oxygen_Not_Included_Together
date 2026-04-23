using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Tools;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Cancel
{
	// Choke-point for "cancel an unfinished building":
	//   - CancelTool drag → Trigger(GameHashes.Cancel) → Constructable.OnCancel(object)
	//   - Right-click "Cancel build" → same trigger
	//   - Any scripted / single-click cancel → same method
	// Method is private, so match by name string (Harmony accepts it).
	[HarmonyPatch(typeof(Constructable), "OnCancel")]
	public static class ConstructableCancelPatch
	{
		public static void Postfix(Constructable __instance)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (!MultiplayerSession.InSession) return;
				if (BuildingActionPacket.ProcessingIncoming) return;
				// Drag path already syncs via CancelPacket; skip here to avoid double-send.
				if (DragToolPacket.ProcessingIncoming) return;

				var identity = __instance.GetComponent<NetworkIdentity>();
				if (identity == null || identity.NetId == 0) return;

				PacketSender.SendToAllOtherPeers(new BuildingActionPacket
				{
					NetId = identity.NetId,
					Action = BuildingActionKind.CancelConstruct,
				});
				DebugConsole.Log($"[BuildingAction] send NetId={identity.NetId} kind=CancelConstruct src=ConstructCancelPatch");
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[ConstructableCancelPatch] Exception: {ex}");
			}
		}
	}
}
