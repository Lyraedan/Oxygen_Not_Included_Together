using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using System;
using System.Linq;
using Shared.Profiling;
using ONI_Together.Networking.OxySync.Components.Entities;

namespace ONI_Together.Patches.KleiPatches
{
	class KAnimControllerBase_Patches
	{
		internal static readonly bool ENABLE_LOG = false;

		internal static bool ShouldSyncAnim(KAnimControllerBase controller)
		{
			if (!controller.TryGetComponent<KPrefabID>(out var prefabID))
				return false;

			// Only sync animations for creatures and minions.
			// This is to avoid syncing animations for things like buildings,
			// which can cause issues with the game.
			if (prefabID.HasTag(GameTags.Creature) || prefabID.HasTag(GameTags.BaseMinion))
				return true;
			
			return false;
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
			
			if (!ShouldSyncAnim(controller))
				return true;
			
			if (!controller.TryGetComponent<AnimSyncer>(out var _animSyncer))
			{
				// Allow the animation to play anyway, but log a warning.
				// This should never happen, as the AnimSyncer is added to all creatures and minions in MinionMultiplayerInitializer and CreatureMultiplayerInitializer.
				// On the client, we may be able to see this log during the initialize process.
				// Therefore, we can ignore warnings at the beginning of the log file on the client side.
				DebugConsole.LogAssert(
					$"[KAnimControllerBase_Patches]{controller.gameObject.GetProperName()}:(unknown netid) " +
					$"AnimSyncer not found. anim: {animNames.FirstOrDefault()}");
				return true;
			}

			if (ENABLE_LOG)
				DebugConsole.LogNonImportant(
					$"[KAnimControllerBase_Patches]{_animSyncer.EntityName}:{_animSyncer.NetId} " +
					$"Processing {_animSyncer.ResolveAnimName(animNames.FirstOrDefault())}:{animNames.FirstOrDefault()}");

			// If the animate is from the navigator, allow it to play on the client.
			// Meanwhile, return here so the host would not send this request to the client.
			// these animations are controlled by 'navigator.BeginTransition' and 'navigator.EndTransition' on the client.
			if (_animSyncer.IsNavigatorAnim(animNames.FirstOrDefault()))
			{
				if (ENABLE_LOG)
					DebugConsole.LogNonImportant(
						$"[KAnimControllerBase_Patches]{_animSyncer.EntityName}:{_animSyncer.NetId} " +
						$"Is navigator anim: {_animSyncer.ResolveAnimName(animNames.FirstOrDefault())}:{animNames.FirstOrDefault()}");
				return true;
			}

			if (MultiplayerSession.IsClient)
			{
				// If the animate is from the host, we should allow it to play on the client.
				if (_animSyncer.IsInSyncedPlaybackScope())
				{
					if (ENABLE_LOG)
						DebugConsole.LogNonImportant(
							$"[KAnimControllerBase_Patches]{_animSyncer.EntityName}:{_animSyncer.NetId} " +
							$"In synced playback scope: {_animSyncer.ResolveAnimName(animNames.FirstOrDefault())}:{animNames.FirstOrDefault()}");
					return true;
				}
				
				if (ENABLE_LOG)
					DebugConsole.LogNonImportant(
						$"[KAnimControllerBase_Patches]{_animSyncer.EntityName}:{_animSyncer.NetId} Not in synced playback scope: " +
						$"{_animSyncer.ResolveAnimName(animNames.FirstOrDefault())}:{animNames.FirstOrDefault()}");

				// For all other animations on the client, block them from playing directly.
				return false;
			}

			if (ENABLE_LOG)
				DebugConsole.LogNonImportant(
					$"[KAnimControllerBase_Patches]{_animSyncer.EntityName}:{_animSyncer.NetId} Reached host with active session and has players. " +
					$"{_animSyncer.ResolveAnimName(animNames.FirstOrDefault())}:{animNames.FirstOrDefault()}");
			
			// Host with active session, and has players: set the syncer to send animations to clients.
			// Meanwhile, we do not need to sync those animations that are handled by the client locally.
			if (!_animSyncer.IsInSyncedPlaybackScope())
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
		
		private static bool TryProcessOverride(KAnimControllerBase kbac, bool isAdding, KAnimFile kanim_file, float priority = 0f)
		{
			using var _ = Profiler.Scope();

			if (ENABLE_LOG)
				DebugConsole.Log($"[KAnimControllerBase_Patches][OVERRIDE]{kbac.gameObject.GetProperName()} Start Processing kanim file {kanim_file?.name}");

			if (!MultiplayerSession.InActiveSession || (MultiplayerSession.IsHost && !MultiplayerSession.SessionHasPlayers))
				return true;
			
			if (kanim_file == null || string.IsNullOrEmpty(kanim_file.name))
				return true;

			if (kbac == null || kbac.gameObject.IsNullOrDestroyed())
				return true;
			
			if (!ShouldSyncAnim(kbac))
				return true;
			
			if (!kbac.TryGetComponent<AnimSyncer>(out var syncer))
			{
				DebugConsole.LogAssert($"[KAnimControllerBase_Patches][OVERRIDE]{kbac.gameObject.GetProperName()} AnimSyncer not found.");
				return true;
			}

			if (MultiplayerSession.IsClient)
			{
				bool result = syncer.IsInOverrideScope();
				if (ENABLE_LOG)
					DebugConsole.LogNonImportant(
						$"[KAnimControllerBase_Patches][OVERRIDE]{syncer.EntityName}:{syncer.NetId} " +
						$"Client processing kanim file {kanim_file.name}, IsInOverrideScope: {result}");
				
				// For the client, we only process the override if we are currently in the override scope.
				return result;
			}
			if (ENABLE_LOG)
				DebugConsole.Log($"[KAnimControllerBase_Patches][OVERRIDE]{syncer.EntityName}:{syncer.NetId} Host processing kanim file {kanim_file.name}");
			
			// Host with active session, and has players: set the syncer to send animations to clients.
			// Meanwhile, we do not need to sync those animations that are handled by the client locally.
			if (!syncer.IsInOverrideScope())
				syncer.RequestUpdateKanimOverride(kanim_file.name, isAdding, priority);

			return true;
		}

		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.AddAnimOverrides))]
		public class KAnimControllerBase_AddAnimOverrides_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, KAnimFile kanim_file, float priority)
			{
				return TryProcessOverride(__instance, true, kanim_file, priority);
			}
		}

		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.RemoveAnimOverrides))]
		public class KAnimControllerBase_RemoveAnimOverrides_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, KAnimFile kanim_file)
			{
				return TryProcessOverride(__instance, false, kanim_file);
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
					if (ENABLE_LOG)
						DebugConsole.LogNonImportant(
							$"[KAnimControllerBase_Patches][SYMBOL_VISIBILITY]{__instance.gameObject.GetProperName()} " +
							$"SetSymbolVisiblity called for symbol {symbol} with is_visible={is_visible}");

					if (__instance == null || __instance.gameObject.IsNullOrDestroyed())
						return;
					
					if (!ShouldSyncAnim(__instance))
						return;

					if (__instance.gameObject.GetComponent<AnimSyncer>() is AnimSyncer animSyncer)
					{
						if (ENABLE_LOG)
							DebugConsole.LogNonImportant(
								$"[KAnimControllerBase_Patches][SYMBOL_VISIBILITY]{__instance.gameObject.GetProperName()} " +
								$"Requesting symbol visibility change for symbol {symbol} to is_visible={is_visible}");
						animSyncer.RequestSymbolVisibilityChange(symbol, is_visible);
					}
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[KAnimControllerBase_SetSymbolVisiblity_Patch.Prefix] {ex}");
				}
			}
		}
	}
}