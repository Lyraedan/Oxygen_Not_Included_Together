using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Synchronization
{
	/// <summary>
	/// Host: broadcasts the resource counts of every world, see ResourceCountPacket.
	/// Client: keeps them, and WorldInventory answers from them (patches below).
	///
	/// Lives on the persistent mod object. The previous version sat on Game and
	/// was disabled with a note that it did not work: it sent the active world of
	/// the host only, keyed by tag name, and its client patch answered for whatever
	/// world it was asked about and never took the related-worlds path the build
	/// menu uses.
	/// </summary>
	public class ResourceSyncer : MonoBehaviour
	{
		private const float SEND_INTERVAL = 2f;
		private const float FULL_RESEND_INTERVAL = 30f;
		private const float CHANGE_EPSILON = 0.01f;

		public struct Amounts
		{
			public float Available;
			public float Total;
		}

		// client: worldId -> tag hash -> amounts, as last reported by the host
		private static readonly Dictionary<int, Dictionary<int, Amounts>> _hostCounts = new();

		// host: worldId -> what was last sent, so an unchanged world is skipped
		private readonly Dictionary<int, Dictionary<int, Amounts>> _lastSent = new();
		private float _nextSendTime;
		private float _nextFullResendTime;
		private int _lastPlayerCount;

		public static void ClearClientCounts()
		{
			_hostCounts.Clear();
		}

		public static bool TryGetHostCounts(int worldId, out Dictionary<int, Amounts> counts)
		{
			return _hostCounts.TryGetValue(worldId, out counts);
		}

		public static void ApplyHostCounts(int worldId, int[] tagHashes, float[] available, float[] total)
		{
			using var _ = Profiler.Scope();

			var counts = new Dictionary<int, Amounts>(tagHashes.Length);
			for (int i = 0; i < tagHashes.Length; i++)
				counts[tagHashes[i]] = new Amounts { Available = available[i], Total = total[i] };
			_hostCounts[worldId] = counts;

			DiscoverReported(counts);
		}

		/// <summary>
		/// A material the client never held is not in its discovered list, and the
		/// build menu offers discovered materials only - the copper of the host would
		/// count but not be selectable. Discover what the host reports. Elements and
		/// prefabs only: the inventory also carries category tags (Metal, Filter...)
		/// that are not resources themselves.
		/// </summary>
		private static void DiscoverReported(Dictionary<int, Amounts> counts)
		{
			var discovered = DiscoveredResources.Instance;
			if (discovered == null) return;

			foreach (var kvp in counts)
			{
				if (kvp.Value.Total <= 0f) continue;
				var tag = new Tag(kvp.Key);
				if (discovered.IsDiscovered(tag)) continue;

				var element = ElementLoader.GetElement(tag);
				if (element != null)
				{
					discovered.Discover(tag, element.materialCategory);
					continue;
				}

				var prefab = Assets.TryGetPrefab(tag);
				if (prefab != null && prefab.TryGetComponent<KPrefabID>(out var prefabId))
					discovered.Discover(tag, DiscoveredResources.GetCategoryForEntity(prefabId));
			}
		}

		private void Update()
		{
			if (!MultiplayerSession.IsHost || !MultiplayerSession.InActiveSession) return;
			if (!Utils.IsInGame() || ClusterManager.Instance == null) return;
			if (GameServerHardSync.IsHardSyncInProgress) return;
			if (Time.unscaledTime < _nextSendTime) return;
			_nextSendTime = Time.unscaledTime + SEND_INTERVAL;

			int players = MultiplayerSession.ConnectedPlayers.Count;
			if (players == 0)
			{
				_lastPlayerCount = 0;
				return;
			}

			// A newcomer needs everything, and so does everyone now and then in case
			// a packet went missing.
			if (players != _lastPlayerCount || Time.unscaledTime >= _nextFullResendTime)
			{
				_lastSent.Clear();
				_nextFullResendTime = Time.unscaledTime + FULL_RESEND_INTERVAL;
				_lastPlayerCount = players;
			}

			using var _ = Profiler.Scope();
			foreach (var world in ClusterManager.Instance.WorldContainers)
			{
				if (world == null) continue;
				var inventory = world.worldInventory;
				if (inventory == null || !inventory.HasValidCount) continue;
				SendWorld(inventory);
			}
		}

		private void SendWorld(WorldInventory inventory)
		{
			int worldId = inventory.worldId;
			var accessible = inventory.GetAccessibleAmounts();
			if (!_lastSent.TryGetValue(worldId, out var last))
			{
				last = new Dictionary<int, Amounts>();
				_lastSent[worldId] = last;
			}

			var hashes = new List<int>(accessible.Count);
			var available = new List<float>(accessible.Count);
			var total = new List<float>(accessible.Count);
			bool changed = last.Count != accessible.Count;

			foreach (var kvp in accessible)
			{
				var tag = kvp.Key;
				int hash = tag.GetHashCode();
				float availableNow = inventory.GetAmount(tag, false);
				float totalNow = inventory.GetTotalAmount(tag, false);
				hashes.Add(hash);
				available.Add(availableNow);
				total.Add(totalNow);

				if (!changed &&
					(!last.TryGetValue(hash, out var previous) ||
					 Mathf.Abs(previous.Available - availableNow) > CHANGE_EPSILON ||
					 Mathf.Abs(previous.Total - totalNow) > CHANGE_EPSILON))
					changed = true;
			}

			if (!changed) return;

			last.Clear();
			for (int i = 0; i < hashes.Count; i++)
				last[hashes[i]] = new Amounts { Available = available[i], Total = total[i] };

			PacketSender.SendToAllClients(new ResourceCountPacket
			{
				WorldId = worldId,
				TagHashes = hashes.ToArray(),
				Available = available.ToArray(),
				Total = total.ToArray(),
			}, PacketSendMode.Reliable);
		}
	}

	/// <summary>
	/// The build menu, the pinned resources panel, the resource screen and the
	/// fetch status items all ask WorldInventory; on a client they now get the
	/// numbers of the host for that world. The related-worlds form is left to the
	/// game: it sums GetAmount(tag, false) over the related worlds, and each of
	/// those lands back here. A world the host has not reported yet falls through
	/// to the local count.
	/// </summary>
	[HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetAmount))]
	public static class WorldInventoryGetAmountPatch
	{
		public static bool Prefix(WorldInventory __instance, Tag tag, bool includeRelatedWorlds, ref float __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsHost) return true;
			if (includeRelatedWorlds) return true;
			if (!ResourceSyncer.TryGetHostCounts(__instance.worldId, out var counts)) return true;

			__result = counts.TryGetValue(tag.GetHashCode(), out var amounts) ? amounts.Available : 0f;
			return false;
		}
	}

	[HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetTotalAmount))]
	public static class WorldInventoryGetTotalAmountPatch
	{
		public static bool Prefix(WorldInventory __instance, Tag tag, ref float __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsHost) return true;
			if (!ResourceSyncer.TryGetHostCounts(__instance.worldId, out var counts)) return true;

			__result = counts.TryGetValue(tag.GetHashCode(), out var amounts) ? amounts.Total : 0f;
			return false;
		}
	}

	[HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetAccessibleAmounts))]
	public static class WorldInventoryGetAccessibleAmountsPatch
	{
		public static void Postfix(WorldInventory __instance, ref Dictionary<Tag, float> __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession || MultiplayerSession.IsHost) return;
			if (!ResourceSyncer.TryGetHostCounts(__instance.worldId, out var counts)) return;

			var reported = new Dictionary<Tag, float>(counts.Count);
			foreach (var kvp in counts)
				reported[new Tag(kvp.Key)] = kvp.Value.Total;
			__result = reported;
		}
	}
}
