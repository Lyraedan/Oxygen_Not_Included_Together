using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Aquatic;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Aquatic
{
	public sealed class PunchClamStatePacket : IPacket, IHostOnlyPacket
	{
		public int PuncherNetId;
		public int TargetClamNetId;
		public bool HasClamState;
		public int ClamNetId;
		public bool HasBeenOpened;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid punch clam state");

			writer.Write(PuncherNetId);
			writer.Write(TargetClamNetId);
			writer.Write(HasClamState);
			writer.Write(ClamNetId);
			writer.Write(HasBeenOpened);
		}

		public void Deserialize(BinaryReader reader)
		{
			PuncherNetId = reader.ReadInt32();
			TargetClamNetId = reader.ReadInt32();
			HasClamState = reader.ReadBoolean();
			ClamNetId = reader.ReadInt32();
			HasBeenOpened = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid punch clam state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				PunchClamSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (PuncherNetId == 0 && !HasClamState)
				return false;
			if (PuncherNetId == 0 && TargetClamNetId != 0)
				return false;
			return !HasClamState || ClamNetId != 0;
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
