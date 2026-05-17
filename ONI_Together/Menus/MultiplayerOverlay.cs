using ONI_Together.DebugTools;
using ONI_Together.Patches.LoadingOverlayPatch;
using System;
using Shared.Profiling;
using TMPro;
using UnityEngine;
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

			if (overlay == null)
			{
				overlay = new MultiplayerOverlay();
			}
			Text = text;
		}

		public static void Close()
		{
			using var _ = Profiler.Scope();

			overlay?.Dispose();
			overlay = null;
		}
	}
}
