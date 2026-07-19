using Database;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
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
		public static bool OptionsCaptureInProgress = false;

		// Flag to skip ApplyOptionsToScreen when InitializeContainers has already created containers
		public static bool ContainersCreatedByPatch = false;

		// Clear the lock when the screen closes or duplicant is printed
		public static void ClearOptionsLock()
		{
			using var _ = Profiler.Scope();

			OptionsLocked = false;
			OptionsCaptureInProgress = false;
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

		public static int FindAvailableOptionIndex(ITelepadDeliverable selectedDeliverable)
		{
			if (!OptionsLocked || AvailableOptions == null || selectedDeliverable == null)
				return -1;

			ImmigrantOptionEntry selected = ImmigrantOptionEntry.FromGameDeliverable(selectedDeliverable);
			for (int i = 0; i < AvailableOptions.Count; i++)
			{
				if (AvailableOptions[i].ContentEquals(selected))
					return i;
			}
			return -1;
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

			if (instance == null || instance.Telepad == null) return;

			if (AvailableOptions == null || AvailableOptions.Count == 0)
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

			if (!MultiplayerSession.InSession) return;

			DebugConsole.Log("[ImmigrantScreen] Initialize postfix triggered");

			// If options are already locked but containers weren't created by us, apply them
			if (ImmigrantScreenPatch.OptionsLocked && ImmigrantScreenPatch.AvailableOptions != null && ImmigrantScreenPatch.AvailableOptions.Count > 0)
			{
				DebugConsole.Log($"[ImmigrantScreen] Options already locked, applying {ImmigrantScreenPatch.AvailableOptions.Count} cached options");
				ImmigrantScreenPatch.ApplyOptionsToScreen(__instance);
				return;
			}

			if (MultiplayerSession.IsClient)
			{
				if (__instance.Telepad != null)
				{
					PacketSender.SendToHost(new ImmigrantOptionsRequestPacket
					{
						PrintingPodWorldIndex = __instance.Telepad.GetMyWorldId()
					});
				}
				return;
			}

			ImmigrantScreenPatch.OptionsCaptureInProgress = true;
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
				ImmigrantScreenPatch.OptionsCaptureInProgress = false;
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

			if (!MultiplayerSession.IsHost || __instance.Telepad == null)
			{
				ImmigrantScreenPatch.OptionsCaptureInProgress = false;
				return;
			}

			DebugConsole.Log("[ImmigrantScreen] Host: Capturing authoritative options from containers...");

			// Get containers from ImmigrantScreen (inherited from CharacterSelectionController)

			DebugConsole.Log($"[ImmigrantScreen] Found {__instance.containers.Count} containers");

			var packet = new ImmigrantOptionsPacket();

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
					packet.Options.Add(fromContainer);
				else
					DebugConsole.Log($"[ImmigrantScreen]   Container {container.GetType().Name} has no stats or carePackageInfo");
			}

			if (packet.Options.Count > 0)
			{
				// Lock options for this cycle
				ImmigrantScreenPatch.AvailableOptions = packet.Options;
				ImmigrantScreenPatch.OptionsLocked = true;
				ImmigrantScreenPatch.OptionsCaptureInProgress = false;

				DebugConsole.Log($"[ImmigrantScreen] Host: Broadcasting {packet.Options.Count} authoritative options");
				PacketSender.SendToAllClients(packet);
			}
			else
			{
				ImmigrantScreenPatch.OptionsCaptureInProgress = false;
				DebugConsole.LogWarning("[ImmigrantScreen] Host: No options to broadcast!");
			}
		}
	}

	[HarmonyPatch(typeof(ImmigrantScreen), nameof(ImmigrantScreen.OnProceed))]
	public static class ImmigrantScreenProceedPatch
	{
		public static bool Prefix(ImmigrantScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return true;

			if (MultiplayerSession.IsHost)
			{
				// Host: Clear the lock after printing (Postfix will handle this)
				return true;
			}

            if (__instance.Telepad == null) return true;

			if (__instance.selectedDeliverables.Count == 0 || !ImmigrantScreenPatch.OptionsLocked)
				return false;

			ITelepadDeliverable selectedDeliverable = __instance.selectedDeliverables[0];
			int optionIndex = ImmigrantScreenPatch.FindAvailableOptionIndex(selectedDeliverable);
			if (optionIndex < 0)
			{
				DebugConsole.LogWarning("[ImmigrantScreen] Client selection is not in the host option set");
				return false;
			}

			var packet = new ImmigrantSelectionPacket
			{
				SelectedOptionIndex = optionIndex,
				PrintingPodWorldIndex = __instance.Telepad.GetMyWorldId()
			};
			PacketSender.SendToHost(packet);

			DebugConsole.Log($"[ImmigrantScreen] Client: Sending host option index {optionIndex}");

			// Keep the screen open until the host confirms and broadcasts the close.
			return false;
		}

		public static void Postfix()
		{
			using var _ = Profiler.Scope();

			// Host: Clear the lock after printing and notify clients
			if (MultiplayerSession.IsHost)
			{
				DebugConsole.Log("[ImmigrantScreen] Host: Selection made via screen, notifying clients to close");

				// Send -2 to close client screens
				// NOTE: For host's own selections via OnProceed, the game spawns the entity normally
				// Entity sync will be handled separately (e.g. via EntitySpawnPacket from a different hook)
				var packet = new ImmigrantSelectionPacket
				{
					PrintingPodWorldIndex = -2,
					SelectedOptionIndex = -1
				};
				PacketSender.SendToAllClients(packet);

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

            if (!MultiplayerSession.InSession) return true;

			DebugConsole.Log("[ImmigrantScreen] Reject All clicked");

			if (MultiplayerSession.IsClient)
			{
				// Client: Send reject to host
				DebugConsole.Log("[ImmigrantScreen] Client: Sending Reject All to host");
				var packet = new ImmigrantSelectionPacket
				{
					PrintingPodWorldIndex = -1,
					SelectedOptionIndex = -1
				};
				PacketSender.SendToHost(packet);

				return false; // Wait for the host acknowledgement before closing.
			}

			// Host: Let original execute, Postfix will notify clients
			return true;
		}

		public static void Postfix()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return;

			if (MultiplayerSession.IsHost)
			{
				DebugConsole.Log("[ImmigrantScreen] Host: Reject All, notifying clients");

				// Notify clients to close their screens
				var packet = new ImmigrantSelectionPacket
				{
					PrintingPodWorldIndex = -1,
					SelectedOptionIndex = -1
				};
				PacketSender.SendToAllClients(packet);

				ImmigrantScreenPatch.ClearOptionsLock();
			}
		}
	}
}
