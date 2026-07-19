#if DEBUG
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.World
{
	internal enum SoakKeyframeBatchProgressResult
	{
		Ignore,
		Invalid,
		Advanced,
		Complete,
	}

	internal sealed class SoakKeyframeBatchProgressWindow
	{
		internal int RunId;
		internal int SampleId;
		internal int ExpectedEntries;
		internal long ConnectionGeneration;
		internal SoakHashDomainKeyframeBatchPacket OutstandingBatch;
	}

	public sealed class SoakKeyframeBatchAckPacket : IPacket
	{
		public int RunId;
		public int SampleId;
		public int FirstEntryIndex;
		public int ReceivedEntries;
		public bool ApplyFinished;
		public bool ApplySucceeded;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(FirstEntryIndex);
			writer.Write(ReceivedEntries);
			writer.Write(ApplyFinished);
			writer.Write(ApplySucceeded);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			FirstEntryIndex = reader.ReadInt32();
			ReceivedEntries = reader.ReadInt32();
			ApplyFinished = reader.ReadBoolean();
			ApplySucceeded = reader.ReadBoolean();
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveKeyframeBatchProgress(
					this, PacketHandler.CurrentContext);
		}

		internal static SoakKeyframeBatchProgressResult Evaluate(
			SoakKeyframeBatchProgressWindow window,
			SoakKeyframeBatchAckPacket progress,
			long connectionGeneration)
		{
			SoakHashDomainKeyframeBatchPacket batch = window?.OutstandingBatch;
			if (batch == null || progress == null)
				return SoakKeyframeBatchProgressResult.Invalid;
			if (connectionGeneration != window.ConnectionGeneration
			    || progress.RunId != window.RunId || progress.SampleId != window.SampleId)
				return SoakKeyframeBatchProgressResult.Ignore;
			if (progress.FirstEntryIndex < batch.FirstEntryIndex)
				return SoakKeyframeBatchProgressResult.Ignore;
			if (progress.FirstEntryIndex != batch.FirstEntryIndex
			    || progress.ReceivedEntries != batch.NextEntryIndex)
				return SoakKeyframeBatchProgressResult.Invalid;

			bool streamComplete = batch.NextEntryIndex == window.ExpectedEntries;
			if (progress.ApplyFinished != streamComplete
			    || progress.ApplySucceeded && !streamComplete)
				return SoakKeyframeBatchProgressResult.Invalid;
			return streamComplete
				? SoakKeyframeBatchProgressResult.Complete
				: SoakKeyframeBatchProgressResult.Advanced;
		}

		internal bool Matches(SoakKeyframeBatchAckPacket other)
			=> other != null && RunId == other.RunId && SampleId == other.SampleId
			   && FirstEntryIndex == other.FirstEntryIndex
			   && ReceivedEntries == other.ReceivedEntries
			   && ApplyFinished == other.ApplyFinished
			   && ApplySucceeded == other.ApplySucceeded;

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			if (FirstEntryIndex < 0
			    || FirstEntryIndex >= SoakHashDomainKeyframeBeginPacket.MaxEntries
			    || ReceivedEntries <= FirstEntryIndex
			    || ReceivedEntries > SoakHashDomainKeyframeBeginPacket.MaxEntries
			    || ApplySucceeded && !ApplyFinished)
				throw new InvalidDataException("Invalid soak keyframe batch progress");
		}
	}
}
#endif
