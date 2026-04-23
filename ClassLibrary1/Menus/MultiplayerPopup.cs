using ONI_MP.DebugTools;
using ONI_MP.Menus;
using ONI_MP.Networking;
using Shared.Profiling;
using Steamworks;
using UnityEngine;

namespace ONI_MP.Menus
{
public static class MultiplayerPopup
{
	private static GameObject currentPopup;

	public static void Show(Transform parent)
	{
		using var _ = Profiler.Scope();

		if (currentPopup != null)
		{
			DebugConsole.Log("[MultiplayerPopup] Popup already open.");
			return;
		}

		// Create base popup container
		GameObject popup = new GameObject("MP_Popup", typeof(RectTransform), typeof(CanvasGroup));
		currentPopup = popup;

		popup.transform.SetParent(parent, worldPositionStays: false);

		var rt = popup.GetComponent<RectTransform>();
		rt.sizeDelta = new Vector2(300, 300); // Increased height
		rt.anchorMin = new Vector2(0.5f, 0.5f);
		rt.anchorMax = new Vector2(0.5f, 0.5f);
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.anchoredPosition = Vector2.zero;


		var canvasGroup = popup.GetComponent<CanvasGroup>();
		canvasGroup.interactable = true;
		canvasGroup.blocksRaycasts = true;

		// Create buttons
		AddPopupButton(popup.transform, "Host Game", new Vector2(0, 100), () =>
		{
			MultiplayerSession.ShouldHostAfterLoad = true;
			//MainMenuPatch.Instance.Button_ResumeGame.SignalClick(KKeyCode.Mouse0);
			//HostLastSave();
			/*
			SteamLobby.CreateLobby(onSuccess: () =>
			{

			});
			*/
		});

		AddPopupButton(popup.transform, "Join Game", new Vector2(0, 40), () =>
		{
			//SteamFriends.ActivateGameOverlay("friends");
			SteamFriends.ActivateGameOverlayInviteDialog(MultiplayerSession.LocalUserID.AsCSteamID());
		});





		AddPopupButton(popup.transform, "Cancel", new Vector2(0, -80), () =>
		{
			Close();
		});
	}

	private static void Close()
	{
		using var _ = Profiler.Scope();

		if (currentPopup != null)
		{
			Object.Destroy(currentPopup);
			currentPopup = null;
		}
	}

	private static void AddPopupButton(Transform parent, string text, Vector2 position, System.Action onClick)
	{
		using var _ = Profiler.Scope();

		var template = UnityEngine.Object.FindFirstObjectByType<MainMenu>()?.Button_ResumeGame;
		if (template == null)
		{
			DebugConsole.LogError("Cannot find template button to clone.");
			return;
		}

		GameObject btnGO = UnityEngine.Object.Instantiate(template.gameObject, parent);
		btnGO.name = $"MP_{text.Replace(" ", "")}_Button";

		var rt = btnGO.GetComponent<RectTransform>();
		rt.anchoredPosition = position;

		var btn = btnGO.GetComponent<KButton>();

		var textComponents = btnGO.GetComponentsInChildren<LocText>(includeInactive: true);
		bool mainSet = false;
		foreach (var locText in textComponents)
		{
			if (!mainSet)
			{
				locText.text = text;
				mainSet = true;
			}
			else
			{
				locText.text = "";
			}
		}

		btn.onClick += () => onClick();
	}
}

}