using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using System;
using System.Linq;
using Shared.Profiling;
using ONI_Together.Networking.OxySync.Components.Entities;

namespace ONI_Together.Patches.KleiPatches
{
	class KAnimControllerBase_Patches
	{
		internal static bool ShouldSyncAnim(KAnimControllerBase controller, KPrefabID prefabID)
		{
			// Only sync animations for creatures and minions.
			// This is to avoid syncing animations for things like buildings,
			// which can cause issues with the game.
			if (prefabID.HasTag(GameTags.Creature) || prefabID.HasTag(GameTags.BaseMinion))
				return true;
			
			return false;
		}

		private static bool IsNavigatorAnim(KAnimControllerBase controller, HashedString animName)
		{
			using var _ = Profiler.Scope();

			if (!controller.TryGetComponent<Navigator>(out var navigator) || navigator == null)
				return false;

			// Check if the current animation is the idle animation for the navigator.
			if (navigator.NavGrid != null &&navigator.NavGrid.GetIdleAnim(navigator.CurrentNavType) == animName)
				return true;
			
			// If the current animation is not the idle animation, check if it is part of an active transition.
			var activeTransition = navigator.transitionDriver?.GetTransition;
			if (activeTransition == null)
				return false;

			return animName == activeTransition.anim || animName == activeTransition.preAnim;
		}

		internal static bool CanPlayAnim(KAnimControllerBase controller, out AnimSyncer animSyncer, HashedString[] animNames)
		{
			using var _ = Profiler.Scope();
			animSyncer = null;

			if (animNames == null || animNames.Length == 0 || animNames.FirstOrDefault() == default)
				return true;

			if (!MultiplayerSession.InActiveSession || (MultiplayerSession.IsHost && !MultiplayerSession.SessionHasPlayers))
				return true;

			if (controller == null || controller.gameObject.IsNullOrDestroyed())
				return true;

			if (!controller.TryGetComponent<KPrefabID>(out var prefabId))
				return true;
			
			if (!ShouldSyncAnim(controller, prefabId))
				return true;
			
			if (!controller.TryGetComponent<AnimSyncer>(out var _animSyncer))
			{
				// Allow the animation to play anyway, but log a warning.
				// This should never happen, as the AnimSyncer is added to all creatures and minions in MinionMultiplayerInitializer and CreatureMultiplayerInitializer.
				// On the client, we may be able to see this log during the initialize process.
				// Therefore, we can ignore warnings at the beginning of the log file.
				DebugConsole.LogAssert($"[KAnimControllerBase_Patches] AnimSyncer not found on {controller.gameObject.GetProperName()}");
				return true;
			}

			// If the animate is from the navigator, allow it to play on the client.
			// Meanwhile, return here so the host would not send this request to the client.
			// these animations are controlled by 'navigator.BeginTransition' and 'navigator.EndTransition' on the client.
			if (IsNavigatorAnim(controller, animNames.FirstOrDefault()))
				return true;

			if (MultiplayerSession.IsClient)
			{
				// If the animate is from the host, we should allow it to play on the client.
				if (_animSyncer.IsInSyncedPlaybackScope())
					return true;

				// For all other animations on the client, block them from playing directly.
				return false;
			}
			
			// Host with active session, and has players: use the local anim syncer.
			animSyncer = _animSyncer;

			return true;
		}

		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.Play), [typeof(HashedString), typeof(KAnim.PlayMode), typeof(float), typeof(float)])]
		public class KAnimControllerBase_Play_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, HashedString anim_name, KAnim.PlayMode mode, float speed, float time_offset)
			{
				using var _ = Profiler.Scope();

				if (!CanPlayAnim(__instance, out AnimSyncer animSyncer, [anim_name]))
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

				if (!CanPlayAnim(__instance, out AnimSyncer animSyncer, anim_names))
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

				if (!CanPlayAnim(__instance, out AnimSyncer animSyncer, [anim_name]))
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
					{
						if (__instance.gameObject.GetComponent<AnimSyncer>() is AnimSyncer animSyncer && animSyncer.IsInOverrideScope())
							return true;
						return TogglingOverrideFromPacket;
					}

					// Animation patch disabled globally.
					// Console.WriteLine("sending addAnimOveridePacket");
					// PacketSender.SendToAllClients(new ToggleAnimOverridePacket(__instance.gameObject, kanim_file, priority));
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
					{
						if (__instance.gameObject.GetComponent<AnimSyncer>() is AnimSyncer animSyncer && animSyncer.IsInOverrideScope())
							return true;
						return TogglingOverrideFromPacket;
					}

					// Animation patch disabled globally.
					// Console.WriteLine("sending removeAnimOveridePacket");
					// PacketSender.SendToAllClients(new ToggleAnimOverridePacket(__instance.gameObject, kanim_file));
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

					// Animation patch disabled globally.
					// PacketSender.SendToAllClients(new SymbolVisibilityTogglePacket(__instance, symbol, is_visible));
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[KAnimControllerBase_SetSymbolVisiblity_Patch.Prefix] {ex}");
				}
			}
		}
	}
}