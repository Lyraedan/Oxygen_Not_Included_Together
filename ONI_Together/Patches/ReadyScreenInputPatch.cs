using HarmonyLib;
using JetBrains.Annotations;
using ONI_Together.Menus;

namespace ONI_Together.Patches
{
	/// <summary>
	/// Keyboard behaviour of the ready screen: swallow everything. The screen is ONI's
	/// LoadingOverlay, a KModalScreen, and KModalScreen.OnKeyDown answers Escape with
	/// Deactivate() - destroying the overlay with no way to bring it back (Show deliberately
	/// does not rebuild, see MultiplayerOverlay.Show).
	///
	/// No key does anything while this screen is up - deliberately. Do not revive the
	/// "Escape opens the pause menu over the ready screen" variant: field-tested twice, the
	/// menu ends up active and clickable yet never visible, and blind clicks can end the
	/// session mid-join. The screen ends when everyone is ready or when the failure protocol
	/// (leave-while-loading, 120s load expiry) releases it.
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
