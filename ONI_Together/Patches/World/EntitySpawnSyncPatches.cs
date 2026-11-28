using HarmonyLib;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	/// <summary>
	/// Makes critter and gameplay entity spawning host-authoritative (Issue #154):
	/// blocks clients from locally rolling/spawning eggs, hatched babies, grown adults,
	/// butcher drops, and plant seeds, and replicates host-side spawns via SpawnUtils.BroadcastSpawn.
	/// </summary>
	internal static class EntitySpawnSyncPatches
	{
		[ThreadStatic]
		private static bool _capturingHostSpawns;
		[ThreadStatic]
		private static List<GameObject> _capturedSpawns;

		private static void BeginCapture()
		{
			_capturingHostSpawns = true;
			_capturedSpawns ??= new List<GameObject>(4);
			_capturedSpawns.Clear();
		}

		private static void EndCaptureAndBroadcast()
		{
			_capturingHostSpawns = false;
			if (_capturedSpawns == null || _capturedSpawns.Count == 0)
				return;

			for (int i = 0; i < _capturedSpawns.Count; i++)
			{
				var go = _capturedSpawns[i];
				if (go != null && !go.IsNullOrDestroyed())
					SpawnUtils.BroadcastSpawn(go, go.activeSelf);
			}
			_capturedSpawns.Clear();
		}

		private static void NotifyDestroyedOnClients(GameObject go)
		{
			if (!MultiplayerSession.IsHostInSession || go == null)
				return;

			if (go.TryGetComponent<NetworkIdentity>(out var identity) && identity.NetId != 0)
			{
				PacketSender.SendToAllClients(new GroundItemPickedUpPacket { NetId = identity.NetId });
			}
		}

		[HarmonyPatch(typeof(Util), nameof(Util.KInstantiate), new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(GameObject), typeof(string), typeof(bool), typeof(int) })]
		internal static class Util_KInstantiate_Capture_Patch
		{
			static void Postfix(GameObject __result)
			{
				if (_capturingHostSpawns && __result != null && __result.GetComponent<KPrefabID>() != null)
					_capturedSpawns?.Add(__result);
			}
		}

		// --- Eggs laid by critters ---

		[HarmonyPatch(typeof(FertilityMonitor.Instance), nameof(FertilityMonitor.Instance.ShowEgg))]
		internal static class FertilityMonitor_ShowEgg_Patch
		{
			static bool Prefix()
			{
				return !MultiplayerSession.InActiveSession || !MultiplayerSession.IsClient;
			}
		}

		[HarmonyPatch(typeof(FertilityMonitor.Instance), nameof(FertilityMonitor.Instance.LayEgg))]
		internal static class FertilityMonitor_LayEgg_Patch
		{
			static bool Prefix(FertilityMonitor.Instance __instance, out GameObject __state)
			{
				__state = null;
				if (!MultiplayerSession.InActiveSession)
					return true;

				if (MultiplayerSession.IsClient)
					return false;

				__state = __instance?.egg;
				return true;
			}

			static void Postfix(GameObject __state)
			{
				if (!MultiplayerSession.IsHostInSession || __state == null || __state.IsNullOrDestroyed())
					return;

				SpawnUtils.BroadcastSpawn(__state, __state.activeSelf);
			}
		}

		// --- Egg hatching into baby critters ---

		[HarmonyPatch(typeof(IncubationMonitor), nameof(IncubationMonitor.SpawnBaby))]
		internal static class IncubationMonitor_SpawnBaby_Patch
		{
			static bool Prefix(IncubationMonitor.Instance smi)
			{
				if (!MultiplayerSession.InActiveSession)
					return true;

				if (MultiplayerSession.IsClient)
					return false;

				NotifyDestroyedOnClients(smi?.gameObject);
				BeginCapture();
				return true;
			}

			static void Postfix()
			{
				if (_capturingHostSpawns)
					EndCaptureAndBroadcast();
			}
		}

		[HarmonyPatch(typeof(IncubationMonitor), nameof(IncubationMonitor.SpawnShell))]
		internal static class IncubationMonitor_SpawnShell_Patch
		{
			static bool Prefix(ref GameObject __result)
			{
				if (MultiplayerSession.InActiveSession && MultiplayerSession.IsClient)
				{
					__result = null;
					return false;
				}
				return true;
			}

			static void Postfix(GameObject __result)
			{
				if (MultiplayerSession.IsHostInSession && __result != null && !__result.IsNullOrDestroyed())
					SpawnUtils.BroadcastSpawn(__result, __result.activeSelf);
			}
		}

		[HarmonyPatch(typeof(IncubationMonitor), nameof(IncubationMonitor.SpawnEggInnards))]
		internal static class IncubationMonitor_SpawnEggInnards_Patch
		{
			static bool Prefix(ref GameObject __result)
			{
				if (MultiplayerSession.InActiveSession && MultiplayerSession.IsClient)
				{
					__result = null;
					return false;
				}
				return true;
			}

			static void Postfix(GameObject __result)
			{
				if (MultiplayerSession.IsHostInSession && __result != null && !__result.IsNullOrDestroyed())
					SpawnUtils.BroadcastSpawn(__result, __result.activeSelf);
			}
		}

		[HarmonyPatch(typeof(IncubationMonitor), nameof(IncubationMonitor.SpawnGenericEgg))]
		internal static class IncubationMonitor_SpawnGenericEgg_Patch
		{
			static bool Prefix()
			{
				if (!MultiplayerSession.InActiveSession)
					return true;

				if (MultiplayerSession.IsClient)
					return false;

				BeginCapture();
				return true;
			}

			static void Postfix()
			{
				if (_capturingHostSpawns)
					EndCaptureAndBroadcast();
			}
		}

		// --- Baby critters maturing into adults ---

		[HarmonyPatch(typeof(BabyMonitor.Instance), nameof(BabyMonitor.Instance.SpawnAdult))]
		internal static class BabyMonitor_SpawnAdult_Patch
		{
			static bool Prefix(BabyMonitor.Instance __instance)
			{
				if (!MultiplayerSession.InActiveSession)
					return true;

				if (MultiplayerSession.IsClient)
					return false;

				NotifyDestroyedOnClients(__instance?.gameObject);
				BeginCapture();
				return true;
			}

			static void Postfix()
			{
				if (_capturingHostSpawns)
					EndCaptureAndBroadcast();
			}
		}

		// --- Critter butcher drops (meat, eggshell, etc.) ---

		[HarmonyPatch(typeof(Butcherable), nameof(Butcherable.CreateDrops))]
		internal static class Butcherable_CreateDrops_Patch
		{
			static bool Prefix(ref GameObject[] __result)
			{
				if (MultiplayerSession.InActiveSession && MultiplayerSession.IsClient)
				{
					__result = Array.Empty<GameObject>();
					return false;
				}
				return true;
			}

			static void Postfix(GameObject[] __result)
			{
				if (!MultiplayerSession.IsHostInSession || __result == null)
					return;

				for (int i = 0; i < __result.Length; i++)
				{
					var drop = __result[i];
					if (drop != null && !drop.IsNullOrDestroyed())
						SpawnUtils.BroadcastSpawn(drop, drop.activeSelf);
				}
			}
		}

		// --- Plant seed drops (including digging up wild plants / buried seeds) ---

		[HarmonyPatch(typeof(SeedProducer), nameof(SeedProducer.ProduceSeed))]
		internal static class SeedProducer_ProduceSeed_Patch
		{
			static bool Prefix(ref GameObject __result)
			{
				if (MultiplayerSession.InActiveSession && MultiplayerSession.IsClient)
				{
					__result = null;
					return false;
				}
				return true;
			}

			static void Postfix(GameObject __result)
			{
				if (MultiplayerSession.IsHostInSession && __result != null && !__result.IsNullOrDestroyed())
					SpawnUtils.BroadcastSpawn(__result, __result.activeSelf);
			}
		}

		// ProduceSeed returns null on clients, but both callers Trigger an event on the seed it
		// returns, which throws on null. The host's seed arrives through the spawn broadcast, so
		// clients skip the seed roll on harvest and the seed drop on uproot.
		[HarmonyPatch(typeof(SeedProducer), nameof(SeedProducer.CropPickedInternal), typeof(WorkerBase), typeof(bool))]
		internal static class SeedProducer_CropPickedInternal_Patch
		{
			static bool Prefix() => !(MultiplayerSession.InActiveSession && MultiplayerSession.IsClient);
		}

		[HarmonyPatch(typeof(SeedProducer), nameof(SeedProducer.DropSeed))]
		internal static class SeedProducer_DropSeed_Patch
		{
			static bool Prefix(SeedProducer __instance)
			{
				if (!(MultiplayerSession.InActiveSession && MultiplayerSession.IsClient))
					return true;
				__instance.droppedSeedAlready = true;
				return false;
			}
		}
	}
}
