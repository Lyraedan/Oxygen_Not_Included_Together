using HarmonyLib;
using Klei.AI;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Animation;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
using System;
using System.Linq;
using Shared.Profiling;
using static STRINGS.UI.CLUSTERMAP.ROCKETS;

namespace ONI_Together.Patches.KleiPatches
{
	class KAnimControllerBase_Patches
	{
		/// Playing Overrides

		static bool _allowedToPlayAnims = false;
		public static void AllowAnims() => _allowedToPlayAnims = true;
		public static void ForbidAnims() => _allowedToPlayAnims = false;
		public static bool CanPlayAnims => true;// (MultiplayerSession.InSession && MultiplayerSession.IsClient) ? _allowedToPlayAnims : true;



		///Play() has internal calls to "Queue", prevent duplicate entries
		static bool LockAnimSending = false;
		static void Unlock() => LockAnimSending = false;
		static void SendAnimPacketToClients(KAnimControllerBase __instance, bool queueing, HashedString[] anims, KAnim.PlayMode mode = KAnim.PlayMode.Once, float speed = 1f, float time_offset = 0f)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsClient)
				return;
			if (__instance.gameObject.IsNullOrDestroyed() || !__instance.gameObject.TryGetComponent<KPrefabID>(out var id))
				return;

			if (!id.HasTag(GameTags.BaseMinion) && !id.HasTag(GameTags.Creature)) // Allow BaseMinion and Creature
				return;

			int netId = __instance.GetNetId();
			if(netId == 0)
			{
				DebugConsole.LogWarning("no netId found on " + __instance.GetProperName());
				return;
			}

			if (LockAnimSending)
				return;

			LockAnimSending = true;
			PacketSender.SendToAllClients(new PlayAnimPacket(netId, anims, queueing,mode,speed,time_offset));
		}

		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.Play), [typeof(HashedString), typeof(KAnim.PlayMode), typeof(float), typeof(float)])]
		public class KAnimControllerBase_Play_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, HashedString anim_name, KAnim.PlayMode mode, float speed, float time_offset)
			{
				using var _ = Profiler.Scope();

				try
				{
					if (!MultiplayerSession.InActiveSession)
						return true;
					if (__instance.IsNullOrDestroyed() || !__instance.enabled) return CanPlayAnims;

					if(MultiplayerSession.IsHost)
						SendAnimPacketToClients(__instance, false, [anim_name],mode,speed,time_offset);
					return CanPlayAnims;
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[KAnimControllerBase_Play_Patch.Prefix] {ex}");
					return true;
				}
			}

			public static void Postfix(KAnimControllerBase __instance) => Unlock();
		}

		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.Play), [typeof(HashedString[]), typeof(KAnim.PlayMode)])]
		public class KAnimControllerBase_PlayRange_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, HashedString[] anim_names, KAnim.PlayMode mode)
			{
				using var _ = Profiler.Scope();

				try
				{
					if (!MultiplayerSession.InActiveSession)
						return true;
					if (__instance.IsNullOrDestroyed() || !__instance.enabled) return CanPlayAnims;
					if (MultiplayerSession.IsHost)
						SendAnimPacketToClients(__instance, false, anim_names, mode);
					return CanPlayAnims;
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[KAnimControllerBase_PlayRange_Patch.Prefix] {ex}");
					return true;
				}
			}

			public static void Postfix(KAnimControllerBase __instance) => Unlock();
		}

		[HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.Queue))]
		public class KAnimControllerBase_Queue_Patch
		{
			public static bool Prefix(KAnimControllerBase __instance, HashedString anim_name, KAnim.PlayMode mode, float speed, float time_offset)
			{
				using var _ = Profiler.Scope();

				try
				{
					if (!MultiplayerSession.InActiveSession)
						return true;
					if (__instance.IsNullOrDestroyed() || !__instance.enabled) return CanPlayAnims;
					if (MultiplayerSession.IsHost)
						SendAnimPacketToClients(__instance, true, [anim_name], mode, speed, time_offset);
					return CanPlayAnims;
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[KAnimControllerBase_Queue_Patch.Prefix] {ex}");
					return true;
				}
			}

			public static void Postfix(KAnimControllerBase __instance) => Unlock();
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
