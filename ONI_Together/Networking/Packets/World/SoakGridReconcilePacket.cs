#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	internal sealed class SoakGridReconcileChunkPacket : IPacket, IHostOnlyPacket
	{
		internal int RunId;
		internal int SampleId;
		internal long Generation;
		internal long WorldUpdateCut;
		internal int ChunkIndex;
		internal int TotalChunks;
		internal int TotalRecords;
		internal byte[] ChunkHash = SoakHashWire.NewHash();
		internal byte[] FullGridHash = SoakHashWire.NewHash();
		internal List<SoakGridCell> Cells = new();

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			WriteMarker(writer);
			writer.Write(ChunkHash);
			writer.Write(FullGridHash);
			writer.Write(Cells.Count);
			foreach (SoakGridCell cell in Cells)
				SoakGridWire.WriteCell(writer, cell);
		}

		public void Deserialize(BinaryReader reader)
		{
			ReadMarker(reader);
			ChunkHash = SoakHashWire.ReadHash(reader);
			FullGridHash = SoakHashWire.ReadHash(reader);
			int cellCount = reader.ReadInt32();
			if (cellCount <= 0 || cellCount > SoakGridWire.MaxCellsPerChunk)
				throw new InvalidDataException("Invalid soak grid chunk cell count");
			Cells = new List<SoakGridCell>(cellCount);
			for (int i = 0; i < cellCount; i++)
				Cells.Add(SoakGridWire.ReadCell(reader));
			Validate();
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveGridReconcileChunk(this);
		}

		internal SoakGridMarker Marker
			=> new SoakGridMarker(RunId, SampleId, Generation, WorldUpdateCut);

		private void WriteMarker(BinaryWriter writer)
		{
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(Generation);
			writer.Write(WorldUpdateCut);
			writer.Write(ChunkIndex);
			writer.Write(TotalChunks);
			writer.Write(TotalRecords);
		}

		private void ReadMarker(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			Generation = reader.ReadInt64();
			WorldUpdateCut = reader.ReadInt64();
			ChunkIndex = reader.ReadInt32();
			TotalChunks = reader.ReadInt32();
			TotalRecords = reader.ReadInt32();
		}

		private void Validate()
		{
			SoakGridWire.ValidateMarker(RunId, SampleId, Generation, WorldUpdateCut);
			SoakGridWire.ValidateChunkMetadata(ChunkIndex, TotalChunks, TotalRecords, Cells?.Count ?? 0);
			SoakHashWire.ValidateState(TotalRecords, FullGridHash);
			SoakHashWire.ValidateState(Cells.Count, ChunkHash);
			if (!ChunkHash.SequenceEqual(SoakGridProof.HashCells(Cells)))
				throw new InvalidDataException("Soak grid chunk hash does not match its payload");
			int previousCell = -1;
			foreach (SoakGridCell cell in Cells)
			{
				SoakGridWire.ValidateCell(cell);
				if (cell.Cell <= previousCell)
					throw new InvalidDataException("Soak grid chunk cells are not strictly ordered");
				previousCell = cell.Cell;
			}
		}
	}

	internal sealed class SoakGridReconcileAckPacket : IPacket
	{
		internal int RunId;
		internal int SampleId;
		internal long Generation;
		internal long WorldUpdateCut;
		internal int ChunkIndex;
		internal int TotalChunks;
		internal int TotalRecords;
		internal byte[] ChunkHash = SoakHashWire.NewHash();
		internal byte[] FullGridHash = SoakHashWire.NewHash();

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(Generation);
			writer.Write(WorldUpdateCut);
			writer.Write(ChunkIndex);
			writer.Write(TotalChunks);
			writer.Write(TotalRecords);
			writer.Write(ChunkHash);
			writer.Write(FullGridHash);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			Generation = reader.ReadInt64();
			WorldUpdateCut = reader.ReadInt64();
			ChunkIndex = reader.ReadInt32();
			TotalChunks = reader.ReadInt32();
			TotalRecords = reader.ReadInt32();
			ChunkHash = SoakHashWire.ReadHash(reader);
			FullGridHash = SoakHashWire.ReadHash(reader);
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveGridReconcileAck(this, PacketHandler.CurrentContext);
		}

		private void Validate()
		{
			SoakGridWire.ValidateMarker(RunId, SampleId, Generation, WorldUpdateCut);
			SoakGridWire.ValidateChunkMetadata(ChunkIndex, TotalChunks, TotalRecords, 1);
			SoakHashWire.ValidateState(1, ChunkHash);
			SoakHashWire.ValidateState(TotalRecords, FullGridHash);
		}
	}

	internal enum SoakGridAbortReason : byte
	{
		ApplyDidNotConverge = 1,
	}

	internal sealed class SoakGridReconcileAbortPacket : IPacket
	{
		internal int RunId;
		internal int SampleId;
		internal long Generation;
		internal long WorldUpdateCut;
		internal SoakGridAbortReason Reason;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(Generation);
			writer.Write(WorldUpdateCut);
			writer.Write((byte)Reason);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			Generation = reader.ReadInt64();
			WorldUpdateCut = reader.ReadInt64();
			Reason = (SoakGridAbortReason)reader.ReadByte();
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveGridReconcileAbort(this, PacketHandler.CurrentContext);
		}

		private void Validate()
		{
			SoakGridWire.ValidateMarker(RunId, SampleId, Generation, WorldUpdateCut);
			if (Reason != SoakGridAbortReason.ApplyDidNotConverge)
				throw new InvalidDataException("Invalid soak grid reconcile abort reason");
		}
	}

	internal static class SoakGridWire
	{
		internal const int MaxCellsPerChunk = 1024;
		internal const int MaxGridRecords = 4 * 1024 * 1024;
		internal const int MaxChunks = 8192;

		internal static void ValidateMarker(
			int runId, int sampleId, long generation, long worldUpdateCut)
		{
			SoakHashWire.ValidateMarker(runId, sampleId, 0f);
			if (generation <= 0 || worldUpdateCut < 0)
				throw new InvalidDataException("Invalid soak grid generation");
		}

		internal static void ValidateChunkMetadata(
			int chunkIndex, int totalChunks, int totalRecords, int cellCount)
		{
			if (totalChunks <= 0 || totalChunks > MaxChunks
			    || chunkIndex < 0 || chunkIndex >= totalChunks
			    || totalRecords <= 0 || totalRecords > MaxGridRecords
			    || totalChunks > totalRecords
			    || totalRecords > totalChunks * MaxCellsPerChunk
			    || cellCount <= 0 || cellCount > MaxCellsPerChunk
			    || cellCount > totalRecords)
				throw new InvalidDataException("Invalid soak grid chunk metadata");
		}

		internal static void ValidateCell(SoakGridCell cell)
		{
			if (cell.Cell < 0 || cell.Cell >= MaxGridRecords
			    || !IsFinite(cell.Temperature) || !IsFinite(cell.Mass) || cell.Mass < 0f
			    || cell.DiseaseCount < 0)
				throw new InvalidDataException("Invalid soak grid cell payload");
		}

		internal static void WriteCell(BinaryWriter writer, SoakGridCell cell)
		{
			writer.Write(cell.Cell);
			writer.Write(cell.ElementIdx);
			writer.Write(cell.Temperature);
			writer.Write(cell.Mass);
			writer.Write(cell.DiseaseIdx);
			writer.Write(cell.DiseaseCount);
		}

		internal static SoakGridCell ReadCell(BinaryReader reader)
		{
			return new SoakGridCell
			{
				Cell = reader.ReadInt32(),
				ElementIdx = reader.ReadUInt16(),
				Temperature = reader.ReadSingle(),
				Mass = reader.ReadSingle(),
				DiseaseIdx = reader.ReadByte(),
				DiseaseCount = reader.ReadInt32(),
			};
		}

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
#endif
