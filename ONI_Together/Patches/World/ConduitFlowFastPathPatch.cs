using System;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	// Event-driven fast path for "empty -> non-empty" pipe transitions
	// (valve opens, freshly built pipe receives its first packet, storage
	// discharges into an empty network). Steady-state flow stays on the
	// 1.5 s ConduitFlowSyncer sweep — this patch only handles the one
	// frame the user actually watches, where a 1.5 s lag is most visible.
	//
	// Per-cell rate limit, end-of-frame coalescing and shadow update all
	// live inside ConduitFlowSyncer.EnqueueImmediate so this patch stays
	// trivially small and defensive.
	internal static class ConduitFlowFastPathPatch
	{
		[HarmonyPatch(typeof(ConduitFlow), nameof(ConduitFlow.AddElement))]
		public static class ConduitFlow_AddElement_FastPath
		{
			// Capture pre-add mass so the Postfix can detect a 0 -> >0
			// transition. AddElement *adds* to the cell, so reading the
			// after-state alone only tells us "now > 0", not whether the
			// cell was empty before this call.
			public static void Prefix(ConduitFlow __instance, int cell, out float __state)
			{
				__state = -1f;
				try
				{
					using var _ = Profiler.Scope();
					if (!MultiplayerSession.IsHost) return;
					if (Game.Instance == null) return;
					__state = __instance.GetContents(cell).mass;
				}
				catch
				{
					// invariant #10: Prefix never throws. __state stays -1
					// so the Postfix takes the "wasn't empty" branch and
					// bails — same outcome as the periodic sweep handling it.
				}
			}

			public static void Postfix(ConduitFlow __instance, int cell, float __state)
			{
				try
				{
					using var _ = Profiler.Scope();
					if (!MultiplayerSession.IsHost) return;
					if (Game.Instance == null) return;
					if (__state > 0.001f) return;          // wasn't empty before — steady-state, sweep handles
					var after = __instance.GetContents(cell);
					if (after.mass <= 0f) return;          // defensive: AddElement(0,...) or rejected by sim

					byte conduitType;
					if (object.ReferenceEquals(__instance, Game.Instance.gasConduitFlow))
						conduitType = ConduitContentsPacket.CONDUIT_GAS;
					else if (object.ReferenceEquals(__instance, Game.Instance.liquidConduitFlow))
						conduitType = ConduitContentsPacket.CONDUIT_LIQUID;
					else
						return;                            // solid rails or unknown ConduitFlow — out of scope

					ConduitFlowSyncer.Instance?.EnqueueImmediate(cell, conduitType, after);
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[ConduitFlowFastPathPatch] Postfix threw: {ex}");
				}
			}
		}
	}
}
