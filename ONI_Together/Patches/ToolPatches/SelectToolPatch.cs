using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.ToolPatches
{
	[HarmonyPatch(typeof(SelectTool), "Activate")]
	public static class SelectToolPatch
	{
		static void Postfix()
		{
			using var _ = Profiler.Scope();

			// Only apply if SelectTool is the currently active tool
			if (PlayerController.Instance.ActiveTool != SelectTool.Instance)
				return;
			UpdateColor();
		}

		public static void UpdateColor()
		{
			using var _ = Profiler.Scope();

			Texture2D cursor = Assets.GetTexture("cursor_arrow") as Texture2D;
			if (cursor == null)
			{
				DebugConsole.LogWarning("[ONI_Together] Default cursor_arrow texture not found.");
				return;
			}

			// Use the multiplayer session cursor color or fallback to white
			Color tint = MultiplayerSession.InActiveSession
					? CursorManager.Instance?.color ?? Color.white
					: Color.white;

			Texture2D tinted = TintTexture(cursor, tint);
			Cursor.SetCursor(tinted, Vector2.zero, CursorMode.Auto);

			if (PlayerController.Instance?.vim != null)
				PlayerController.Instance?.vim.SetCursor(tinted);
		}

		private static Texture2D TintTexture(Texture2D src, Color tint)
		{
			using var _ = Profiler.Scope();

			Texture2D tex = new Texture2D(src.width, src.height, src.format, false);
			Color[] pixels = src.GetPixels();
			for (int i = 0; i < pixels.Length; i++)
			{
				Color p = pixels[i];
				p = new Color(p.r * tint.r, p.g * tint.g, p.b * tint.b, p.a); // preserve alpha
				pixels[i] = p;
			}
			tex.SetPixels(pixels);
			tex.Apply();
			return tex;
		}
	}
}
