using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Aquatic
{
	internal static class PunchClamSync
	{
		internal static bool NeedsOpenedApply(bool current, bool target) => current != target;

		internal static void SendTarget(PunchClamMonitor.Instance monitor)
		{
			if (!MultiplayerSession.IsHostInSession || monitor == null)
				return;

			int puncherNetId = EnsureNetId(monitor.gameObject);
			ClamHarvestable clam = monitor.Clam;
			int clamNetId = EnsureNetId(clam?.gameObject);
			if (puncherNetId == 0)
				return;

			var packet = new PunchClamStatePacket
			{
				PuncherNetId = puncherNetId,
				TargetClamNetId = clamNetId,
				HasClamState = clamNetId != 0,
				ClamNetId = clamNetId,
				HasBeenOpened = clam != null && ReadOpened(clam)
			};
			PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}

		internal static void SendClamState(ClamHarvestable clam)
		{
			if (!MultiplayerSession.IsHostInSession || clam == null)
				return;
			int clamNetId = EnsureNetId(clam.gameObject);
			if (clamNetId == 0)
				return;

			PacketSender.SendToAllClients(new PunchClamStatePacket
			{
				HasClamState = true,
				ClamNetId = clamNetId,
				HasBeenOpened = ReadOpened(clam)
			}, PacketSendMode.ReliableImmediate);
		}

		internal static bool TryApply(PunchClamStatePacket packet)
		{
			bool targetApplied = packet.PuncherNetId == 0 || ApplyTarget(packet);
			bool clamApplied = !packet.HasClamState || ApplyOpened(packet.ClamNetId, packet.HasBeenOpened);
			return targetApplied && clamApplied;
		}

		private static bool ApplyTarget(PunchClamStatePacket packet)
		{
			if (!NetworkIdentityRegistry.TryGet(packet.PuncherNetId, out NetworkIdentity identity))
				return false;
			PunchClamMonitor.Instance monitor = identity.gameObject.GetSMI<PunchClamMonitor.Instance>();
			if (monitor == null || !TryGetTarget(packet.TargetClamNetId, out GameObject target))
				return false;

			object parameter = Traverse.Create(monitor.sm).Field("clamTarget").GetValue();
			if (parameter == null)
				return false;
			Traverse.Create(parameter).Method("Set", new object[] { target, monitor }).GetValue();
			return true;
		}

		private static bool TryGetTarget(int clamNetId, out GameObject target)
		{
			target = null;
			if (clamNetId == 0)
				return true;
			if (!NetworkIdentityRegistry.TryGet(clamNetId, out NetworkIdentity identity) ||
			    identity.gameObject.GetComponent<ClamHarvestable>() == null)
				return false;
			target = identity.gameObject;
			return true;
		}

		private static bool ApplyOpened(int clamNetId, bool target)
		{
			if (!NetworkIdentityRegistry.TryGet(clamNetId, out NetworkIdentity identity))
				return false;
			ClamHarvestable clam = identity.gameObject.GetComponent<ClamHarvestable>();
			if (clam == null)
				return false;
			bool current = ReadOpened(clam);
			if (!NeedsOpenedApply(current, target))
				return true;

			Traverse.Create(clam).Field("hasBeenOpened").SetValue(target);
			Traverse.Create(clam).Method("UpdateHarvestReadyAnimations", true).GetValue();
			Traverse.Create(clam).Method("SetupWorkable").GetValue();
			return true;
		}

		private static bool ReadOpened(ClamHarvestable clam)
			=> Traverse.Create(clam).Field("hasBeenOpened").GetValue<bool>();

		private static int EnsureNetId(GameObject go)
		{
			if (go == null)
				return 0;
			NetworkIdentity identity = go.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			return identity.NetId;
		}
	}

	[HarmonyPatch(typeof(PunchClamMonitor.Instance), nameof(PunchClamMonitor.Instance.SearchForClam))]
	internal static class PunchClamSearchPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(PunchClamMonitor.Instance __instance)
			=> PunchClamSync.SendTarget(__instance);
	}

	[HarmonyPatch(typeof(PunchClamMonitor), "ClearTarget", typeof(PunchClamMonitor.Instance))]
	internal static class PunchClamClearTargetPatch
	{
		internal static void Postfix(PunchClamMonitor.Instance __0)
			=> PunchClamSync.SendTarget(__0);
	}

	[HarmonyPatch(typeof(PunchClamOpenStates.Instance), nameof(PunchClamOpenStates.Instance.OpenClam))]
	internal static class PunchClamOpenStatePatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);
	}

	[HarmonyPatch(typeof(ClamHarvestable), nameof(ClamHarvestable.PunchOpen))]
	internal static class PunchClamGameplayPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(ClamHarvestable __instance)
			=> PunchClamSync.SendClamState(__instance);
	}

	[HarmonyPatch(typeof(ClamHarvestable), "OnCompleteWork", typeof(WorkerBase))]
	internal static class PunchClamManualWorkPatch
	{
		internal static bool Prefix()
			=> AquaticSync.ShouldRunAuthoritativeGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(ClamHarvestable __instance)
			=> PunchClamSync.SendClamState(__instance);
	}

	[HarmonyPatch(typeof(ClamHarvestable), "OnHarvested", typeof(object))]
	internal static class PunchClamHarvestResetPatch
	{
		internal static void Postfix(ClamHarvestable __instance)
			=> PunchClamSync.SendClamState(__instance);
	}
}
