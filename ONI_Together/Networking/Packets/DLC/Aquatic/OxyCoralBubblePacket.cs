using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.Aquatic
{
	public sealed class OxyCoralBubblePacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxCell = 4 * 1024 * 1024;
		internal const float MaxMass = 100f;
		private const int MaxWorldId = 1024;
		private const float MaxTemperature = 10000f;
		private static readonly Dictionary<(int, int), long> LastSequences = new();

		public static void ResetSessionState() => LastSequences.Clear();

		public int WorldId;
		public int SourceCell;
		public int OutputCell;
		public long Sequence;
		public float Mass;
		public float Temperature;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid OxyCoral bubble outcome");
			writer.Write(WorldId);
			writer.Write(SourceCell);
			writer.Write(OutputCell);
			writer.Write(Sequence);
			writer.Write(Mass);
			writer.Write(Temperature);
		}

		public void Deserialize(BinaryReader reader)
		{
			WorldId = reader.ReadInt32();
			SourceCell = reader.ReadInt32();
			OutputCell = reader.ReadInt32();
			Sequence = reader.ReadInt64();
			Mass = reader.ReadSingle();
			Temperature = reader.ReadSingle();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid OxyCoral bubble outcome");
		}

		public void OnDispatched()
		{
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost) ||
			    !Grid.IsValidCell(OutputCell) || BubbleManager.instance == null)
				return;
			if (!TryClaimSequence(WorldId, SourceCell, Sequence))
				return;
			Vector3 position = Grid.CellToPosCCC(OutputCell, Grid.SceneLayer.BuildingFront);
			BubbleManager.instance.SpawnBubble(
				SimHashes.Oxygen, position, Mass, Temperature, BubbleManager.Disease.None);
		}

		internal bool IsWireValid()
			=> WorldId >= 0 && WorldId <= MaxWorldId && IsCell(SourceCell) && IsCell(OutputCell) &&
			   Sequence > 0 && IsFinite(Mass) && Mass > 0f && Mass <= MaxMass &&
			   IsFinite(Temperature) && Temperature >= 0f && Temperature <= MaxTemperature;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal static bool TryClaimSequence(int worldId, int sourceCell, long sequence)
		{
			var key = (worldId, sourceCell);
			LastSequences.TryGetValue(key, out long previous);
			if (sequence <= previous)
				return false;
			LastSequences[key] = sequence;
			return true;
		}

		private static bool IsCell(int cell) => cell >= 0 && cell < MaxCell;
		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
