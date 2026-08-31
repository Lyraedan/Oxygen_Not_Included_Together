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

			// Make the overlay actually stop clicks reaching the world instead of merely covering
			// it. LoadingOverlay is decorative - nothing on it blocks raycasts - so every click
			// went straight through into a world the host had not resumed, and orders placed
			// that way are lost: a joining client is still in the menu when the packet arrives.
			//
			// Blocking here rather than refusing each tool in turn. A per-tool guard has to be
			// repeated for dig, build, deconstruct, sweep, prioritise... and silently misses
			// whatever tool is added next.
			//
			// Keyboard input is handled separately: LoadingOverlay is a KModalScreen, so it eats
			// every key and answers Escape by destroying itself. ReadyScreenInputPatch swallows
			// the key instead, so Escape can no longer tear this screen down.
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

			// Deliberately does NOT rebuild when only the backing UI is gone. That was tried:
			// the idea was that a scene load destroys the LoadingOverlay and leaves the wrapper
			// alive, so Show would paint nothing. Measured over a full join, the client rebuilt
			// 0 times - the premise does not hold there - while the host rebuilt every 5s,
			// because in game ONI tears its LoadingOverlay back down as fast as we raise it.
			// Each rebuild calls LoadingOverlay.Load and puts a full-screen overlay back on
			// top, which is what forced the player to hit Escape over and over.
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

		public static void Close()
		{
			using var _ = Profiler.Scope();

			if (overlay == null)
				return;

			overlay?.Dispose();
			overlay = null;
		}


	}
}
