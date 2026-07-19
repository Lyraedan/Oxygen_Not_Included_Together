using System;
using HarmonyLib;
using Klei.AI;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DuplicantActions;
using Shared.Profiling;

namespace ONI_Together.Patches.Duplicant
{
	internal class EffectsPatch
	{
		private static int _packetApplyDepth;
		private static bool IsApplyingPacket => _packetApplyDepth > 0;

		internal static bool ShouldRunMutation(
			bool inSession,
			bool isHost,
			bool isApplyingPacket,
			bool hasNetworkIdentity)
			=> !inSession || isHost || isApplyingPacket || !hasNetworkIdentity;

		internal static bool ShouldPredictLocally(
			bool inSession,
			bool isHost,
			bool isApplyingPacket,
			bool hasNetworkIdentity)
			=> inSession && !isHost && !isApplyingPacket && hasNetworkIdentity;

		public static EffectInstance AddEffect(
			Effects effects,
			string effectId,
			bool shouldSave,
			float timeRemaining)
		{
			using var _ = Profiler.Scope();
			Effect effect = Db.Get().effects.TryGet(effectId);
			if (effect == null)
			{
				DebugConsole.LogWarning("Could not find effect with id " + effectId);
				return null;
			}

			EffectInstance instance = AddLocally(effects, effect, shouldSave, null);
			if (instance != null)
				instance.timeRemaining = timeRemaining;
			return instance;
		}

		public static void RemoveEffect(Effects effects, HashedString effectId)
		{
			using var _ = Profiler.Scope();
			_packetApplyDepth++;
			try
			{
				effects.Remove(effectId);
			}
			finally
			{
				_packetApplyDepth--;
			}
		}

		private static EffectInstance AddLocally(
			Effects effects,
			Effect effect,
			bool shouldSave,
			Func<string, object, string> resolveTooltip)
		{
			_packetApplyDepth++;
			try
			{
				return effects.Add(effect, shouldSave, resolveTooltip);
			}
			finally
			{
				_packetApplyDepth--;
			}
		}

		private static bool TryGetIdentity(Effects effects, out NetworkIdentity identity)
		{
			identity = effects?.GetComponent<NetworkIdentity>();
			return identity != null && identity.NetId != 0;
		}

		[HarmonyPatch(typeof(Effects), nameof(Effects.Add),
			[typeof(Effect), typeof(bool), typeof(Func<string, object, string>)])]
		public class EffectsAddPatch
		{
			public static bool Prefix(
				Effects __instance,
				Effect newEffect,
				bool should_save,
				Func<string, object, string> resolveTooltipCallback,
				ref EffectInstance __result)
			{
				using var scope = Profiler.Scope();
				bool hasIdentity = TryGetIdentity(__instance, out _);
				if (ShouldPredictLocally(MultiplayerSession.InSession,
					MultiplayerSession.IsHost, IsApplyingPacket, hasIdentity))
				{
					__result = AddLocally(__instance, newEffect, should_save, resolveTooltipCallback);
					return false;
				}

				return ShouldRunMutation(MultiplayerSession.InSession,
					MultiplayerSession.IsHost, IsApplyingPacket, hasIdentity);
			}

			public static void Postfix(Effects __instance, Effect newEffect)
			{
				if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
				    IsApplyingPacket || !TryGetIdentity(__instance, out NetworkIdentity identity))
					return;
				ScheduleHostSnapshot(identity, newEffect);
			}
		}

		private static void ScheduleHostSnapshot(NetworkIdentity identity, Effect effect)
		{
			if (GameScheduler.Instance == null)
			{
				SendHostSnapshot(identity, effect);
				return;
			}
			GameScheduler.Instance.ScheduleNextFrame("ONI Together effect snapshot",
				_ => SendHostSnapshot(identity, effect));
		}

		private static void SendHostSnapshot(NetworkIdentity identity, Effect effect)
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    identity == null || identity.NetId == 0 || effect == null)
				return;
			EffectInstance current = identity.GetComponent<Effects>()?.Get(effect);
			ToggleEffectPacket packet = current != null
				? new ToggleEffectPacket(identity, current)
				: new ToggleEffectPacket(identity, effect.IdHash);
			PacketSender.SendToAllClients(packet);
		}

		[HarmonyPatch(typeof(Effects), nameof(Effects.Remove), [typeof(HashedString)])]
		public class EffectsRemovePatch
		{
			public static bool Prefix(Effects __instance, HashedString effect_id)
			{
				using var _ = Profiler.Scope();
				bool hasIdentity = TryGetIdentity(__instance, out NetworkIdentity identity);
				bool shouldRun = ShouldRunMutation(MultiplayerSession.InSession,
					MultiplayerSession.IsHost, IsApplyingPacket, hasIdentity);
				if (shouldRun && MultiplayerSession.InSession && MultiplayerSession.IsHost &&
				    !IsApplyingPacket && hasIdentity)
					PacketSender.SendToAllClients(new ToggleEffectPacket(identity, effect_id));
				return shouldRun;
			}
		}
	}
}
