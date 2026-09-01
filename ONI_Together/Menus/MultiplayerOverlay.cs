using ONI_Together.DebugTools;
using ONI_Together.Patches.LoadingOverlayPatch;
using System;
using Shared.Profiling;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ONI_Together.Menus
{
	class MultiplayerOverlay
	{
		public static string Text
		{
			get => overlay?.text ?? "";
			set
			{
				if (overlay == null)
					return;
				overlay.text = value;
				if (overlay.textComponent != null)
					overlay.textComponent.text = value;
			}
		}


		private LocText textComponent = null;
		private string text = "";

		private RectTransform rect = null;

		// ReSharper disable once InconsistentNaming
		private Func<float> GetScale = null;

		private static MultiplayerOverlay overlay;
		private static LoadingOverlay instance
		{
			get
			{
				return LoadingOverlayExtensions.GetSingleton();
			}
		}

		public static bool IsOpen => overlay != null;

		public MultiplayerOverlay()
		{
			using var _ = Profiler.Scope();

			SceneManager.sceneLoaded += OnPostLoadScene;
			ScreenResize.Instance.OnResize += OnResize;
			CreateOverlay();
		}

		private void CreateOverlay()
		{
			using var _ = Profiler.Scope();

			LoadingOverlay.Load(() => { });
			var inst = instance;
			if (inst == null)
			{
				DebugConsole.LogWarning("[MultiplayerOverlay] LoadingOverlayExtensions.GetSingleton() returned null.");
				return;
			}

			// LoadingOverlay is decorative - nothing on it blocks raycasts - so without this
			// clicks land in a world the host has not resumed. Blocked here rather than tool
			// by tool: a per-tool guard silently misses whatever tool is added next.
			//
			// Keys are handled separately by ReadyScreenInputPatch, which swallows them so
			// Escape can no longer answer with Deactivate() and destroy this screen.
			var canvasGroup = inst.gameObject.GetComponent<CanvasGroup>();
			if (canvasGroup == null)
				canvasGroup = inst.gameObject.AddComponent<CanvasGroup>();
			canvasGroup.blocksRaycasts = true;
			canvasGroup.interactable = true;

			var colorFill = inst.transform.Find("ColorFill")?.GetComponent<Image>();
			if (colorFill != null)
				colorFill.raycastTarget = true;
			else
				DebugConsole.LogWarning("[MultiplayerOverlay] No ColorFill to block clicks with");

			textComponent = inst.GetComponentInChildren<LocText>();
			if (textComponent == null)
			{
				DebugConsole.LogWarning("[MultiplayerOverlay] Could not find LocText in LoadingOverlay.");
				return;
			}

			textComponent.alignment = TextAlignmentOptions.Top;
			textComponent.margin = new Vector4(0, -21.0f, 0, 0);
			textComponent.text = text;

			var scaler = inst.GetComponentInParent<KCanvasScaler>();
			if (scaler == null)
			{
				DebugConsole.LogWarning("[MultiplayerOverlay] KCanvasScaler missing.");
				GetScale = () => 1.0f;
			}
			else
			{
				GetScale = scaler.GetCanvasScale;
			}

			rect = textComponent.gameObject.GetComponent<RectTransform>();
			if (rect == null)
			{
				DebugConsole.LogWarning("[MultiplayerOverlay] RectTransform missing on LocText GameObject.");
				return;
			}
			rect.sizeDelta = new Vector2(Screen.width / GetScale(), 0);
		}


		private void OnPostLoadScene(Scene scene, LoadSceneMode mode)
		{
			//SteamNetworkingComponent.scheduler.Run(CreateOverlay);
		}

		private void OnResize()
		{
		}

		private void Dispose()
		{
			using var _ = Profiler.Scope();

			SceneManager.sceneLoaded -= OnPostLoadScene;
			ScreenResize.Instance.OnResize -= OnResize;
			LoadingOverlay.Clear();
		}

		public static void Show(string text)
		{
			using var _ = Profiler.Scope();

			// Several rejection paths surface to the player only through this overlay.
			// Log transitions, not every call - the ready screen re-shows the same
			// text constantly.
			if (text != Text)
				DebugConsole.Log($"[MultiplayerOverlay] {(text ?? string.Empty).Replace("\n", " | ")}");

			// Builds only when there is no wrapper at all. Do not rebuild when the backing UI
			// was destroyed: in game ONI tears LoadingOverlay down as fast as we raise it, and
			// a rebuild loop stacks a fresh full-screen overlay on the player every few seconds.
			if (overlay == null)
			{
				overlay = new MultiplayerOverlay();

				// Nothing is usable while this screen is up - keys are swallowed
				// (ReadyScreenInputPatch) and clicks are blocked - so a pause menu that was
				// open when the screen went up would be trapped open behind it, unclosable,
				// until the gate reopens. Close it instead of stranding it.
				if (PauseScreen.Instance != null && PauseScreen.Instance.isActiveAndEnabled)
				{
					DebugConsole.Log("[MultiplayerOverlay] Closing the pause menu under the ready screen");
					PauseScreen.Instance.Show(false);
				}
			}
			Text = text;
		}

		/// <summary>
		/// The scene swap destroys the backing LoadingOverlay but not this wrapper, leaving
		/// IsOpen true over a screen that no longer exists - text writes go to a dead object
		/// and the player sees a bare world. Call once after a world load so the next Show
		/// builds a real screen again. One-shot by design: Show itself still never rebuilds
		/// (a generic rebuild-on-destroyed stacked fresh overlays on the player).
		/// </summary>
		public static void ResetAfterSceneSwap()
		{
			using var _ = Profiler.Scope();

			if (overlay == null)
				return;

			DebugConsole.Log("[MultiplayerOverlay] dropping the wrapper the scene swap orphaned");
			overlay.Dispose();
			overlay = null;
		}

		public static void Close()
		{
			using var _ = Profiler.Scope();

			if (overlay == null)
				return;

			DebugConsole.Log("[MultiplayerOverlay] closed");

			overlay?.Dispose();
			overlay = null;
		}


	}
}
