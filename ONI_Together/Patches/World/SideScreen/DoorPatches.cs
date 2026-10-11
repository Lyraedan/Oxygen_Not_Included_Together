using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for door state synchronization
	/// </summary>

	/// <summary>
	/// Sync door state changes (Open/Close/Auto)
	/// </summary>
	///

	///DO NOT PATCH "Door" DIRECTLY !!

	//[HarmonyPatch(typeof(Door), "QueueStateChange")]
	public static class Door_QueueStateChange_Patch
	{
		public static void Postfix(Door __instance, Door.ControlState nextState)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			SideScreenSyncHelper.SyncDoorState(__instance.gameObject, nextState);
		}
		public static void ExecutePatch()
		{
			using var _ = Profiler.Scope();

			var m_TargetMethod = AccessTools.Method("Door, Assembly-CSharp:QueueStateChange");
			var m_Postfix = AccessTools.Method(typeof(Door_QueueStateChange_Patch), "Postfix");
			MultiplayerMod.Harmony.Patch(m_TargetMethod, null, new HarmonyMethod(m_Postfix));
		}
	}

	/// <summary>
	/// Prevents the door melt check from destroying doors on clients.
	/// In singleplayer, Door.Sim200ms checks if the door's cells are still solid
	/// and destroys the door if they're not (e.g., melted by extreme heat).
	/// On clients, the sim cell state is synced from the host and may arrive before
	/// the door chore completes (open/close), causing false-positive melt destruction.
	/// This patch forces do_melt_check to false on clients, preventing the issue.
	/// </summary>
	public static class Door_Sim200ms_Patch
	{
		public static void Prefix(Door __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost && MultiplayerSession.InActiveSession)
			{
				__instance.do_melt_check = false;
			}
		}

		public static void ExecutePatch()
		{
			using var _ = Profiler.Scope();

			var m_Target = AccessTools.Method("Door, Assembly-CSharp:Sim200ms");
			var m_Prefix = AccessTools.Method(typeof(Door_Sim200ms_Patch), "Prefix");
			MultiplayerMod.Harmony.Patch(m_Target, new HarmonyMethod(m_Prefix));
		}
	}

	/// <summary>
	/// Prevents Door.OnCleanUp from firing on clients as a safety net.
	/// When a door is destroyed on a client (from any cause), this prevents
	/// the ReplaceAndDisplaceElement calls in OnCleanUp that spawn ore debris.
	/// The host is authoritative for sim state changes.
	/// </summary>
	public static class Door_OnCleanUp_Patch
	{
		public static bool Prefix(object __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost && MultiplayerSession.InActiveSession)
			{
				return false;
			}
			return true;
		}

		public static void ExecutePatch()
		{
			using var _ = Profiler.Scope();

			var m_Target = AccessTools.Method("Door, Assembly-CSharp:OnCleanUp");
			var m_Prefix = AccessTools.Method(typeof(Door_OnCleanUp_Patch), "Prefix");
			MultiplayerMod.Harmony.Patch(m_Target, new HarmonyMethod(m_Prefix));
		}
	}
}
