using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class TemporalTearSync
	{
		internal static bool TryFire(int openerNetId)
		{
			if (!NetworkIdentityRegistry.TryGet(openerNetId, out var identity))
				return false;
			TemporalTearOpener.Instance opener = identity.gameObject.GetSMI<TemporalTearOpener.Instance>();
			ClusterPOIManager manager = ClusterManager.Instance?.GetClusterPOIManager();
			if (opener == null || manager == null || manager.IsTemporalTearOpen() ||
			    !opener.SidescreenButtonInteractable())
				return false;
			Traverse.Create(opener).Method("FireTemporalTearOpener", opener).GetValue();
			return true;
		}

		internal static bool TryApply(TemporalTearStatePacket state)
		{
			ClusterPOIManager manager = ClusterManager.Instance?.GetClusterPOIManager();
			TemporalTear tear = manager?.GetTemporalTear();
			if (state?.IsWireValid() != true || tear == null ||
			    tear.Location != AxialCoordinateSync.FromQr(state.LocationQ, state.LocationR))
				return false;
			bool revealed = manager.IsTemporalTearRevealed();
			bool open = tear.IsOpen();
			if (!TemporalTearStatePacket.NeedsApply(revealed, open, state))
				return true;
			if (state.Revealed && !revealed)
				SpacedOutSyncGuard.Run(manager.RevealTemporalTear);
			if (state.Open && !open)
				SpacedOutSyncGuard.Run(tear.Open);
			return true;
		}

		internal static void Broadcast()
		{
			if (!MultiplayerSession.IsHostInSession)
				return;
			ClusterPOIManager manager = ClusterManager.Instance?.GetClusterPOIManager();
			TemporalTear tear = manager?.GetTemporalTear();
			if (tear == null)
				return;
			PacketSender.SendToAllClients(new TemporalTearStatePacket
			{
				LocationQ = tear.Location.q,
				LocationR = tear.Location.r,
				Revealed = manager.IsTemporalTearRevealed(),
				Open = tear.IsOpen()
			}, PacketSendMode.ReliableImmediate);
		}
	}

	[HarmonyPatch(typeof(TemporalTearOpener.Instance), "FireTemporalTearOpener", typeof(TemporalTearOpener.Instance))]
	internal static class TemporalTearFirePatch
	{
		internal static bool Prefix(TemporalTearOpener.Instance __instance)
		{
			if (!MultiplayerSession.IsClient)
				return true;
			int netId = __instance.gameObject.GetNetIdentity()?.NetId ?? 0;
			if (netId != 0)
				PacketSender.SendToAllOtherPeers(new TemporalTearRequestPacket { OpenerNetId = netId });
			return false;
		}
	}

	[HarmonyPatch(typeof(TemporalTearOpener.Instance), nameof(TemporalTearOpener.Instance.OpenTemporalTear))]
	internal static class TemporalTearOpenGameplayPatch
	{
		internal static bool Prefix() => !MultiplayerSession.IsClient;
	}

	[HarmonyPatch(typeof(ClusterPOIManager), nameof(ClusterPOIManager.RevealTemporalTear))]
	internal static class TemporalTearRevealStatePatch
	{
		internal static void Postfix() => TemporalTearSync.Broadcast();
	}

	[HarmonyPatch(typeof(ClusterPOIManager), nameof(ClusterPOIManager.OpenTemporalTear))]
	internal static class TemporalTearOpenStatePatch
	{
		internal static void Postfix() => TemporalTearSync.Broadcast();
	}
}
