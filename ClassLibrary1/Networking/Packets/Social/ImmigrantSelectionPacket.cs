using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_MP.Networking.Packets.Social
{
	public class ImmigrantSelectionPacket : IPacket
	{
		public int PrintingPodWorldIndex = 0; //defaults to world 0, can be different in spaced out, used with negative values to clear other pods
		public ImmigrantOptionEntry selectedOption;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(PrintingPodWorldIndex);
			selectedOption.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			PrintingPodWorldIndex = reader.ReadInt32();
			selectedOption = ImmigrantOptionEntry.Deserialize(reader);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return;

			DebugConsole.Log($"[ImmigrantSelectionPacket] Received selection: world index {PrintingPodWorldIndex}, IsHost: {MultiplayerSession.IsHost} with id: {selectedOption.GetId()}");

			// Handle client receiving notification from host
			if (!MultiplayerSession.IsHost)
			{
				// Host sends -2 when selection is made, or -1 for Reject All
				// EntitySpawnPacket is sent separately to handle actual spawning with correct NetId
				if (PrintingPodWorldIndex < 0)
				{
					if (PrintingPodWorldIndex == -1)
					{
						DebugConsole.Log("[ImmigrantSelectionPacket] Client: Host rejected all, closing screen and resetting Immigration");
					}
					else
					{
						DebugConsole.Log("[ImmigrantSelectionPacket] Client: Host made selection, closing screen and resetting Immigration");
					}

					ONI_MP.Patches.GamePatches.ImmigrantScreenPatch.ClearOptionsLock();

					if (ImmigrantScreen.instance != null && ImmigrantScreen.instance.gameObject.activeInHierarchy)
					{
						ImmigrantScreen.instance.Deactivate();
					}

					try
					{
						if (Immigration.Instance != null)
						{
							Immigration.Instance.EndImmigration();
						}
					}
					catch (System.Exception ex) { DebugConsole.LogError($"[ImmigrantSelectionPacket] Error ending immigration: {ex}"); }
				}
				return;
			}
			else
			{
				DebugConsole.Log("[ImmigrantSelectionPacket] Host: Processing client selection using AvailableOptions");

				// Close host's screen if open
				if (ImmigrantScreen.instance != null && ImmigrantScreen.instance.gameObject.activeInHierarchy)
				{
					ImmigrantScreen.instance.Deactivate();
				}
				// Screen is closed - spawn directly using cached options
				DebugConsole.Log("[ImmigrantSelectionPacket] Host: Screen is closed, spawning using cached options");

				// Handle Reject All from client
				if (PrintingPodWorldIndex == -1)
				{
					DebugConsole.Log("[ImmigrantSelectionPacket] Host: Client rejected all, ending immigration");

					// End immigration cycle
					if (Immigration.Instance != null)
					{
						Immigration.Instance.EndImmigration();
					}

					ONI_MP.Patches.GamePatches.ImmigrantScreenPatch.ClearOptionsLock();

					// Notify all clients to close screens
					var rejectPacket = new ImmigrantSelectionPacket { PrintingPodWorldIndex = -1 };
					PacketSender.SendToAllClients(rejectPacket);
					return;
				}

				ImmigrantOptionEntry opt = selectedOption;

				try
				{
					Telepad telepad = null;
					foreach (Telepad existing in global::Components.Telepads)
					{
						if (existing.GetMyWorldId() == PrintingPodWorldIndex)
						{
							telepad = existing;
							break;
						}
					}
					if (telepad == null)
					{
						DebugConsole.LogWarning("[ImmigrantSelectionPacket] Cannot find Telepad");
						return;
					}
					var deliverable = opt.ToGameDeliverable();
					var position = Grid.CellToPosCBC(Grid.PosToCell(telepad), Grid.SceneLayer.Move);
					if (deliverable is MinionStartingStats stats)
					{
						//telepad.OnAcceptDelivery(stats);
						//Delivery is handled via EntityDeliverPatch to send EntitySpawnPacket
						DebugConsole.Log($"[ImmigrantSelectionPacket] Spawned duplicant via Telepad: {opt.Name}");
						telepad.OnAcceptDelivery(deliverable);
					}
					else if (deliverable is CarePackageInfo pkg)
					{
						//var spawnedGO = pkg.Deliver(position);
						//Delivery is handled via EntityDeliverPatch to send EntitySpawnPacket
						DebugConsole.Log($"[ImmigrantSelectionPacket] Spawned care package via Telepad: {opt.CarePackageId} x{opt.Quantity}");
						telepad.OnAcceptDelivery(deliverable);
					}

					// Clear options and notify clients (with -2 to just close screens, EntitySpawnPacket handles spawning)
					ONI_MP.Patches.GamePatches.ImmigrantScreenPatch.ClearOptionsLock();

					// Send close screen notification
					var notifyPacket = new ImmigrantSelectionPacket { PrintingPodWorldIndex = -2 };
					PacketSender.SendToAllClients(notifyPacket);
				}
				catch (System.Exception ex)
				{
					DebugConsole.LogError($"[ImmigrantSelectionPacket] Failed to spawn: {ex}");
				}


				if (PrintingPodWorldIndex == -1) // Reject All
				{
					if (ImmigrantScreen.instance != null)
					{
						ImmigrantScreen.instance.Deactivate();
					}
					DebugConsole.Log("[ImmigrantSelectionPacket] Host rejected all");
				}
			}
		}
	}
}
