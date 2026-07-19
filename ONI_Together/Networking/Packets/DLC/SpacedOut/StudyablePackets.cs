using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class StudyableRequestPacket : IPacket, IClientRelayable
	{
		public int TargetNetId;
		public bool ExpectedMarked;
		public bool DesiredMarked;

		public void Serialize(BinaryWriter writer)
		{
			if (TargetNetId == 0 || ExpectedMarked == DesiredMarked)
				throw new InvalidDataException("Invalid studyable request");
			writer.Write(TargetNetId);
			writer.Write(ExpectedMarked);
			writer.Write(DesiredMarked);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			ExpectedMarked = reader.ReadBoolean();
			DesiredMarked = reader.ReadBoolean();
			if (TargetNetId == 0 || ExpectedMarked == DesiredMarked)
				throw new InvalidDataException("Invalid studyable request");
		}

		public void OnDispatched()
		{
			if (!ShouldAccept(MultiplayerSession.IsHost, PacketHandler.CurrentContext) ||
			    !StudyableSync.TrySetMarked(TargetNetId, ExpectedMarked, DesiredMarked))
				return;
			StudyableSync.Broadcast(TargetNetId);
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast;
	}

	public sealed class StudyableStatePacket : IPacket, IHostOnlyPacket
	{
		public int TargetNetId;
		public bool Studied;
		public bool MarkedForStudy;

		public void Serialize(BinaryWriter writer)
		{
			if (TargetNetId == 0)
				throw new InvalidDataException("Invalid studyable state");
			writer.Write(TargetNetId);
			writer.Write(Studied);
			writer.Write(MarkedForStudy);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Studied = reader.ReadBoolean();
			MarkedForStudy = reader.ReadBoolean();
			if (TargetNetId == 0)
				throw new InvalidDataException("Invalid studyable state");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost)
				StudyableSync.TryApply(this);
		}
	}
}
