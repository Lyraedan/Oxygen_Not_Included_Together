using System;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Prehistoric;

namespace ONI_Together.Patches.DLC.Prehistoric
{
	internal static class FossilMarkerSync
	{
		internal static bool IsApplying { get; private set; }

		public static void ResetSessionState() => IsApplying = false;

		internal static bool ShouldRunAuthoritativeGameplay(bool inSession, bool isHost)
			=> !inSession || isHost;

		internal static bool ShouldRunMarkerAction(bool inSession, bool isHost, bool isApplying)
			=> !inSession || isHost || isApplying;

		internal static bool NeedsApply(bool current, bool target) => current != target;

		internal static bool TryCapture(FossilBits fossil, out FossilMarkerPacketData data)
		{
			data = null;
			int netId = fossil?.GetNetIdentity()?.NetId ?? 0;
			if (netId == 0)
				return false;

			data = new FossilMarkerPacketData
			{
				TargetKind = FossilMarkerTarget.FossilBits,
				TargetNetId = netId,
				MarkedForDig = fossil.MarkedForDig
			};
			return true;
		}

		internal static bool TryCapture(
			MinorFossilDigSite.Instance fossil,
			out FossilMarkerPacketData data)
		{
			data = null;
			int netId = fossil?.master?.gameObject?.GetNetIdentity()?.NetId ?? 0;
			if (netId == 0)
				return false;

			data = new FossilMarkerPacketData
			{
				TargetKind = FossilMarkerTarget.MinorFossilDigSite,
				TargetNetId = netId,
				MarkedForDig = fossil.sm.MarkedForDig.Get(fossil)
			};
			return true;
		}

		internal static bool TryCapture(
			MajorFossilDigSite.Instance fossil,
			out FossilMarkerPacketData data)
		{
			data = null;
			int netId = fossil?.master?.gameObject?.GetNetIdentity()?.NetId ?? 0;
			if (netId == 0)
				return false;

			data = new FossilMarkerPacketData
			{
				TargetKind = FossilMarkerTarget.MajorFossilDigSite,
				TargetNetId = netId,
				MarkedForDig = fossil.sm.MarkedForDig.Get(fossil)
			};
			return true;
		}

		internal static bool TryApply(FossilMarkerPacketData data)
		{
			if (data == null || !data.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGet(data.TargetNetId, out var identity))
				return false;

			if (data.TargetKind == FossilMarkerTarget.FossilBits)
			{
				FossilBits fossil = identity.GetComponent<FossilBits>();
				if (fossil == null)
					return false;
				if (NeedsApply(fossil.MarkedForDig, data.MarkedForDig))
					RunApplying(() => fossil.OnSidescreenButtonPressed());
				return fossil.MarkedForDig == data.MarkedForDig;
			}

			if (data.TargetKind == FossilMarkerTarget.MinorFossilDigSite)
			{
				MinorFossilDigSite.Instance minor = identity.gameObject.GetSMI<MinorFossilDigSite.Instance>();
				if (minor == null)
					return false;
				if (NeedsApply(minor.sm.MarkedForDig.Get(minor), data.MarkedForDig))
					RunApplying(() => minor.OnExcavateButtonPressed());
				return minor.sm.MarkedForDig.Get(minor) == data.MarkedForDig;
			}

			MajorFossilDigSite.Instance major = identity.gameObject.GetSMI<MajorFossilDigSite.Instance>();
			if (major == null)
				return false;
			if (NeedsApply(major.sm.MarkedForDig.Get(major), data.MarkedForDig))
				RunApplying(() => major.OnExcavateButtonPressed());
			return major.sm.MarkedForDig.Get(major) == data.MarkedForDig;
		}

		internal static void SendRequest(FossilMarkerPacketData data)
		{
			data.MarkedForDig = !data.MarkedForDig;
			PacketSender.SendToAllOtherPeers(new FossilMarkerRequestPacket(data));
		}

		internal static void SendState(FossilMarkerPacketData data)
		{
			if (!IsApplying && MultiplayerSession.IsHostInSession)
				PacketSender.SendToAllClients(new FossilMarkerStatePacket(data));
		}

		private static void RunApplying(System.Action action)
		{
			bool previous = IsApplying;
			IsApplying = true;
			try
			{
				action();
			}
			finally
			{
				IsApplying = previous;
			}
		}
	}

	[HarmonyPatch(typeof(FossilBits), nameof(FossilBits.OnSidescreenButtonPressed), new Type[0])]
	internal static class FossilBitsMarkerPatch
	{
		internal static bool Prefix(FossilBits __instance)
		{
			if (FossilMarkerSync.ShouldRunMarkerAction(
				    MultiplayerSession.InSession, MultiplayerSession.IsHost, FossilMarkerSync.IsApplying))
				return true;
			if (FossilMarkerSync.TryCapture(__instance, out FossilMarkerPacketData data))
				FossilMarkerSync.SendRequest(data);
			return false;
		}

		internal static void Postfix(FossilBits __instance)
		{
			if (FossilMarkerSync.TryCapture(__instance, out FossilMarkerPacketData data))
				FossilMarkerSync.SendState(data);
		}
	}

	[HarmonyPatch(typeof(MinorFossilDigSite.Instance), "OnExcavateButtonPressed", new Type[0])]
	internal static class MinorFossilMarkerPatch
	{
		internal static bool Prefix(MinorFossilDigSite.Instance __instance)
		{
			if (FossilMarkerSync.ShouldRunMarkerAction(
				    MultiplayerSession.InSession, MultiplayerSession.IsHost, FossilMarkerSync.IsApplying))
				return true;
			if (FossilMarkerSync.TryCapture(__instance, out FossilMarkerPacketData data))
				FossilMarkerSync.SendRequest(data);
			return false;
		}

		internal static void Postfix(MinorFossilDigSite.Instance __instance)
		{
			if (FossilMarkerSync.TryCapture(__instance, out FossilMarkerPacketData data))
				FossilMarkerSync.SendState(data);
		}
	}

	[HarmonyPatch(typeof(MajorFossilDigSite.Instance), "OnExcavateButtonPressed", new Type[0])]
	internal static class MajorFossilMarkerPatch
	{
		internal static bool Prefix(MajorFossilDigSite.Instance __instance)
		{
			if (FossilMarkerSync.ShouldRunMarkerAction(
				    MultiplayerSession.InSession, MultiplayerSession.IsHost, FossilMarkerSync.IsApplying))
				return true;
			if (FossilMarkerSync.TryCapture(__instance, out FossilMarkerPacketData data))
				FossilMarkerSync.SendRequest(data);
			return false;
		}

		internal static void Postfix(MajorFossilDigSite.Instance __instance)
		{
			if (FossilMarkerSync.TryCapture(__instance, out FossilMarkerPacketData data))
				FossilMarkerSync.SendState(data);
		}
	}

	[HarmonyPatch(typeof(FossilBits), "DropLoot", new Type[0])]
	internal static class FossilBitsDropLootPatch
	{
		internal static bool Prefix()
			=> FossilMarkerSync.ShouldRunAuthoritativeGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);
	}

	[HarmonyPatch(
		typeof(MinorFossilDigSite),
		"DropLoot",
		new[] { typeof(MinorFossilDigSite.Instance) })]
	internal static class MinorFossilDropLootPatch
	{
		internal static bool Prefix()
			=> FossilMarkerSync.ShouldRunAuthoritativeGameplay(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);
	}
}
