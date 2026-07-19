using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Prehistoric;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Prehistoric
{
	internal static class MosquitoHungerSync
	{
		internal static bool ShouldRunSelection(bool inSession, bool isHost)
			=> !inSession || isHost;

		internal static void SendTarget(MosquitoHungerMonitor.Instance smi)
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost || smi == null)
				return;
			int mosquitoNetId = smi.gameObject.GetNetIdentity()?.NetId ?? 0;
			GameObject victim = smi.Victim;
			int victimNetId = victim?.GetNetIdentity()?.NetId ?? 0;
			if (mosquitoNetId == 0 || (victim != null && victimNetId == 0))
				return;
			PacketSender.SendToAllClients(new MosquitoTargetStatePacket
			{
				MosquitoNetId = mosquitoNetId,
				HasVictim = victim != null,
				VictimNetId = victimNetId
			}, PacketSendMode.ReliableImmediate);
		}

		internal static bool TryApply(MosquitoTargetStatePacket state)
		{
			if (state == null || !state.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(state.MosquitoNetId, out var mosquitoIdentity))
				return false;
			MosquitoHungerMonitor.Instance smi =
				mosquitoIdentity.gameObject.GetSMI<MosquitoHungerMonitor.Instance>();
			if (smi == null || !TryResolveVictim(state, out GameObject victim))
				return false;
			smi.sm.victim.Set(victim, smi);
			return true;
		}

		private static bool TryResolveVictim(
			MosquitoTargetStatePacket state,
			out GameObject victim)
		{
			victim = null;
			if (!state.HasVictim)
				return true;
			if (!NetworkIdentityRegistry.TryGet(state.VictimNetId, out var victimIdentity) ||
			    victimIdentity?.gameObject == null)
				return false;
			victim = victimIdentity.gameObject;
			return true;
		}
	}

	[HarmonyPatch(typeof(MosquitoHungerMonitor), nameof(MosquitoHungerMonitor.LookForVictim),
		typeof(MosquitoHungerMonitor.Instance))]
	internal static class MosquitoLookForVictimPatch
	{
		internal static bool Prefix()
			=> MosquitoHungerSync.ShouldRunSelection(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(MosquitoHungerMonitor.Instance smi)
			=> MosquitoHungerSync.SendTarget(smi);
	}

	[HarmonyPatch(typeof(MosquitoHungerMonitor), "ClearTarget",
		typeof(MosquitoHungerMonitor.Instance))]
	internal static class MosquitoClearTargetPatch
	{
		internal static void Postfix(MosquitoHungerMonitor.Instance smi)
			=> MosquitoHungerSync.SendTarget(smi);
	}
}
