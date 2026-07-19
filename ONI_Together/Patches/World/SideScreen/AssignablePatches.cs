using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Patches.World.SideScreen
{
	[HarmonyPatch(typeof(Assignable), nameof(Assignable.OnSpawn))]
	public static class Assignable_OnSpawn_Patch
	{
		public static void Postfix(Assignable __instance)
		{
			using var _ = Profiler.Scope();
			NetworkIdentity identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
		}
	}

	[HarmonyPatch(typeof(Assignable), nameof(Assignable.Assign), typeof(IAssignableIdentity))]
	public static class Assignable_Assign_Patch
	{
		public static bool Prefix(Assignable __instance, IAssignableIdentity new_assignee)
		{
			using var _ = Profiler.Scope();
			if (ShouldRunLocally(MultiplayerSession.InSession, MultiplayerSession.IsHost,
				    AssignmentPacket.IsApplying))
				return true;
			AssignmentSync.SendRequest(__instance, new_assignee);
			return false;
		}

		internal static bool ShouldRunLocally(bool inSession, bool isHost, bool isApplying)
			=> !inSession || isHost || isApplying;
	}

	[HarmonyPatch(typeof(Assignable), nameof(Assignable.Assign), typeof(IAssignableIdentity),
		typeof(AssignableSlotInstance))]
	internal static class Assignable_Assign_SpecificSlot_Patch
	{
		internal static bool Prefix(Assignable __instance, IAssignableIdentity new_assignee,
			AssignableSlotInstance specificSlotInstance)
		{
			using var _ = Profiler.Scope();
			if (ShouldRunLocally(MultiplayerSession.InSession, MultiplayerSession.IsHost,
				    AssignmentPacket.IsApplying))
				return true;
			AssignmentSync.SendRequest(__instance, new_assignee, specificSlotInstance);
			return false;
		}

		internal static void Postfix(Assignable __instance)
		{
			using var _ = Profiler.Scope();
			AssignmentSync.Broadcast(__instance);
		}

		internal static bool ShouldRunLocally(bool inSession, bool isHost, bool isApplying)
			=> Assignable_Assign_Patch.ShouldRunLocally(inSession, isHost, isApplying);
	}

	[HarmonyPatch(typeof(Assignable), nameof(Assignable.Unassign))]
	public static class Assignable_Unassign_Patch
	{
		public static bool Prefix(Assignable __instance)
		{
			using var _ = Profiler.Scope();
			if (Assignable_Assign_Patch.ShouldRunLocally(MultiplayerSession.InSession,
				    MultiplayerSession.IsHost, AssignmentPacket.IsApplying))
				return true;
			AssignmentSync.SendRequest(__instance, null);
			return false;
		}

		public static void Postfix(Assignable __instance)
		{
			using var _ = Profiler.Scope();
			AssignmentSync.Broadcast(__instance);
		}
	}

	[HarmonyPatch(typeof(Assignable), nameof(Assignable.CanAssignTo))]
	internal static class Assignable_CanAssignTo_Patch
	{
		internal static bool Prefix(ref bool __result)
		{
			if (!ShouldTrustHostOutcome(MultiplayerSession.IsClient, AssignmentSync.IsApplyingHostOutcome))
				return true;
			__result = true;
			return false;
		}

		internal static bool ShouldTrustHostOutcome(bool isClient, bool applyingHostOutcome)
			=> isClient && applyingHostOutcome;
	}
}
