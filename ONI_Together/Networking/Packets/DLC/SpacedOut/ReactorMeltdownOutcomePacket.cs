using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class ReactorMeltdownCometData
	{
		public int NetId;
		public Vector3 Position;
		public Vector2 Velocity;
		public float Rotation;
		public float Mass;
		public float Temperature;
		public byte DiseaseIndex;
		public int DiseaseCount;

		internal bool IsWireValid()
			=> NetId != 0 && ReactorMeltdownOutcomePacket.ValidVector(Position) &&
			   ReactorMeltdownOutcomePacket.IsFinite(Velocity.x) && Math.Abs(Velocity.x) <= 1_000f &&
			   ReactorMeltdownOutcomePacket.IsFinite(Velocity.y) && Math.Abs(Velocity.y) <= 1_000f &&
			   ReactorMeltdownOutcomePacket.IsFinite(Rotation) && Math.Abs(Rotation) <= 36_000f &&
			   ReactorMeltdownOutcomePacket.ValidMassTemperature(Mass, Temperature) && DiseaseCount >= 0;
	}

	public sealed class ReactorMeltdownCellData
	{
		public int Cell;
		public float Mass;
		public float Temperature;
		public int DiseaseCount;

		internal bool IsWireValid()
			=> Cell >= 0 && Cell <= 16_777_216 &&
			   ReactorMeltdownOutcomePacket.ValidMassTemperature(Mass, Temperature) && DiseaseCount >= 0;
	}

	public sealed class ReactorMeltdownOutcomePacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxComets = 3;
		internal const int MaxCells = 3;
		private const float MaxCoordinate = 1_000_000f;
		public int ReactorNetId;
		public int Revision;
		public float MeltdownMassRemaining;
		public float TimeSinceMeltdownEmit;
		public List<ReactorMeltdownCometData> Comets = new();
		public List<ReactorMeltdownCellData> Cells = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid reactor meltdown outcome");
			writer.Write(ReactorNetId);
			writer.Write(Revision);
			writer.Write(MeltdownMassRemaining);
			writer.Write(TimeSinceMeltdownEmit);
			writer.Write(Comets.Count);
			foreach (ReactorMeltdownCometData comet in Comets) WriteComet(writer, comet);
			writer.Write(Cells.Count);
			foreach (ReactorMeltdownCellData cell in Cells) WriteCell(writer, cell);
		}

		public void Deserialize(BinaryReader reader)
		{
			ReactorNetId = reader.ReadInt32();
			Revision = reader.ReadInt32();
			MeltdownMassRemaining = reader.ReadSingle();
			TimeSinceMeltdownEmit = reader.ReadSingle();
			int cometCount = ReadCount(reader, MaxComets, "comet");
			Comets = new List<ReactorMeltdownCometData>(cometCount);
			for (int i = 0; i < cometCount; i++) Comets.Add(ReadComet(reader));
			int cellCount = ReadCount(reader, MaxCells, "cell");
			Cells = new List<ReactorMeltdownCellData>(cellCount);
			for (int i = 0; i < cellCount; i++) Cells.Add(ReadCell(reader));
			if (!IsWireValid())
				throw new InvalidDataException("Invalid reactor meltdown outcome");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				ReactorMeltdownSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (ReactorNetId == 0 || Revision < 0 || Revision > 1_000_000 ||
			    !IsFinite(MeltdownMassRemaining) || MeltdownMassRemaining < 0f ||
			    MeltdownMassRemaining > 1_000_000_000f || !IsFinite(TimeSinceMeltdownEmit) ||
			    TimeSinceMeltdownEmit < 0f || TimeSinceMeltdownEmit > 60f || Comets == null ||
			    Comets.Count > MaxComets || Cells == null || Cells.Count > MaxCells)
				return false;
			var ids = new HashSet<int>();
			foreach (ReactorMeltdownCometData comet in Comets)
				if (comet == null || !comet.IsWireValid() || !ids.Add(comet.NetId)) return false;
			foreach (ReactorMeltdownCellData cell in Cells)
				if (cell == null || !cell.IsWireValid()) return false;
			return true;
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost) => !localIsHost && senderIsHost;
		internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
		internal static bool ValidMassTemperature(float mass, float temperature)
			=> IsFinite(mass) && mass > 0f && mass <= 1_000_000_000f && IsFinite(temperature) &&
			   temperature >= 0f && temperature <= 100_000f;
		internal static bool ValidVector(Vector3 value)
			=> IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) &&
			   Math.Abs(value.x) <= MaxCoordinate && Math.Abs(value.y) <= MaxCoordinate &&
			   Math.Abs(value.z) <= MaxCoordinate;

		private static int ReadCount(BinaryReader reader, int maximum, string label)
		{
			int count = reader.ReadInt32();
			if (count < 0 || count > maximum) throw new InvalidDataException($"Invalid meltdown {label} count");
			return count;
		}

		private static void WriteComet(BinaryWriter writer, ReactorMeltdownCometData comet)
		{
			writer.Write(comet.NetId);
			writer.Write(comet.Position.x); writer.Write(comet.Position.y); writer.Write(comet.Position.z);
			writer.Write(comet.Velocity.x); writer.Write(comet.Velocity.y); writer.Write(comet.Rotation);
			writer.Write(comet.Mass); writer.Write(comet.Temperature);
			writer.Write(comet.DiseaseIndex); writer.Write(comet.DiseaseCount);
		}

		private static ReactorMeltdownCometData ReadComet(BinaryReader reader)
			=> new()
			{
				NetId = reader.ReadInt32(),
				Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
				Velocity = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
				Rotation = reader.ReadSingle(), Mass = reader.ReadSingle(), Temperature = reader.ReadSingle(),
				DiseaseIndex = reader.ReadByte(), DiseaseCount = reader.ReadInt32()
			};

		private static void WriteCell(BinaryWriter writer, ReactorMeltdownCellData cell)
		{
			writer.Write(cell.Cell); writer.Write(cell.Mass); writer.Write(cell.Temperature); writer.Write(cell.DiseaseCount);
		}

		private static ReactorMeltdownCellData ReadCell(BinaryReader reader)
			=> new()
			{
				Cell = reader.ReadInt32(), Mass = reader.ReadSingle(), Temperature = reader.ReadSingle(),
				DiseaseCount = reader.ReadInt32()
			};
	}
}
