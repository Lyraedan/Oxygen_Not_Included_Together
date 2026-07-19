#if DEBUG
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	internal enum SoakKeyframePageProgressResult
	{
		Ignore,
		Invalid,
		Advanced,
		Complete,
	}

	internal sealed class SoakKeyframePageProgressWindow
	{
		internal int RunId;
		internal int SampleId;
		internal int ExpectedEntries;
		internal SoakHashDomainKeyframePagePacket OutstandingPage;
	}

	public sealed class SoakKeyframePageAckPacket : IPacket
	{
		public int RunId;
		public int SampleId;
		public int EntryIndex;
		public int AcknowledgedPages;
		public int ReceivedEntries;
		public bool ApplyFinished;
		public bool ApplySucceeded;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(EntryIndex);
			writer.Write(AcknowledgedPages);
			writer.Write(ReceivedEntries);
			writer.Write(ApplyFinished);
			writer.Write(ApplySucceeded);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			EntryIndex = reader.ReadInt32();
			AcknowledgedPages = reader.ReadInt32();
			ReceivedEntries = reader.ReadInt32();
			ApplyFinished = reader.ReadBoolean();
			ApplySucceeded = reader.ReadBoolean();
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveKeyframePageProgress(
					this, PacketHandler.CurrentContext);
		}

		internal static SoakKeyframePageProgressResult Evaluate(
			SoakKeyframePageProgressWindow window,
			SoakKeyframePageAckPacket progress)
		{
			SoakHashDomainKeyframePagePacket page = window?.OutstandingPage;
			if (page == null || progress == null)
				return SoakKeyframePageProgressResult.Invalid;
			if (progress.RunId != window.RunId || progress.SampleId != window.SampleId)
				return SoakKeyframePageProgressResult.Ignore;
			if (progress.EntryIndex < page.EntryIndex
			    || progress.EntryIndex == page.EntryIndex
			    && progress.AcknowledgedPages <= page.PageIndex)
				return SoakKeyframePageProgressResult.Ignore;
			if (progress.EntryIndex != page.EntryIndex
			    || progress.AcknowledgedPages != page.PageIndex + 1)
				return SoakKeyframePageProgressResult.Invalid;

			bool entryComplete = progress.AcknowledgedPages == page.PageCount;
			int expectedReceived = entryComplete ? page.EntryIndex + 1 : page.EntryIndex;
			bool streamComplete = entryComplete
			                      && expectedReceived == window.ExpectedEntries;
			if (progress.ReceivedEntries != expectedReceived
			    || progress.ApplyFinished != streamComplete
			    || progress.ApplySucceeded && !streamComplete)
				return SoakKeyframePageProgressResult.Invalid;
			return streamComplete
				? SoakKeyframePageProgressResult.Complete
				: SoakKeyframePageProgressResult.Advanced;
		}

		internal bool Matches(SoakKeyframePageAckPacket other)
			=> other != null && RunId == other.RunId && SampleId == other.SampleId
			   && EntryIndex == other.EntryIndex
			   && AcknowledgedPages == other.AcknowledgedPages
			   && ReceivedEntries == other.ReceivedEntries
			   && ApplyFinished == other.ApplyFinished
			   && ApplySucceeded == other.ApplySucceeded;

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			if (EntryIndex < 0
			    || EntryIndex >= SoakHashDomainKeyframeBeginPacket.MaxEntries
			    || AcknowledgedPages <= 0
			    || AcknowledgedPages > SoakHashDomainKeyframePagePacket.MaxPagesPerEntry
			    || ReceivedEntries < 0
			    || ReceivedEntries > SoakHashDomainKeyframeBeginPacket.MaxEntries
			    || ApplySucceeded && !ApplyFinished)
				throw new InvalidDataException("Invalid soak keyframe page progress");
		}
	}
}
#endif
