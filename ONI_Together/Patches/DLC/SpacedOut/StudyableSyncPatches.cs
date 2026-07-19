using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class StudyableSync
	{
		internal static bool ShouldRunGameplay(bool inSession, bool isHost) => !inSession || isHost;

		internal static bool TrySetMarked(int netId, bool expected, bool desired)
		{
			if (!NetworkIdentityRegistry.TryGetComponent(netId, out Studyable studyable) ||
			    studyable.Studied || GetMarked(studyable) != expected || expected == desired)
				return false;
			Traverse.Create(studyable).Field("markedForStudy").SetValue(desired);
			studyable.Refresh();
			return true;
		}

		internal static bool TryApply(StudyableStatePacket state)
		{
			if (state == null || !NetworkIdentityRegistry.TryGetComponent(state.TargetNetId, out Studyable target))
				return false;
			if (state.Studied)
				target.CancelChore();
			var traverse = Traverse.Create(target);
			traverse.Field("studied").SetValue(state.Studied);
			traverse.Field("markedForStudy").SetValue(state.MarkedForStudy);
			target.Refresh();
			return true;
		}

		internal static void Broadcast(Studyable target)
		{
			if (!MultiplayerSession.IsHostInSession || target == null)
				return;
			int netId = target.GetNetId();
			if (netId == 0)
				return;
			PacketSender.SendToAllClients(new StudyableStatePacket
			{
				TargetNetId = netId,
				Studied = target.Studied,
				MarkedForStudy = GetMarked(target)
			}, PacketSendMode.ReliableImmediate);
		}

		internal static void Broadcast(int netId)
		{
			if (NetworkIdentityRegistry.TryGetComponent(netId, out Studyable target))
				Broadcast(target);
		}

		internal static bool GetMarked(Studyable target)
			=> Traverse.Create(target).Field("markedForStudy").GetValue<bool>();
	}

	[HarmonyPatch(typeof(Studyable), nameof(Studyable.OnSidescreenButtonPressed))]
	internal static class StudyableButtonPatch
	{
		internal static bool Prefix(Studyable __instance)
		{
			if (!MultiplayerSession.IsClient)
				return true;
			bool marked = StudyableSync.GetMarked(__instance);
			int netId = __instance.GetNetId();
			if (netId != 0)
				PacketSender.SendToAllOtherPeers(new StudyableRequestPacket
			{
				TargetNetId = netId,
				ExpectedMarked = marked,
				DesiredMarked = !marked
				});
			return false;
		}

		internal static void Postfix(Studyable __instance) => StudyableSync.Broadcast(__instance);
	}

	[HarmonyPatch(typeof(Studyable), "OnCompleteWork", typeof(WorkerBase))]
	internal static class StudyableCompletePatch
	{
		internal static bool Prefix()
			=> StudyableSync.ShouldRunGameplay(MultiplayerSession.InSession, MultiplayerSession.IsHost);

		internal static void Postfix(Studyable __instance) => StudyableSync.Broadcast(__instance);
	}
}
