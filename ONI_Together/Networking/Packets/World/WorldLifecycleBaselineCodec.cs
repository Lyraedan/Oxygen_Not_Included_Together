using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.World
{
	internal static class WorldLifecycleBaselineCodec
	{
		internal static void Write(
			BinaryWriter writer,
			IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> baseline,
			int maximumEntries)
		{
			if (writer == null || baseline == null || baseline.Count > maximumEntries)
				throw new InvalidDataException("Invalid lifecycle baseline count");
			var netIds = new HashSet<int>();
			writer.Write(baseline.Count);
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry in baseline)
			{
				if (!netIds.Add(entry.NetId) || !IsValidTransferEntry(entry))
					throw new InvalidDataException("Invalid lifecycle baseline entry");
				writer.Write(entry.NetId);
				writer.Write(entry.Revision);
				writer.Write(entry.Tombstoned);
				writer.Write(entry.Descriptor != null);
				entry.Descriptor?.Serialize(writer);
			}
		}

		internal static List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> Read(
			BinaryReader reader, int maximumEntries)
		{
			if (reader == null)
				throw new InvalidDataException("Lifecycle baseline reader is missing");
			int count = reader.ReadInt32();
			if (count < 0 || count > maximumEntries)
				throw new InvalidDataException($"Invalid lifecycle baseline count: {count}");
			var baseline = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(count);
			var netIds = new HashSet<int>();
			for (int index = 0; index < count; index++)
			{
				int netId = reader.ReadInt32();
				ulong revision = reader.ReadUInt64();
				bool tombstoned = reader.ReadBoolean();
				SpawnPrefabPacket descriptor = null;
				if (reader.ReadBoolean())
				{
					descriptor = new SpawnPrefabPacket();
					descriptor.Deserialize(reader);
				}
				var entry = new NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry(
					netId, revision, tombstoned, descriptor);
				if (!netIds.Add(netId) || !IsValidTransferEntry(entry))
					throw new InvalidDataException("Invalid lifecycle baseline entry");
				baseline.Add(entry);
			}
			return baseline;
		}

		internal static bool IsValidTransferEntry(
			NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry)
		{
			if (entry.NetId == 0 || entry.Revision == 0)
				return false;
			if (entry.Tombstoned)
				return entry.Descriptor == null;
			SpawnPrefabPacket descriptor = entry.Descriptor;
			return descriptor != null
			       && descriptor.NetId == entry.NetId
			       && descriptor.Revision == entry.Revision
			       && descriptor.Hash != 0
			       && IsFinite(descriptor.Position.x)
			       && IsFinite(descriptor.Position.y)
			       && IsFinite(descriptor.Position.z)
			       && (!descriptor.HasElementData
			           || SpawnPrefabPacket.IsValidElementState(
				           descriptor.Mass, descriptor.Temperature, descriptor.DiseaseCount));
		}

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
