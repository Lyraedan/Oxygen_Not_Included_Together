using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Tools;
using Shared.Profiling;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.ToolPatches.Deconstruct
{
	// All "mark for deconstruction" paths funnel through QueueDeconstruction:
	//   - DeconstructTool drag → gameObject.Trigger(GameHashes.Deconstruct) → OnDeconstruct → QueueDeconstruction
	//   - Right-click / user-menu "Deconstruct" button → OnDeconstruct(object) → QueueDeconstruction
	//   - Single-click-context and any scripted trigger → same hash → same path
	// Hooking this one method catches drag + single-click + menu in one place,
	// which the existing DeconstructToolPatch (OnDragTool only) missed.
	[HarmonyPatch(typeof(Deconstructable), nameof(Deconstructable.QueueDeconstruction), new System.Type[] { typeof(bool) })]
	public static class DeconstructableQueuePatch
	{
		public static void Postfix(Deconstructable __instance, bool userTriggered)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (!MultiplayerSession.InSession) return;
				if (BuildingActionPacket.ProcessingIncoming) return;
				// Drag path already has its own sync via DeconstructPacket; don't double-send.
				// This patch exists specifically for non-drag entry points.
				if (DragToolPacket.ProcessingIncoming) return;
				if (!userTriggered) return; // only sync user intent; load/rehydrate calls userTriggered=false

				var identity = __instance.GetComponent<NetworkIdentity>();
				if (identity == null || identity.NetId == 0) return;

				PacketSender.SendToAllOtherPeers(new BuildingActionPacket
				{
					NetId = identity.NetId,
					Action = BuildingActionKind.QueueDeconstruct,
				});
				DebugConsole.Log($"[BuildingAction] send NetId={identity.NetId} kind=QueueDeconstruct src=QueuePatch");
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[DeconstructableQueuePatch] Exception: {ex}");
			}
		}
	}
}
