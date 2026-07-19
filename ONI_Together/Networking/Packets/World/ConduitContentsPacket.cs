using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.World
{
	// One entry per pipe cell with non-trivially-changed contents.
	// ConduitType: 0 = gas, 1 = liquid. Solid pipes (rails) deferred — their
	// SolidConduitFlow.ConduitContents references a host-local pickupable handle
	// that does not survive serialization.
	public struct ConduitCellUpdate
	{
		public int Cell;
		public byte ConduitType;
		public int Element;        // SimHashes (int-backed enum)
		public float Mass;
		public float Temperature;
		public byte DiseaseIdx;
		public int DiseaseCount;
	}

	public class ConduitContentsPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxUpdateCount = 50;
		public const byte CONDUIT_GAS = 0;
		public const byte CONDUIT_LIQUID = 1;

		public List<ConduitCellUpdate> Updates = new List<ConduitCellUpdate>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Updates.Count);
			foreach (var u in Updates)
			{
				writer.Write(u.Cell);
				writer.Write(u.ConduitType);
				writer.Write(u.Element);
				writer.Write(u.Mass);
				writer.Write(u.Temperature);
				writer.Write(u.DiseaseIdx);
				writer.Write(u.DiseaseCount);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			if (count < 0 || count > MaxUpdateCount)
				throw new InvalidDataException($"Invalid conduit update count: {count}");
			Updates = new List<ConduitCellUpdate>(count);
			for (int i = 0; i < count; i++)
			{
				Updates.Add(new ConduitCellUpdate
				{
					Cell = reader.ReadInt32(),
					ConduitType = reader.ReadByte(),
					Element = reader.ReadInt32(),
					Mass = reader.ReadSingle(),
					Temperature = reader.ReadSingle(),
					DiseaseIdx = reader.ReadByte(),
					DiseaseCount = reader.ReadInt32(),
				});
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;

			ConduitFlowSyncer.Instance?.OnContentsReceived(this);
		}
	}
}
