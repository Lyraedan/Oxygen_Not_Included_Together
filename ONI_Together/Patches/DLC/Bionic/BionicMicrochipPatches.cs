using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Bionic;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Bionic
{
	internal static class BionicMicrochipSync
	{
		internal static bool ShouldCreateMicrochip(bool inSession, bool isHost)
			=> !inSession || isHost;

		internal static bool TryCapture(
			BionicMicrochipMonitor.Instance smi,
			out BionicMicrochipProgressStatePacket state)
		{
			state = null;
			int netId = smi?.gameObject.GetNetIdentity()?.NetId ?? 0;
			if (netId == 0)
				return false;
			state = new BionicMicrochipProgressStatePacket
			{
				NetId = netId,
				Progress = Mathf.Clamp01(smi.Progress)
			};
			return state.IsWireValid();
		}

		internal static bool TryApply(BionicMicrochipProgressStatePacket state)
		{
			if (state == null || !state.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(state.NetId, out var identity) || identity?.gameObject == null)
				return false;
			BionicMicrochipMonitor.Instance smi =
				identity.gameObject.GetSMI<BionicMicrochipMonitor.Instance>();
			if (smi == null)
				return false;
			smi.sm.Progress.Set(state.Progress, smi);
			return true;
		}

		internal static void SendState(BionicMicrochipMonitor.Instance smi)
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost ||
			    !TryCapture(smi, out BionicMicrochipProgressStatePacket state))
				return;
			PacketSender.SendToAllClients(state);
		}
	}

	[HarmonyPatch(typeof(BionicMicrochipMonitor), nameof(BionicMicrochipMonitor.ProgressUpdate),
		typeof(BionicMicrochipMonitor.Instance), typeof(float))]
	internal static class BionicMicrochipProgressUpdatePatch
	{
		internal static bool Prefix()
			=> BionicRuntimeSync.ShouldRunAuthoritativeGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(BionicMicrochipMonitor.Instance smi)
			=> BionicMicrochipSync.SendState(smi);
	}

	[HarmonyPatch(typeof(BionicMicrochipMonitor), nameof(BionicMicrochipMonitor.CreateMicrochip),
		typeof(BionicMicrochipMonitor.Instance))]
	internal static class BionicMicrochipCreatePatch
	{
		internal static bool Prefix()
			=> BionicMicrochipSync.ShouldCreateMicrochip(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);
	}
}
