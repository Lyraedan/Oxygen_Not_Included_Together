using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	/// <summary>
	/// Host -> clients: remove a tracked ground item by NetId.
	/// Carries the item NetId plus lifecycle revision. WorldDamageSpawnResourcePacket assigns matching NetIds
	/// via identity.OverrideNetId(NetId), so client registry lookup is reliable.
	/// Keep this packet immediate so the PR does not depend on the separate
	/// bulk-flush fix branch to dispatch small pickup bursts.
	/// </summary>
	public class GroundItemPickedUpPacket : IPacket, IHostOnlyPacket
	{
		private static readonly HashSet<int> PendingPickupNetIds = [];

		public int NetId;
		public ulong Revision;

		public static bool TryConsumePending(int netId)
		{
			using var _ = Profiler.Scope();
			return PendingPickupNetIds.Remove(netId);
		}

		public static void ClearPending()
		{
			using var _ = Profiler.Scope();
			int n = PendingPickupNetIds.Count;
			PendingPickupNetIds.Clear();
			DebugConsole.Log($"[PendingPickup] cleared count={n}");
		}

		internal static void CancelPending(int netId) => PendingPickupNetIds.Remove(netId);

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (Revision == 0)
				Revision = NetworkIdentityRegistry.EndLifecycle(NetId);
			writer.Write(NetId);
			writer.Write(Revision);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			NetId = reader.ReadInt32();
			Revision = reader.ReadUInt64();
			if (NetId == 0 || Revision == 0)
				throw new InvalidDataException("Invalid ground-item lifecycle metadata");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (NetId == 0 || Revision == 0)
				return;
			if (!NetworkIdentityRegistry.TryAcceptLifecycleRevision(NetId, Revision, tombstone: true))
				return;
			StorageItemPacket.CancelPending(NetId);
			SpawnPrefabPacket.CancelPendingBinding(NetId);

			if (!NetworkIdentityRegistry.TryGetComponent<Pickupable>(NetId, out var pickupable))
			{
				PendingPickupNetIds.Add(NetId);
				DebugConsole.LogWarning($"[GroundItemPickedUpPacket] Pickupable NetId {NetId} not yet registered; queued pending removal");
				return;
			}

			Util.KDestroyGameObject(pickupable.gameObject);
		}
	}
}
