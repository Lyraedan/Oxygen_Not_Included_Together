using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Patches.World.SideScreen
{
	[HarmonyPatch(typeof(AssignmentGroupController), nameof(AssignmentGroupController.SetMember))]
	internal static class AssignmentGroupController_SetMember_Patch
	{
		internal static bool Prefix(
			AssignmentGroupController __instance,
			MinionAssignablesProxy minion,
			bool isAllowed)
		{
			if (ShouldRunLocally(MultiplayerSession.InSession, MultiplayerSession.IsHost,
				    AssignmentGroupMemberSync.IsApplying))
				return true;
			AssignmentGroupMemberSync.SendRequest(__instance, minion, isAllowed);
			return false;
		}

		internal static void Postfix(AssignmentGroupController __instance, MinionAssignablesProxy minion)
			=> AssignmentGroupMemberSync.Broadcast(__instance, minion);

		internal static bool ShouldRunLocally(bool inSession, bool isHost, bool isApplying)
			=> !inSession || isHost || isApplying;
	}
}
