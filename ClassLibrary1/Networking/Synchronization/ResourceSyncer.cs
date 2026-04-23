using HarmonyLib;
using ONI_MP.Networking.Packets.World;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Networking.Synchronization
{
	public class ResourceSyncer : MonoBehaviour
	{
		public static Dictionary<string, float> ClientResources = new Dictionary<string, float>();

		private float _lastSendTime;
		private const float SYNC_INTERVAL = 3.0f; // Sync every 3 seconds, not critical

		private void Update()
		{
			// Disabled - world inventory sync not working correctly
			return;

			/*
			if (!MultiplayerSession.InSession) return;

			if (MultiplayerSession.IsHost)
			{
				HostUpdate();
			}
			*/
		}

		private void HostUpdate()
		{
			using var _ = Profiler.Scope();

			if (Time.time - _lastSendTime < SYNC_INTERVAL) return;

			var world = ClusterManager.Instance.activeWorld;
			if (world == null) return;

			// Scan discovered resources
			// Assuming we want to sync EVERYTHING discovered.
			// Access DiscoveredResources or iterate WorldInventory?
			// WorldInventory has the amounts.

			// We need a list of tags to check.
			// DiscoveredResources.Instance.GetDiscovered() returns a set of Tag.

			var discovered = DiscoveredResources.Instance;
			if (discovered == null) return;

			// Access private keys? Or iterate all known Element/Item tags?
			// Simpler: Access Assets.GetPrefabsWithTag?
			// DiscoveredResources actually holds the list of what we care about.

			var packet = new ResourceCountPacket();

			// Just scan elements for now as a test? Or try to reflect discovered list.
			// Reflection: DiscoveredResources.discoveredResources (HashSet<Tag>)

			var field = Traverse.Create(discovered).Field("discoveredResources").GetValue<HashSet<Tag>>();
			if (field != null)
			{
				foreach (var tag in field)
				{
					float amount = world.worldInventory.GetAmount(tag, false);
					if (amount > 0)
					{
						packet.Resources[tag.Name] = amount;
					}
				}
			}

			if (packet.Resources.Count > 0)
			{
				PacketSender.SendToAllClients(packet);
				// DebugConsole.Log($"[ResourceSyncer] Sent {packet.Resources.Count} resources.");
			}

			_lastSendTime = Time.time;
		}
	}

	// Client-side patch: Override WorldInventory.GetAmount to return our synced values if we are Client
	[HarmonyPatch(typeof(WorldInventory), "GetAmount")]
	public static class WorldInventoryGetAmountPatch
	{
		public static bool Prefix(Tag tag, bool includeRelatedWorlds, ref float __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost) return true;

			if (ResourceSyncer.ClientResources.TryGetValue(tag.Name, out float val))
			{
				__result = val;
				return false; // Skip original method
			}

			return true;
		}
	}
}
