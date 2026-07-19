using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public enum RocketSettingsTarget : byte
	{
		DestinationSelector,
		ControlStation
	}

	public enum RocketCraftPhase : byte
	{
		None,
		Grounded,
		Launching,
		InFlight,
		Landing,
	}

	public sealed class RocketSettingsPacketData
	{
		internal const int MaxCoordinate = 1024;

		public RocketSettingsTarget TargetKind;
		public int TargetNetId;
		public ulong TargetLifecycleRevision;
		public bool HasDestination;
		public int DestinationQ;
		public int DestinationR;
		public bool HasPad;
		public int PadNetId;
		public bool Repeat;
		public bool RestrictWhenGrounded;
		public bool HasCraftState;
		public int CraftLocationQ;
		public int CraftLocationR;
		public RocketCraftPhase CraftPhase;
		public bool HasCurrentPad;
		public int CurrentPadNetId;

		internal void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid rocket settings payload");

			writer.Write((byte)TargetKind);
			writer.Write(TargetNetId);
			writer.Write(TargetLifecycleRevision);
			writer.Write(HasDestination);
			writer.Write(DestinationQ);
			writer.Write(DestinationR);
			writer.Write(HasPad);
			writer.Write(PadNetId);
			writer.Write(Repeat);
			writer.Write(RestrictWhenGrounded);
			writer.Write(HasCraftState);
			writer.Write(CraftLocationQ);
			writer.Write(CraftLocationR);
			writer.Write((byte)CraftPhase);
			writer.Write(HasCurrentPad);
			writer.Write(CurrentPadNetId);
		}

		internal static RocketSettingsPacketData Deserialize(BinaryReader reader)
		{
			var data = new RocketSettingsPacketData
			{
				TargetKind = (RocketSettingsTarget)reader.ReadByte(),
				TargetNetId = reader.ReadInt32(),
				TargetLifecycleRevision = reader.ReadUInt64(),
				HasDestination = reader.ReadBoolean(),
				DestinationQ = reader.ReadInt32(),
				DestinationR = reader.ReadInt32(),
				HasPad = reader.ReadBoolean(),
				PadNetId = reader.ReadInt32(),
				Repeat = reader.ReadBoolean(),
				RestrictWhenGrounded = reader.ReadBoolean(),
				HasCraftState = reader.ReadBoolean(),
				CraftLocationQ = reader.ReadInt32(),
				CraftLocationR = reader.ReadInt32(),
				CraftPhase = (RocketCraftPhase)reader.ReadByte(),
				HasCurrentPad = reader.ReadBoolean(),
				CurrentPadNetId = reader.ReadInt32(),
			};
			if (!data.IsWireValid())
				throw new InvalidDataException("Invalid rocket settings payload");
			return data;
		}

		internal bool IsWireValid()
		{
			if (TargetNetId == 0 || TargetKind > RocketSettingsTarget.ControlStation)
				return false;
			if (TargetKind == RocketSettingsTarget.ControlStation)
				return !HasDestination && DestinationQ == 0 && DestinationR == 0
				       && !HasPad && PadNetId == 0 && !Repeat
				       && !HasCraftState && CraftLocationQ == 0 && CraftLocationR == 0
				       && CraftPhase == RocketCraftPhase.None
				       && !HasCurrentPad && CurrentPadNetId == 0;
			if (RestrictWhenGrounded)
				return false;
			if (!HasDestination && HasPad)
				return false;
			if (HasDestination && !CoordinateWithinBounds(DestinationQ, DestinationR))
				return false;
			if (HasPad && PadNetId == 0)
				return false;
			if (!HasCraftState)
				return CraftLocationQ == 0 && CraftLocationR == 0
				       && CraftPhase == RocketCraftPhase.None && !HasCurrentPad
				       && CurrentPadNetId == 0;
			if (TargetLifecycleRevision == 0
			    || !CoordinateWithinBounds(CraftLocationQ, CraftLocationR)
			    || CraftPhase < RocketCraftPhase.Grounded
			    || CraftPhase > RocketCraftPhase.Landing)
				return false;
			bool phaseHasPad = CraftPhase != RocketCraftPhase.InFlight;
			return HasCurrentPad == phaseHasPad
			       && (!HasCurrentPad && CurrentPadNetId == 0
			           || HasCurrentPad && CurrentPadNetId != 0);
		}

		internal bool IsAuthoritativeWireValid()
			=> IsWireValid() && TargetLifecycleRevision != 0
			   && (TargetKind == RocketSettingsTarget.ControlStation || HasCraftState);

		internal static bool CoordinateWithinBounds(int q, int r)
			=> q >= -MaxCoordinate && q <= MaxCoordinate &&
			   r >= -MaxCoordinate && r <= MaxCoordinate;
	}

	public sealed class RocketSettingsRequestPacket :
		IPacket, IClientRelayable, IHostAuthoritativeRelay
	{
		public RocketSettingsPacketData Data = new();
		public bool SnapshotOnly;

		public RocketSettingsRequestPacket()
		{
		}

		internal RocketSettingsRequestPacket(
			RocketSettingsPacketData data, bool snapshotOnly = false)
		{
			Data = data;
			SnapshotOnly = snapshotOnly;
		}

		public void Serialize(BinaryWriter writer)
		{
			writer.Write(SnapshotOnly);
			Data.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			SnapshotOnly = reader.ReadBoolean();
			Data = RocketSettingsPacketData.Deserialize(reader);
		}

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			if (!ShouldAccept(MultiplayerSession.IsHost, context))
				return;

			bool applied = SnapshotOnly || RocketSettingsSync.TryApplyRequestedSettings(Data);
			if (!RocketSettingsSync.TryCaptureByTarget(Data, out RocketSettingsPacketData state))
				return;
			var packet = RocketSettingsStatePacket.CreateAuthoritative(state);
			if (SnapshotOnly || !applied)
				PacketSender.SendToPlayer(context.SenderId, packet);
			else
				PacketSender.SendToAllClients(packet);
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast;
	}

	public sealed class RocketSettingsStatePacket : IPacket, IHostOnlyPacket
	{
		private static readonly Dictionary<int, ulong> LastRevisions = new();

		public RocketSettingsPacketData Data = new();
		public ulong Revision;

		public RocketSettingsStatePacket()
		{
		}

		internal RocketSettingsStatePacket(RocketSettingsPacketData data, ulong revision)
		{
			Data = data;
			Revision = revision;
		}

		internal static RocketSettingsStatePacket CreateAuthoritative(
			RocketSettingsPacketData data)
			=> new(data, NetworkIdentityRegistry.NextAuthorityRevision());

		public void Serialize(BinaryWriter writer)
		{
			if (Revision == 0 || !Data.IsAuthoritativeWireValid())
				throw new InvalidDataException("Invalid authoritative rocket snapshot");
			writer.Write(Revision);
			Data.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			Revision = reader.ReadUInt64();
			Data = RocketSettingsPacketData.Deserialize(reader);
			if (Revision == 0 || !Data.IsAuthoritativeWireValid())
				throw new InvalidDataException("Invalid authoritative rocket snapshot");
		}

		public void OnDispatched()
		{
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				return;
			ulong current = LastRevisions.TryGetValue(Data.TargetNetId, out ulong value) ? value : 0;
			if (!ShouldAcceptRevision(current, Revision))
				return;
			if (RocketSettingsSync.TryApply(Data))
			{
				LastRevisions[Data.TargetNetId] = Revision;
				RocketSettingsSync.CompleteRepair(Data.TargetNetId);
				return;
			}
			RocketSettingsSync.RequestRepair(Data);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal static bool ShouldAcceptRevision(ulong current, ulong incoming)
			=> incoming != 0 && incoming > current;

		public static void ResetSessionState() => LastRevisions.Clear();
	}
}
