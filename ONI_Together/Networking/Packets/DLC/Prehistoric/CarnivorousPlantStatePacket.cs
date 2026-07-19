using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Prehistoric;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Prehistoric
{
	public enum CarnivorousPlantKind : byte
	{
		Flytrap = 1,
		CritterTrap = 2
	}

	public sealed class CarnivorousPlantStatePacket : IPacket, IHostOnlyPacket
	{
		private const int MaxPrefabIdLength = 256;

		public CarnivorousPlantKind Kind;
		public int PlantNetId;
		public int VictimNetId;
		public bool HasEatenCreature;
		public string LastConsumedPrefabId = string.Empty;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid carnivorous plant state");
			writer.Write((byte)Kind);
			writer.Write(PlantNetId);
			writer.Write(VictimNetId);
			writer.Write(HasEatenCreature);
			writer.Write(LastConsumedPrefabId ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			Kind = (CarnivorousPlantKind)reader.ReadByte();
			PlantNetId = reader.ReadInt32();
			VictimNetId = reader.ReadInt32();
			HasEatenCreature = reader.ReadBoolean();
			LastConsumedPrefabId = reader.ReadString();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid carnivorous plant state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				CarnivorousPlantSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if ((Kind != CarnivorousPlantKind.Flytrap && Kind != CarnivorousPlantKind.CritterTrap) ||
			    PlantNetId == 0 || LastConsumedPrefabId == null || LastConsumedPrefabId.Length > MaxPrefabIdLength)
				return false;
			return HasEatenCreature
				? VictimNetId != 0 && VictimNetId != PlantNetId && LastConsumedPrefabId.Length > 0
				: VictimNetId == 0 && LastConsumedPrefabId.Length == 0;
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
