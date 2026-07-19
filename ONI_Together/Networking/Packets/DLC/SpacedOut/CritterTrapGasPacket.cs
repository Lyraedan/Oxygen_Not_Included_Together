using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class CritterTrapGasPacket : IPacket, IHostOnlyPacket
	{
		private static readonly Dictionary<int, int> LastSequences = new();

		public static void ResetSessionState() => LastSequences.Clear();

		public int PlantNetId;
		public int Sequence;
		public int Cell;
		public SimHashes Element;
		public float Mass;
		public float Temperature;
		public byte DiseaseIndex;
		public int DiseaseCount;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid critter trap gas outcome");
			writer.Write(PlantNetId);
			writer.Write(Sequence);
			writer.Write(Cell);
			writer.Write((int)Element);
			writer.Write(Mass);
			writer.Write(Temperature);
			writer.Write(DiseaseIndex);
			writer.Write(DiseaseCount);
		}

		public void Deserialize(BinaryReader reader)
		{
			PlantNetId = reader.ReadInt32();
			Sequence = reader.ReadInt32();
			Cell = reader.ReadInt32();
			Element = (SimHashes)reader.ReadInt32();
			Mass = reader.ReadSingle();
			Temperature = reader.ReadSingle();
			DiseaseIndex = reader.ReadByte();
			DiseaseCount = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid critter trap gas outcome");
		}

		public void OnDispatched()
		{
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost) ||
			    !TryClaimSequence(PlantNetId, Sequence))
				return;
			CritterTrapGasSync.Apply(this);
		}

		internal bool IsWireValid()
			=> PlantNetId != 0 && Sequence > 0 && Cell >= 0 && Element != SimHashes.Vacuum &&
			   ValidFinite(Mass) && Mass > 0f && Mass <= 1_000_000f &&
			   ValidFinite(Temperature) && Temperature >= 0f && Temperature <= 10_000f &&
			   DiseaseCount >= 0;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal static bool TryClaimSequence(int plantNetId, int candidate)
		{
			LastSequences.TryGetValue(plantNetId, out int previous);
			if (!CritterTrapGasSync.IsNewSequence(previous, candidate))
				return false;
			LastSequences[plantNetId] = candidate;
			return true;
		}

		private static bool ValidFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
