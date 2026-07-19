#if DEBUG
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	public sealed class SoakRawFencePacket : IPacket, IHostOnlyPacket
	{
		public int RunId;
		public int SampleId;
		public int CompletedTicks;
		public long RepairSequenceCut;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(CompletedTicks);
			writer.Write(RepairSequenceCut);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			CompletedTicks = reader.ReadInt32();
			RepairSequenceCut = reader.ReadInt64();
			Validate();
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveRawFence(this);
		}

		private void Validate()
			=> SoakRawFenceWire.Validate(
				RunId, SampleId, CompletedTicks, RepairSequenceCut);
	}

	public sealed class SoakRawFenceAckPacket : IPacket
	{
		public int RunId;
		public int SampleId;
		public int CompletedTicks;
		public long RepairSequenceCut;
		public SoakHashReportPacket RawObserved = new();

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(CompletedTicks);
			writer.Write(RepairSequenceCut);
			RawObserved.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			CompletedTicks = reader.ReadInt32();
			RepairSequenceCut = reader.ReadInt64();
			RawObserved = new SoakHashReportPacket();
			RawObserved.Deserialize(reader);
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveRawFenceAck(
					this, PacketHandler.CurrentContext);
		}

		private void Validate()
		{
			SoakRawFenceWire.Validate(
				RunId, SampleId, CompletedTicks, RepairSequenceCut);
			if (RawObserved == null || RawObserved.RunId != RunId
			    || RawObserved.SampleId != SampleId
			    || RawObserved.CompletedTicks != CompletedTicks)
				throw new InvalidDataException("Raw soak fence report marker does not match");
		}
	}

	internal static class SoakRawFenceWire
	{
		internal static void Validate(
			int runId, int sampleId, int completedTicks, long repairSequenceCut)
		{
			SoakHashWire.ValidateMarker(runId, sampleId, 0f);
			if (completedTicks <= 0 || completedTicks > SoakHashWire.MaxTickCount
			    || repairSequenceCut < 0)
				throw new InvalidDataException("Invalid raw soak fence marker");
		}
	}
}
#endif
