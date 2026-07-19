using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.DebugTools;
using ONI_Together.Misc.World;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	internal enum ForegroundSequenceResult
	{
		Accepted,
		Superseded,
		Gap,
	}

	public partial class WorldUpdatePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxCompressedBytes = 16 * 1024 * 1024;
		internal const int MaxUpdates = 262144;

		public long Revision;
		public long Sequence;
		public long ForegroundCut;
		public long RepairSequence;
		public bool IsBackgroundRepair => Sequence == 0;
		public List<CellUpdate> Updates = new List<CellUpdate>();

		public struct CellUpdate
		{
			public int Cell;
			public ushort ElementIdx;
			public float Temperature, Mass;
			public byte DiseaseIdx;
			public int DiseaseCount;
			public SimMessages.ReplaceType ReplaceType;
			public bool DoVerticalSolidDisplacement;
		}

		public void Serialize(BinaryWriter w)
		{
			using var _ = Profiler.Scope();
			ValidateHeader();
			if (Updates.Count > MaxUpdates)
				throw new InvalidDataException($"World update count exceeds {MaxUpdates}");
			ValidateRepairUpdates();
			w.Write(Revision);
			w.Write(Sequence);
			w.Write(ForegroundCut);
			w.Write(RepairSequence);

			using (var ms = new MemoryStream())
			{
				using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, true))
				using (var compressedWriter = new BinaryWriter(deflate))
				{
					compressedWriter.Write(Updates.Count);
					foreach (var u in Updates)
					{
						compressedWriter.Write(u.Cell);
						compressedWriter.Write(u.ElementIdx);
						compressedWriter.Write(u.Temperature);
						compressedWriter.Write(u.Mass);
						compressedWriter.Write(u.DiseaseIdx);
						compressedWriter.Write(u.DiseaseCount);
						compressedWriter.Write((byte)u.ReplaceType);
						compressedWriter.Write(u.DoVerticalSolidDisplacement);
					}
				}

				byte[] compressedData = ms.ToArray();
				w.Write(compressedData.Length); // Write compressed length
				w.Write(compressedData);        // Write compressed payload
			}
		}

		public void Deserialize(BinaryReader r)
		{
			using var _ = Profiler.Scope();

			Revision = r.ReadInt64();
			Sequence = r.ReadInt64();
			ForegroundCut = r.ReadInt64();
			RepairSequence = r.ReadInt64();
			ValidateHeader();
			int compressedLength = r.ReadInt32();
			if (compressedLength < 0 || compressedLength > MaxCompressedBytes)
				throw new InvalidDataException($"Invalid world update payload length: {compressedLength}");

			byte[] compressedData = r.ReadBytes(compressedLength);
			if (compressedData.Length != compressedLength)
				throw new EndOfStreamException("World update payload ended before the declared length");

			using (var ms = new MemoryStream(compressedData))
			using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
			using (var reader = new BinaryReader(deflate))
			{
				int count = reader.ReadInt32();
				if (count < 0 || count > MaxUpdates)
					throw new InvalidDataException($"Invalid world update count: {count}");

				Updates = new List<CellUpdate>(count);
				for (int i = 0; i < count; i++)
				{
					Updates.Add(new CellUpdate
					{
						Cell = reader.ReadInt32(),
						ElementIdx = reader.ReadUInt16(),
						Temperature = reader.ReadSingle(),
						Mass = reader.ReadSingle(),
						DiseaseIdx = reader.ReadByte(),
						DiseaseCount = reader.ReadInt32(),
						ReplaceType = ReadReplaceType(reader),
						DoVerticalSolidDisplacement = reader.ReadBoolean()
					});
				}
			}
			ValidateRepairUpdates();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!ShouldApply(
				    MultiplayerSession.IsHost,
				    PacketHandler.CurrentContext.SenderIsHost,
				    Revision,
				    ClientSupersededRevision))
			{
				if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost
				    && IsBackgroundRepair && Revision <= ClientSupersededRevision)
					ResolveRepairSequence(RepairSequence);
				return;
			}
			if (IsBackgroundRepair)
			{
				if (ShouldDeferRepair(ForegroundCut))
				{
					if (!DeferRepair(this))
						DebugConsole.LogWarning(
							$"[WorldUpdatePacket] Deferred repair capacity rejected {RepairSequence}; awaiting replay.");
				}
				else
					ApplyRepairAndTrack();
				return;
			}
			ForegroundSequenceResult sequenceResult = AcceptForegroundSequence(Sequence);
			if (sequenceResult == ForegroundSequenceResult.Superseded)
				return;
			if (sequenceResult == ForegroundSequenceResult.Gap)
			{
				DebugConsole.LogError(
					$"[WorldUpdatePacket] Foreground sequence gap after {CurrentClientForegroundSequence}: {Sequence}; disconnecting.",
					false);
				NetworkConfig.TransportClient?.Disconnect();
				return;
			}
			ApplyUpdates(backgroundRepair: false);
			DrainReadyRepairs();
		}

		private void ApplyRepairAndTrack()
		{
			foreach (CellUpdate update in Updates)
			{
				if (Grid.IsValidCell(update.Cell)
				    && TryGetApplyValues(update, out _, out _))
					continue;
				FailRepairObservation("invalid cell update");
				return;
			}
			ApplyUpdates(backgroundRepair: true);
			if (!WorldUpdateRepairObservability.Track(this, Updates))
				FailRepairObservation("observation backlog capacity exceeded");
		}

		private void FailRepairObservation(string reason)
		{
			DebugConsole.LogError(
				$"[WorldUpdatePacket] Repair {RepairSequence} cannot be observed: {reason}; disconnecting.",
				false);
			NetworkConfig.TransportClient?.Disconnect();
		}

		private void ApplyUpdates(bool backgroundRepair)
		{
			foreach (var u in Updates)
			{
				if (!Grid.IsValidCell(u.Cell)
				    || !TryGetApplyValues(u, out float temperature, out float mass)
				    || !TryAcceptCellRevision(u.Cell, Revision, backgroundRepair))
					continue;

				SimMessages.ModifyCell(
						u.Cell, u.ElementIdx,
						temperature, mass,
						u.DiseaseIdx, u.DiseaseCount,
						u.ReplaceType,
						u.DoVerticalSolidDisplacement,
						-1
				);
			}
		}

		private static void DrainReadyRepairs()
		{
			List<WorldUpdatePacket> ready = TakeReadyRepairs();
			foreach (WorldUpdatePacket repair in ready)
			{
				if (repair.Revision > ClientSupersededRevision)
					repair.ApplyRepairAndTrack();
				else
					ResolveRepairSequence(repair.RepairSequence);
			}
		}

		private static List<WorldUpdatePacket> TakeReadyRepairs()
		{
			var ready = new List<WorldUpdatePacket>();
			lock (ClientStateLock)
			{
				var revisions = new List<long>();
				foreach (var entry in PendingRepairs)
				{
					if (entry.Value.ForegroundCut > 0 && (!_clientForegroundInitialized
					    || entry.Value.ForegroundCut > _clientForegroundSequence))
						continue;
					revisions.Add(entry.Key);
					ready.Add(entry.Value);
				}
				foreach (long revision in revisions)
				{
					_pendingRepairUpdates -= PendingRepairs[revision].Updates.Count;
					PendingRepairs.Remove(revision);
				}
			}
			return ready;
		}

		internal static bool TryGetApplyValues(CellUpdate update, out float temperature, out float mass)
		{
			temperature = update.Temperature;
			mass = update.Mass;
			return !float.IsNaN(temperature) && !float.IsInfinity(temperature)
				&& !float.IsNaN(mass) && !float.IsInfinity(mass)
				&& (update.ReplaceType == SimMessages.ReplaceType.None || mass >= 0f);
		}

		private static SimMessages.ReplaceType ReadReplaceType(BinaryReader reader)
		{
			var replaceType = (SimMessages.ReplaceType)reader.ReadByte();
			if (replaceType != SimMessages.ReplaceType.None
				&& replaceType != SimMessages.ReplaceType.Replace
				&& replaceType != SimMessages.ReplaceType.ReplaceAndDisplace)
			{
				throw new InvalidDataException($"Invalid cell replace type: {replaceType}");
			}
			return replaceType;
		}

		internal static bool ShouldApply(
			bool localIsHost, bool senderIsHost, long revision, long supersededRevision)
		{
			return !localIsHost && senderIsHost && revision > 0
			       && revision > supersededRevision;
		}

		private void ValidateHeader()
		{
			if (Revision <= 0)
				throw new InvalidDataException($"Invalid world update revision: {Revision}");
			if (Sequence < 0 || ForegroundCut < 0 || RepairSequence < 0
			    || (Sequence > 0 && (ForegroundCut != 0 || RepairSequence != 0))
			    || (Sequence == 0 && RepairSequence == 0))
				throw new InvalidDataException(
					$"Invalid world update causal metadata: sequence={Sequence}, " +
					$"cut={ForegroundCut}, repair={RepairSequence}");
		}

		private void ValidateRepairUpdates()
		{
			if (!IsBackgroundRepair)
				return;
			if (Updates.Count == 0)
				throw new InvalidDataException("Background world repair cannot be empty");
			foreach (CellUpdate update in Updates)
			{
				if (update.ReplaceType != SimMessages.ReplaceType.Replace
				    || update.DoVerticalSolidDisplacement)
					throw new InvalidDataException("Background world repair must use idempotent replacement");
			}
		}
	}
}
