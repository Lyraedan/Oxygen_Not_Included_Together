using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class PlantMutationStatePacket : IPacket, IHostOnlyPacket
	{
		private const int MaxMutationCount = 1;
		private const int MaxMutationIdLength = 128;

		public int PlantNetId;
		public int SpeciesHash;
		public int SubSpeciesHash;
		public bool Analyzed;
		public List<string> MutationIds = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid plant mutation state");
			writer.Write(PlantNetId);
			writer.Write(SpeciesHash);
			writer.Write(SubSpeciesHash);
			writer.Write(Analyzed);
			writer.Write((byte)MutationIds.Count);
			foreach (string id in MutationIds)
				writer.Write(id);
		}

		public void Deserialize(BinaryReader reader)
		{
			PlantNetId = reader.ReadInt32();
			SpeciesHash = reader.ReadInt32();
			SubSpeciesHash = reader.ReadInt32();
			Analyzed = reader.ReadBoolean();
			int count = reader.ReadByte();
			if (count > MaxMutationCount)
				throw new InvalidDataException("Too many plant mutations");
			MutationIds = new List<string>(count);
			for (int i = 0; i < count; i++)
				MutationIds.Add(reader.ReadString());
			if (!IsWireValid())
				throw new InvalidDataException("Invalid plant mutation state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				PlantMutationSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (PlantNetId == 0 || SpeciesHash == 0 || SubSpeciesHash == 0 ||
			    MutationIds == null || MutationIds.Count > MaxMutationCount)
				return false;
			foreach (string id in MutationIds)
			{
				if (string.IsNullOrEmpty(id) || id.Length > MaxMutationIdLength)
					return false;
			}
			return true;
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
