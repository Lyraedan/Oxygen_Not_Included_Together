using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public enum SetLockerPhase : byte
	{
		Closed,
		BeingWorked,
		Open,
		Off
	}

	public sealed class SetLockerRequestPacket : IPacket, IClientRelayable
	{
		public int TargetNetId;
		public bool ExpectedPending;
		public bool DesiredPending;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid set locker request");
			writer.Write(TargetNetId);
			writer.Write(ExpectedPending);
			writer.Write(DesiredPending);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			ExpectedPending = reader.ReadBoolean();
			DesiredPending = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid set locker request");
		}

		public void OnDispatched()
		{
			if (!ShouldAccept(MultiplayerSession.IsHost, PacketHandler.CurrentContext) ||
			    !SetLockerSync.TrySetPending(TargetNetId, ExpectedPending, DesiredPending))
				return;
			SetLockerSync.Broadcast(TargetNetId);
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 && ExpectedPending != DesiredPending;

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast;
	}

	public sealed class SetLockerStatePacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxContents = 32;
		internal const int MaxIdLength = 256;
		public int TargetNetId;
		public bool PendingRummage;
		public bool Used;
		public SetLockerPhase Phase;
		public List<string> Contents = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid set locker state");
			writer.Write(TargetNetId);
			writer.Write(PendingRummage);
			writer.Write(Used);
			writer.Write((byte)Phase);
			writer.Write(Contents.Count);
			foreach (string id in Contents)
				writer.Write(id);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			PendingRummage = reader.ReadBoolean();
			Used = reader.ReadBoolean();
			Phase = (SetLockerPhase)reader.ReadByte();
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxContents)
				throw new InvalidDataException("Invalid set locker content count");
			Contents = new List<string>(count);
			for (int i = 0; i < count; i++)
			{
				string id = reader.ReadString();
				if (!ValidId(id))
					throw new InvalidDataException("Invalid set locker content ID");
				Contents.Add(id);
			}
			if (!IsWireValid())
				throw new InvalidDataException("Invalid set locker state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				SetLockerSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (TargetNetId == 0 || Phase > SetLockerPhase.Off || Contents == null ||
			    Contents.Count > MaxContents || (Used && PendingRummage))
				return false;
			foreach (string id in Contents)
				if (!ValidId(id))
					return false;
			return true;
		}

		private static bool ValidId(string id)
			=> !string.IsNullOrEmpty(id) && id.Length <= MaxIdLength;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
