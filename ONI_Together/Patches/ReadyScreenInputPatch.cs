using HarmonyLib;
using JetBrains.Annotations;
using ONI_Together.Menus;

namespace ONI_Together.Patches
{
	/// <summary>
	/// Keyboard behaviour of the ready screen: swallow everything. The screen is ONI's
	/// LoadingOverlay, a KModalScreen, and KModalScreen.OnKeyDown answers Escape by
	/// Deactivate() - destroying the overlay - so Escape quietly tore the ready screen down
	/// and exposed a world the gate still held shut, with no way to bring the screen back
	/// (Show deliberately does not rebuild, see MultiplayerOverlay.Show).
	///
	/// No key does anything while this screen is up - deliberately. An "Escape opens the
	/// pause menu over the ready screen" variant was built and field-tested twice: the menu
	/// ended up active, correctly ordered above the overlay and clickable, yet never visible,
	/// and blind-clicking it shut a live session down mid-join (see the fix/ready-resume-gate
	/// notes). The ready screen is a wait-for-players gate; it ends when everyone is ready or
	/// when the failure protocol (leave-while-loading, 120s load expiry) releases it.
	///
	/// Patches KModalScreen, not LoadingOverlay: LoadingOverlay does not override OnKeyDown,
	/// so naming it on the subclass gives Harmony no target and PatchAll throws, taking the
	/// whole mod down with it.
	/// </summary>
	[HarmonyPatch]
	public static class ReadyScreenInputPatch
	{
		[HarmonyPatch(typeof(KModalScreen), nameof(KModalScreen.OnKeyDown))]
		[HarmonyPrefix]
		[UsedImplicitly]
		public static bool OnKeyDown_Prefix(KModalScreen __instance, KButtonEvent e) => Swallow(__instance, e);

		[HarmonyPatch(typeof(KModalScreen), nameof(KModalScreen.OnKeyUp))]
		[HarmonyPrefix]
		[UsedImplicitly]
		public static bool OnKeyUp_Prefix(KModalScreen __instance, KButtonEvent e) => Swallow(__instance, e);

		/// <summary>Returns false - skip the original - once the key has been eaten.</summary>
		private static bool Swallow(KModalScreen screen, KButtonEvent e)
		{
			if (!(screen is LoadingOverlay) || !MultiplayerOverlay.IsOpen)
				return true;

			e.Consumed = true;
			return false;
		}
	}
}
