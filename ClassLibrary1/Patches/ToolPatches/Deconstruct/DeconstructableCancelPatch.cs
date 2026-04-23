using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Tools;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Deconstruct
{
	// Choke-point for "undo a pending deconstruction order":
	//   - CancelTool drag → Trigger(GameHashes.Cancel) → OnCancel(object) → CancelDeconstruction
	//   - User-menu "Cancel deconstruct" button → OnDeconstruct(object) w/ chore!=null → CancelDeconstruction
	//   - Single-click / scripted cancel → same method
	// The existing CancelToolPatch only covered drag.
	[HarmonyPatch(typeof(Deconstructable), nameof(Deconstructable.CancelDeconstruction))]
	public static class DeconstructableCancelPatch
	{
		public static void Postfix(Deconstructable __instance)
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
					Action = BuildingActionKind.CancelDeconstruct,
				});
				DebugConsole.Log($"[BuildingAction] send NetId={identity.NetId} kind=CancelDeconstruct src=CancelPatch");
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[DeconstructableCancelPatch] Exception: {ex}");
			}
		}
	}
}
