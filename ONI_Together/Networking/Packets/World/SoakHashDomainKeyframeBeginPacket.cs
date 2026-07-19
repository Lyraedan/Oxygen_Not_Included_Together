#if DEBUG
using System.Collections.Generic;
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	public sealed class SoakHashDomainKeyframeBeginPacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxEntries = 262144;
		public int RunId;
		public int SampleId;
		public int ExpectedEntries;
		public List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
			LifecycleBaseline = new();

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(ExpectedEntries);
			writer.Write(LifecycleBaseline.Count);
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry
			         in LifecycleBaseline)
			{
				writer.Write(entry.NetId);
				writer.Write(entry.Revision);
				writer.Write(entry.Tombstoned);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			ExpectedEntries = reader.ReadInt32();
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxEntries)
				throw new InvalidDataException("Invalid soak lifecycle baseline count");
			LifecycleBaseline = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(count);
			for (int index = 0; index < count; index++)
			{
				LifecycleBaseline.Add(new NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry(
					reader.ReadInt32(), reader.ReadUInt64(), reader.ReadBoolean()));
			}
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				return;
			SoakHashDomainKeyframeTracker.Begin(new SoakHashDomainKeyframeContext
			{
				RunId = RunId,
				SampleId = SampleId,
				ExpectedEntries = ExpectedEntries,
				PagedTransport = true,
				LifecycleBaseline = LifecycleBaseline,
			});
			if (ExpectedEntries > 0)
				SoakKeyframePageReceiver.Begin(RunId, SampleId, ExpectedEntries);
			else
				SoakStateHashProbe.SendKeyframeProgress();
		}

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			if (ExpectedEntries < 0 || ExpectedEntries > MaxEntries
			    || LifecycleBaseline == null || LifecycleBaseline.Count > MaxEntries)
				throw new InvalidDataException("Invalid soak keyframe entry count");
			var netIds = new HashSet<int>();
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry
			         in LifecycleBaseline)
			{
				if (entry.NetId == 0 || entry.Revision == 0 || !netIds.Add(entry.NetId))
					throw new InvalidDataException("Invalid soak lifecycle baseline");
			}
		}
	}
}
#endif
