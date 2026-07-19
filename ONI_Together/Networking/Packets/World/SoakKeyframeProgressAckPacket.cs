#if DEBUG
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.World
{
	internal enum SoakKeyframeProgressResult
	{
		Ignore,
		Invalid,
		Advanced,
		Complete,
	}

	internal sealed class SoakKeyframeProgressWindow
	{
		internal int RunId;
		internal int SampleId;
		internal int ExpectedEntries;
		internal int SentEntries;
		internal int AcknowledgedEntries;
	}

	public sealed class SoakKeyframeProgressAckPacket : IPacket
	{
		internal const int WindowEntries = 128;
		public int RunId;
		public int SampleId;
		public int ReceivedEntries;
		public bool ApplyFinished;
		public bool ApplySucceeded;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(ReceivedEntries);
			writer.Write(ApplyFinished);
			writer.Write(ApplySucceeded);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			ReceivedEntries = reader.ReadInt32();
			ApplyFinished = reader.ReadBoolean();
			ApplySucceeded = reader.ReadBoolean();
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveKeyframeProgress(
					this, PacketHandler.CurrentContext);
		}

		internal static SoakKeyframeProgressResult Evaluate(
			SoakKeyframeProgressWindow window,
			SoakKeyframeProgressAckPacket progress)
		{
			if (progress.RunId != window.RunId || progress.SampleId != window.SampleId)
				return SoakKeyframeProgressResult.Ignore;
			if (progress.ReceivedEntries <= window.AcknowledgedEntries)
				return SoakKeyframeProgressResult.Ignore;
			if (progress.ReceivedEntries > window.SentEntries
			    || progress.ReceivedEntries > window.ExpectedEntries)
				return SoakKeyframeProgressResult.Invalid;
			bool final = progress.ReceivedEntries == window.ExpectedEntries;
			if (progress.ApplyFinished != final || progress.ApplySucceeded && !final
			    || !final && progress.ReceivedEntries != window.SentEntries)
				return SoakKeyframeProgressResult.Invalid;
			return final
				? SoakKeyframeProgressResult.Complete
				: SoakKeyframeProgressResult.Advanced;
		}

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			if (ReceivedEntries < 0
			    || ReceivedEntries > SoakHashDomainKeyframeBeginPacket.MaxEntries
			    || ApplySucceeded && !ApplyFinished)
				throw new InvalidDataException("Invalid soak keyframe progress");
		}
	}
}
#endif
