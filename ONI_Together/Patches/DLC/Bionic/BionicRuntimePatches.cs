using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Bionic;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Bionic
{
	internal static class BionicRuntimeSync
	{
		private static int _outcomeApplyDepth;
		internal static bool IsApplyingOutcome => _outcomeApplyDepth > 0;

		public static void ResetSessionState() => _outcomeApplyDepth = 0;

		internal static bool ShouldRunAuthoritativeGameplay(bool inSession, bool isHost)
			=> !inSession || isHost;

		internal static bool ShouldRunExplosion(bool inSession, bool isHost, bool isApplying)
			=> !inSession || isHost || isApplying;

		internal static bool TryCapture(Electrobank bank, out BionicElectrobankStatePacket state)
		{
			state = null;
			int netId = bank?.GetNetIdentity()?.NetId ?? 0;
			if (netId == 0)
				return false;
			Traverse traverse = Traverse.Create(bank);
			bool hasLifetime = bank is SelfChargingElectrobank;
			state = new BionicElectrobankStatePacket
			{
				NetId = netId,
				CurrentHealth = Mathf.Clamp(traverse.Field("currentHealth").GetValue<float>(), 0f,
					BionicElectrobankStatePacket.MaxHealth),
				Charge = Mathf.Clamp(traverse.Field("charge").GetValue<float>(), 0f,
					BionicElectrobankStatePacket.MaxCharge),
				TimeSincePowerDrawn = Mathf.Clamp(traverse.Field("timeSincePowerDrawn").GetValue<float>(), 0f,
					BionicElectrobankStatePacket.MaxTimeSincePowerDrawn),
				HasLifetime = hasLifetime,
				LifetimeRemaining = hasLifetime ? Mathf.Clamp(Traverse.Create(bank).Field("lifetimeRemaining")
					.GetValue<float>(), 0f, BionicElectrobankStatePacket.MaxLifetime) : 0f
			};
			return state.IsWireValid();
		}

		internal static bool TryApply(BionicElectrobankStatePacket state)
		{
			if (state == null || !state.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGetComponent(state.NetId, out Electrobank bank) || bank == null ||
			    state.HasLifetime != (bank is SelfChargingElectrobank))
				return false;
			Traverse traverse = Traverse.Create(bank);
			float previousHealth = traverse.Field("currentHealth").GetValue<float>();
			traverse.Field("currentHealth").SetValue(state.CurrentHealth);
			traverse.Field("charge").SetValue(state.Charge);
			traverse.Field("timeSincePowerDrawn").SetValue(state.TimeSincePowerDrawn);
			if (state.HasLifetime)
				Traverse.Create(bank).Field("lifetimeRemaining").SetValue(state.LifetimeRemaining);
			traverse.Method("UpdateRadiationEmitter").GetValue();
			UpdateHealthDisplay(bank, previousHealth, state.CurrentHealth, traverse);
			return true;
		}

		internal static void SendState(Electrobank bank)
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !TryCapture(bank, out BionicElectrobankStatePacket state))
				return;
			PacketSender.SendToAllClients(state);
		}

		internal static void ApplyOutcome(System.Action action)
		{
			_outcomeApplyDepth++;
			try
			{
				action();
			}
			finally
			{
				_outcomeApplyDepth--;
			}
		}

		private static void UpdateHealthDisplay(
			Electrobank bank,
			float previousHealth,
			float currentHealth,
			Traverse traverse)
		{
			if (currentHealth < previousHealth)
			{
				traverse.Field("lastDamageTime").SetValue(Time.time);
				if (bank.healthBar == null)
					bank.CreateHealthBar();
			}
			bank.healthBar?.Update();
		}
	}

	[HarmonyPatch(typeof(Electrobank), nameof(Electrobank.Sim1000ms), typeof(float))]
	internal static class ElectrobankSim1000Patch
	{
		internal static bool Prefix()
			=> BionicRuntimeSync.ShouldRunAuthoritativeGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(Electrobank __instance)
			=> BionicRuntimeSync.SendState(__instance);
	}

	[HarmonyPatch(typeof(SelfChargingElectrobank), nameof(SelfChargingElectrobank.Sim200ms), typeof(float))]
	internal static class SelfChargingElectrobankSim200Patch
	{
		internal static bool Prefix()
			=> BionicRuntimeSync.ShouldRunAuthoritativeGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(SelfChargingElectrobank __instance)
			=> BionicRuntimeSync.SendState(__instance);
	}
}
