using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Animation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Patches.KleiPatches
{
	internal class SymbolOverrideController_Patch
	{
		// Throttle counters: first 5 errors full log, then 1/100 to avoid flooding
		// under a patch storm (Invariant #10 — unhandled Prefix exception = game crash).
		private static long _addErrorCount;
		private static long _removeErrorCount;
		private static long _removeAllErrorCount;

		private static bool ShouldBroadcast(SymbolOverrideController soc)
		{
			// Broader than IsHostMinion: covers bottlers / storage bins / any networked
			// building that uses SymbolOverrideController for visual state (Bug-G).
			// Still rejects non-networked GameObjects (previews, particles) via NetId==0.
			return Utils.IsHostEntityWithNetId(soc, out _);
		}

		[HarmonyPatch(typeof(SymbolOverrideController), nameof(SymbolOverrideController.AddSymbolOverride))]
		public class SymbolOverrideController_AddSymbolOverride_Patch
		{
			public static void Prefix(SymbolOverrideController __instance, HashedString target_symbol, KAnim.Build.Symbol source_symbol, int priority = 0)
			{
				using var _ = Profiler.Scope();
				try
				{
					if (!ShouldBroadcast(__instance))
						return;
					PacketSender.SendToAllClients(new SymbolOverridePacket(__instance, SymbolOverridePacket.Mode.AddSymbolOverride, target_symbol, source_symbol, priority));
				}
				catch (Exception ex)
				{
					long n = System.Threading.Interlocked.Increment(ref _addErrorCount);
					if (n <= 5 || n % 100 == 0)
						DebugConsole.LogError($"[SymbolOverride.Add] #{n} {ex}");
				}
			}
		}

		[HarmonyPatch(typeof(SymbolOverrideController), nameof(SymbolOverrideController.RemoveSymbolOverride))]
		public class SymbolOverrideController_RemoveSymbolOverride_Patch
		{
			public static void Prefix(SymbolOverrideController __instance, HashedString target_symbol, int priority)
			{
				using var _ = Profiler.Scope();
				try
				{
					if (!ShouldBroadcast(__instance))
						return;
					PacketSender.SendToAllClients(new SymbolOverridePacket(__instance, SymbolOverridePacket.Mode.RemoveSymbolOverride, target_symbol, priority: priority));
				}
				catch (Exception ex)
				{
					long n = System.Threading.Interlocked.Increment(ref _removeErrorCount);
					if (n <= 5 || n % 100 == 0)
						DebugConsole.LogError($"[SymbolOverride.Remove] #{n} {ex}");
				}
			}
		}

		[HarmonyPatch(typeof(SymbolOverrideController), nameof(SymbolOverrideController.RemoveAllSymbolOverrides))]
		public class SymbolOverrideController_RemoveAllSymbolOverrides_Patch
		{
			public static void Prefix(SymbolOverrideController __instance, int priority)
			{
				using var _ = Profiler.Scope();
				try
				{
					if (!ShouldBroadcast(__instance))
						return;
					PacketSender.SendToAllClients(new SymbolOverridePacket(__instance, SymbolOverridePacket.Mode.RemoveAllSymbolsOverrides, priority: priority));
				}
				catch (Exception ex)
				{
					long n = System.Threading.Interlocked.Increment(ref _removeAllErrorCount);
					if (n <= 5 || n % 100 == 0)
						DebugConsole.LogError($"[SymbolOverride.RemoveAll] #{n} {ex}");
				}
			}
		}
	}
}
