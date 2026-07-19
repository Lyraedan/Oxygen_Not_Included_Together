using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Frosty;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Frosty
{
	public sealed class SpaceTreeImpactPacket : IPacket, IHostOnlyPacket
	{
		private const int MaxCells = 64;
		private static readonly Dictionary<int, int> LastSequences = new();

		public static void ResetSessionState() => LastSequences.Clear();

		public int CometNetId;
		public int Sequence;
		public SimHashes Element;
		public float MassPerCell;
		public float Temperature;
		public byte DiseaseIndex;
		public int DiseaseCountPerCell;
		public int TreeImpactCell = -1;
		public int TreeTileMaxHeight;
		public List<int> Cells = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid Space Tree comet impact");
			writer.Write(CometNetId);
			writer.Write(Sequence);
			writer.Write((int)Element);
			writer.Write(MassPerCell);
			writer.Write(Temperature);
			writer.Write(DiseaseIndex);
			writer.Write(DiseaseCountPerCell);
			writer.Write(TreeImpactCell);
			writer.Write(TreeTileMaxHeight);
			writer.Write((byte)Cells.Count);
			foreach (int cell in Cells)
				writer.Write(cell);
		}

		public void Deserialize(BinaryReader reader)
		{
			CometNetId = reader.ReadInt32();
			Sequence = reader.ReadInt32();
			Element = (SimHashes)reader.ReadInt32();
			MassPerCell = reader.ReadSingle();
			Temperature = reader.ReadSingle();
			DiseaseIndex = reader.ReadByte();
			DiseaseCountPerCell = reader.ReadInt32();
			TreeImpactCell = reader.ReadInt32();
			TreeTileMaxHeight = reader.ReadInt32();
			int count = reader.ReadByte();
			if (count > MaxCells)
				throw new InvalidDataException("Too many Space Tree impact cells");
			Cells = new List<int>(count);
			for (int i = 0; i < count; i++)
				Cells.Add(reader.ReadInt32());
			if (!IsWireValid())
				throw new InvalidDataException("Invalid Space Tree comet impact");
		}

		public void OnDispatched()
		{
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost) ||
			    !TryClaimSequence(CometNetId, Sequence))
				return;
			SpaceTreeSeededCometSync.Apply(this);
		}

		internal bool IsWireValid()
		{
			if (CometNetId == 0 || Sequence <= 0 || Element == SimHashes.Vacuum ||
			    !ValidFinite(MassPerCell) || MassPerCell <= 0f || MassPerCell > 1_000_000f ||
			    !ValidFinite(Temperature) || Temperature < 0f || Temperature > 10_000f ||
			    DiseaseCountPerCell < 0 || TreeImpactCell < -1 || TreeTileMaxHeight < 0 ||
			    TreeTileMaxHeight > 64 || Cells == null || Cells.Count == 0 || Cells.Count > MaxCells)
				return false;
			foreach (int cell in Cells)
			{
				if (cell < 0)
					return false;
			}
			return TreeImpactCell == -1 || Cells.Contains(TreeImpactCell);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal static bool TryClaimSequence(int cometNetId, int candidate)
		{
			LastSequences.TryGetValue(cometNetId, out int previous);
			if (!SpaceTreeSeededCometSync.IsNewSequence(previous, candidate))
				return false;
			LastSequences[cometNetId] = candidate;
			return true;
		}

		private static bool ValidFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
