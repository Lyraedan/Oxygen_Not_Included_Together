using Database;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Social;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.GamePatches
{
	// Note: ImmigrantScreen logic is complex. This is a partial implementation.
	// We need to sync the containers (Care Packages / Duplicants)

	public static class ImmigrantScreenPatch
	{
		public static List<ImmigrantOptionEntry> AvailableOptions;

		// Flag to prevent re-syncing once options are locked for this cycle
		public static bool OptionsLocked = false;

		// Flag to skip ApplyOptionsToScreen when InitializeContainers has already created containers
		public static bool ContainersCreatedByPatch = false;

		// Clear the lock when the screen closes or duplicant is printed
		public static void ClearOptionsLock()
		{
			using var _ = Profiler.Scope();

			OptionsLocked = false;
			ContainersCreatedByPatch = false;
			AvailableOptions = null;

			// Also clear selectedDeliverables to prevent "add beyond limit" errors on reopen
			try
			{
				if (ImmigrantScreen.instance != null)
				{
					ImmigrantScreen.instance.selectedDeliverables.Clear();
				}
			}
			catch { }

			DebugConsole.Log("[ImmigrantScreen] Options lock cleared");
		}
		static IEnumerator SetMinionDelayed(CharacterContainer container, MinionStartingStats stats)
		{
			using var _ = Profiler.Scope();

			// Wait for end of frame to ensure proper initialization
			yield return SequenceUtil.WaitForNextFrame;
			container.SetMinion(stats);
			DebugConsole.Log($"[ImmigrantScreen] SetMinionDelayed: Set minion '{stats.Name}' in container");
		}
		static IEnumerator SetCarePackageInfoDelayed(CarePackageContainer carePackageContainer, CarePackageInfo pkg)
		{
			using var _ = Profiler.Scope();

			// Wait for end of frame to ensure proper initialization
			yield return SequenceUtil.WaitForNextFrame;
			carePackageContainer.info = pkg;
			var packageInstance = new CarePackageContainer.CarePackageInstanceData();
			if (carePackageContainer.animController != null)
			{
				UnityEngine.Object.Destroy(carePackageContainer.animController.gameObject);
				carePackageContainer.animController = null;
			}
			packageInstance.info = pkg;
			if (pkg.facadeID == "SELECTRANDOM")
			{
				packageInstance.facadeID = Db.GetEquippableFacades().resources.FindAll((EquippableFacadeResource match) => match.DefID == pkg.id).GetRandom().Id;
			}
			else
			{
				packageInstance.facadeID = pkg.facadeID;
			}
			carePackageContainer.carePackageInstanceData = packageInstance;
			carePackageContainer.ClearEntryIcons();
			carePackageContainer.SetAnimator();
			carePackageContainer.SetInfoText();
			DebugConsole.Log($"[ImmigrantScreen] SetCarePackageInfoDelayed: Set care package '{pkg.id}' in container");
		}

		public static void ApplyOptionsToScreen(ImmigrantScreen instance)
		{
			using var _ = Profiler.Scope();

            if (instance.Telepad == null) return;

            if (AvailableOptions == null || AvailableOptions.Count == 0 || instance == null)
			{
				DebugConsole.LogWarning($"[ImmigrantScreen] ApplyOptionsToScreen: Cannot apply - Options:{AvailableOptions?.Count ?? 0}, Screen:{(instance != null ? "valid" : "null")}");
				return;
			}

			bool canRerollCarePackages = false, canRerollMinions = false;
			foreach (var cont in instance.containers)
			{
				if(cont is CharacterContainer cc)
					if(cc.reshuffleButton.gameObject.activeSelf)
						canRerollMinions = true;
				else if(cont is CarePackageContainer cpc)
					if(cpc.reshuffleButton.gameObject.activeSelf)
						canRerollCarePackages = true;
			}
			///Clearing existing containers
			instance.containers.ForEach(delegate (ITelepadDeliverableContainer cc)
			{
				Object.Destroy(cc.GetGameObject());
			});
			instance.containers.Clear();

			DebugConsole.Log($"[ImmigrantScreen] ApplyOptionsToScreen: Applying {AvailableOptions.Count} options");

			instance.selectedDeliverables.Clear();

			foreach (var option in AvailableOptions)
			{
				DebugConsole.Log($"[ImmigrantScreen]   Type={option.EntryType}, Id={(option.GetId())}");
				var deliverable = option.ToGameDeliverable();
				if(deliverable is MinionStartingStats stats)
				{
					CharacterContainer characterContainer = Util.KInstantiateUI<CharacterContainer>(instance.containerPrefab.gameObject, instance.containerParent);
					characterContainer.SetController(instance);
					characterContainer.SetReshufflingState(canRerollMinions);

					Game.Instance.StartCoroutine(SetMinionDelayed(characterContainer, stats));
					instance.containers.Add(characterContainer);
				}
				else if(deliverable is CarePackageInfo pkg)
				{
					CarePackageContainer carePackageContainer = Util.KInstantiateUI<CarePackageContainer>(instance.carePackageContainerPrefab.gameObject, instance.containerParent);
					carePackageContainer.SetController(instance);
					carePackageContainer.SetReshufflingState(canRerollCarePackages);
					Game.Instance.StartCoroutine(SetCarePackageInfoDelayed(carePackageContainer, pkg));
					instance.containers.Add(carePackageContainer);
				}
			}
			Debug.Log("Container Count after apply: " + instance.containers.Count);
		}
	}

	[HarmonyPatch(typeof(ImmigrantScreen), nameof(ImmigrantScreen.Initialize))]
	public static class ImmigrantScreenInitializePatch
	{
		public static void Postfix(ImmigrantScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession) return;

			DebugConsole.Log("[ImmigrantScreen] Initialize postfix triggered");

			// If options are already locked but containers weren't created by us, apply them
			if (ImmigrantScreenPatch.OptionsLocked && ImmigrantScreenPatch.AvailableOptions != null && ImmigrantScreenPatch.AvailableOptions.Count > 0)
			{
				DebugConsole.Log($"[ImmigrantScreen] Options already locked, applying {ImmigrantScreenPatch.AvailableOptions.Count} cached options");
				ImmigrantScreenPatch.ApplyOptionsToScreen(__instance);
				return;
			}

			// First-opener-wins: Whoever opens first captures and broadcasts
			// Use a delayed capture because container data isn't ready yet at Initialize time
			// Use Game.Instance because ImmigrantScreen is inactive at this point
			Game.Instance.StartCoroutine(DelayedCaptureAndBroadcast(__instance));
		}

		private static System.Collections.IEnumerator DelayedCaptureAndBroadcast(ImmigrantScreen screen)
		{
			using var _ = Profiler.Scope();

			// Wait for end of frame (let containers populate their data)
			yield return null;

			// Check again if locked (in case another player's packet arrived)
			if (ImmigrantScreenPatch.OptionsLocked)
			{
				DebugConsole.Log("[ImmigrantScreen] Options locked during delay, applying cached options");
				if (ImmigrantScreenPatch.AvailableOptions != null && ImmigrantScreenPatch.AvailableOptions.Count > 0)
				{
					ImmigrantScreenPatch.ApplyOptionsToScreen(screen);
				}
				yield break;
			}

			CaptureAndBroadcastOptions(screen);
		}

		private static void CaptureAndBroadcastOptions(ImmigrantScreen __instance)
		{
			using var _ = Profiler.Scope();

            if (__instance.Telepad == null) return;

            string role = MultiplayerSession.IsHost ? "Host" : "Client";
			DebugConsole.Log($"[ImmigrantScreen] {role}: Capturing options from containers...");

			DebugConsole.Log($"[ImmigrantScreen] Found {__instance.containers.Count} containers");

			var options = new List<ImmigrantOptionEntry>();

			var containers = __instance.containers.OrderBy(c => c.GetGameObject().transform.GetSiblingIndex()).ToList();

			foreach (ITelepadDeliverableContainer container in containers)
			{
				if (container == null) continue;
				ImmigrantOptionEntry fromContainer = ImmigrantOptionEntry.INVALID;
				if (container is CharacterContainer cc)
					fromContainer = ImmigrantOptionEntry.FromGameDeliverable(cc.Stats);
				else if( container is CarePackageContainer cpc)
					fromContainer = ImmigrantOptionEntry.FromGameDeliverable(cpc.Info);

				if(fromContainer.IsValid)
					options.Add(fromContainer);
				else
					DebugConsole.Log($"[ImmigrantScreen]   Container {container.GetType().Name} has no stats or carePackageInfo");
			}

			if (options.Count > 0)
			{
				DebugConsole.Log($"[ImmigrantScreen] {role}: Broadcasting {options.Count} options (first-opener-wins)");

				int worldIndex = __instance.Telepad.GetMyWorldId();
				var blob = PrintingPodSyncer.SerializeOptionsBlob(options);

				var component = PrintingPodSyncer.Instance;
				if (component != null)
					component.RequestBroadcastOptions(blob, worldIndex);
			}
			else
			{
				DebugConsole.LogWarning($"[ImmigrantScreen] {role}: No options to broadcast!");
			}
		}
	}

	[HarmonyPatch(typeof(ImmigrantScreen), nameof(ImmigrantScreen.OnProceed))]
	public static class ImmigrantScreenProceedPatch
	{
		public static bool Prefix(ImmigrantScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession) return true;

			if (__instance.Telepad == null) return true;

			if (__instance.selectedDeliverables.Count == 0) return false;

			if (MultiplayerSession.IsClient)
			{
				ITelepadDeliverable selected = __instance.selectedDeliverables[0];
				var selectedOption = ImmigrantOptionEntry.FromGameDeliverable(selected);
				var blob = PrintingPodSyncer.SerializeSelectionBlob(selectedOption);
				int worldIndex = __instance.Telepad.GetMyWorldId();

				PrintingPodSyncer.Instance?.RequestMakeSelection(blob, worldIndex);

				ImmigrantScreenPatch.ClearOptionsLock();
				__instance.Deactivate();
				return false;
			}

			// Host: let original execute (Telepad.OnAcceptDelivery), Postfix notifies clients
			return true;
		}

		public static void Postfix()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
			{
				DebugConsole.Log("[ImmigrantScreen] Host: Selection made via screen, notifying clients to close");

				PrintingPodSyncer.Instance?.BroadcastCloseScreen(0);
				ImmigrantScreenPatch.ClearOptionsLock();
			}
		}
	}

	// Patch for Reject All button
	[HarmonyPatch(typeof(ImmigrantScreen), "OnRejectAll")]
	public static class ImmigrantScreenRejectPatch
	{
		public static bool Prefix(ImmigrantScreen __instance)
		{
			using var _ = Profiler.Scope();

            if (__instance.Telepad == null) return true;

            if (!MultiplayerSession.InActiveSession) return true;

			DebugConsole.Log("[ImmigrantScreen] Reject All clicked");

			if (MultiplayerSession.IsClient)
			{
				DebugConsole.Log("[ImmigrantScreen] Client: Sending Reject All to host");
				int worldIndex = __instance.Telepad.GetMyWorldId();

				PrintingPodSyncer.Instance?.RequestRejectAll(worldIndex);

				ImmigrantScreenPatch.ClearOptionsLock();
				if (ImmigrantScreen.instance != null)
					ImmigrantScreen.instance.Deactivate();

				return false;
			}

			// Host: let original execute (shows confirmation dialog), Postfix handles broadcast
			return true;
		}

		public static void Postfix()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession) return;

			if (MultiplayerSession.IsHost)
			{
				DebugConsole.Log("[ImmigrantScreen] Host: Reject All, notifying clients");

				PrintingPodSyncer.Instance?.BroadcastCloseScreen(-1);
				ImmigrantScreenPatch.ClearOptionsLock();
			}
		}
	}
}

