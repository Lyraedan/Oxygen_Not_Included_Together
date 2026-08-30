using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Animation;
using ONI_Together.Networking.Packets.Core;
using System;
using Shared.Profiling;
using ONI_Together.Networking.OxySync.Components;

namespace ONI_Together.Patches.KleiPatches
{
	class KAnimControllerBase_Patches
	{
		internal static bool ShouldSyncAnim(KAnimControllerBase controller, KPrefabID prefabID)
		{
			// Only suppress local state-machine animation writes for entities whose
			// visual state is authoritative on the host. UI and unrelated local
			// animations must continue to run normally on clients.
			if (prefabID.HasTag(GameTags.Creature) || prefabID.HasTag(GameTags.BaseMinion))
				return true;

			return false;
		}

		internal static bool CanPlayAnim(KAnimControllerBase controller, out AnimSyncer animSyncer)
		{
			using var _ = Profiler.Scope();
			animSyncer = null;

			if (!MultiplayerSession.InActiveSession)
				return true;

			if (MultiplayerSession.IsClient)
				return true;

			if (controller == null || controller.gameObject.IsNullOrDestroyed())
				return true;

			if (!controller.TryGetComponent<KPrefabID>(out var prefabId))
				return true;
			
			if (!ShouldSyncAnim(controller, prefabId))
				return true;
			
			if (!controller.TryGetComponent<AnimSyncer>(out var _animSyncer))
			{
				DebugConsole.LogWarning($"[KAnimControllerBase_Patches] AnimSyncer not found on {controller.GetProperName()}");
				// Allow the animation to play anyway, but log a warning. This should not happen in a properly configured multiplayer session.
				return true;
			}

			// Clients should not play animations directly. they should only be played through packets from the host.
			if (MultiplayerSession.IsClient)
				return false;
			
			if (MultiplayerSession.IsHost && MultiplayerSession.SessionHasPlayers)
				animSyncer = _animSyncer;

			return true;
		}


		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.Play), [typeof(HashedString), typeof(KAnim.PlayMode), typeof(float), typeof(float)])]
		public class KAnimControllerBase_Play_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, HashedString anim_name, KAnim.PlayMode mode, float speed, float time_offset)
			{
				using var _ = Profiler.Scope();

				if (!CanPlayAnim(__instance, out AnimSyncer animSyncer))
					return false;

                animSyncer?.RequestToPlayAnim(false, [anim_name], mode, speed, time_offset);

				return true;
			}
        }

        [HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.Play), [typeof(HashedString[]), typeof(KAnim.PlayMode)])]
		public class KAnimControllerBase_PlayRange_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, HashedString[] anim_names, KAnim.PlayMode mode)
			{
				using var _ = Profiler.Scope();

				if (!CanPlayAnim(__instance, out AnimSyncer animSyncer))
					return false;
				
				animSyncer?.RequestToPlayAnim(false, anim_names, mode);
				
				return true;
			}
		}

		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.Queue))]
		public class KAnimControllerBase_Queue_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, HashedString anim_name, KAnim.PlayMode mode, float speed, float time_offset)
			{
				using var _ = Profiler.Scope();

				if (!CanPlayAnim(__instance, out AnimSyncer animSyncer))
					return false;
				
				animSyncer?.RequestToPlayAnim(true, [anim_name], mode, speed, time_offset);
				
				return true;
			}
		}

		/// Kanim Overrides

		private static bool TogglingOverrideFromPacket = false;
		internal static void AddKanimOverride(KAnimControllerBase kbac, string kanim, float priority)
		{
			using var _ = Profiler.Scope();

			TogglingOverrideFromPacket = true;
			if (Assets.TryGetAnim(kanim, out var anim))
			{
				kbac.AddAnimOverrides(anim, priority);
			}
			else
				DebugConsole.LogWarning("could not find anim " + kanim);

			Console.WriteLine("Adding Kanim Override " + kanim);
			TogglingOverrideFromPacket = false;
		}

		internal static void RemoveKanimOverride(KAnimControllerBase kbac, string kanim)
		{
			using var _ = Profiler.Scope();

			TogglingOverrideFromPacket = true;
			if (Assets.TryGetAnim(kanim, out var anim))
			{
				kbac.RemoveAnimOverrides(anim);
			}
			else
				DebugConsole.LogWarning("could not find anim " + kanim);
			Console.WriteLine("Removing Kanim Override " + kanim);
			TogglingOverrideFromPacket = false;
		}


		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.AddAnimOverrides))]
		public class KAnimControllerBase_AddAnimOverrides_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, KAnimFile kanim_file, float priority = 0f)
			{
				using var _ = Profiler.Scope();

				try
				{
					if (!MultiplayerSession.InActiveSession) return kanim_file != null;

					//leave to minions for now, potentially remove later
					if (!__instance.HasTag(GameTags.BaseMinion))
						return kanim_file != null;

					if (MultiplayerSession.IsClient)
						return TogglingOverrideFromPacket;

					Console.WriteLine("sending addAnimOveridePacket");
					PacketSender.SendToAllClients(new ToggleAnimOverridePacket(__instance.gameObject, kanim_file, priority));
					return kanim_file != null;
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[KAnimControllerBase_AddAnimOverrides_Patch.Prefix] {ex}");
					return kanim_file != null;
				}
			}
		}

		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.RemoveAnimOverrides))]
		public class KAnimControllerBase_RemoveAnimOverrides_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, KAnimFile kanim_file)
			{
				using var _ = Profiler.Scope();

				try
				{
					if (!MultiplayerSession.InActiveSession) return kanim_file != null;

					//leave to minions for now, potentially remove later
					if (!__instance.HasTag(GameTags.BaseMinion))
						return kanim_file != null;

					if (MultiplayerSession.IsClient)
						return TogglingOverrideFromPacket;

					Console.WriteLine("sending removeAnimOveridePacket");
					PacketSender.SendToAllClients(new ToggleAnimOverridePacket(__instance.gameObject, kanim_file));
					return kanim_file != null;
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[KAnimControllerBase_RemoveAnimOverrides_Patch.Prefix] {ex}");
					return kanim_file != null;
				}
			}
		}

		/// Symbol Visibility

		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.SetSymbolVisiblity))]
		public class KAnimControllerBase_SetSymbolVisiblity_Patch
		{
			public static void Prefix(KAnimControllerBase __instance, KAnimHashedString symbol, bool is_visible)
			{
				using var _ = Profiler.Scope();

				try
				{
					if (!Utils.IsHostMinion(__instance))
						return;

					PacketSender.SendToAllClients(new SymbolVisibilityTogglePacket(__instance, symbol, is_visible));
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[KAnimControllerBase_SetSymbolVisiblity_Patch.Prefix] {ex}");
				}
			}
		}
	}
}