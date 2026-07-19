using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public enum CryoTankPhase : byte
	{
		Closed,
		Open,
		Defrost,
		DefrostExit,
		Off
	}

	public sealed class CryoTankActivationRequestPacket : IPacket, IClientRelayable
	{
		public int TargetNetId;

		public void Serialize(BinaryWriter writer)
		{
			if (TargetNetId == 0)
				throw new InvalidDataException("Invalid cryo tank request");
			writer.Write(TargetNetId);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			if (TargetNetId == 0)
				throw new InvalidDataException("Invalid cryo tank request");
		}

		public void OnDispatched()
		{
			if (!ShouldAccept(MultiplayerSession.IsHost, PacketHandler.CurrentContext) ||
			    !NetworkIdentityRegistry.TryGetComponent(TargetNetId, out CryoTank tank) ||
			    !tank.HasDefrostedFriend())
				return;
			tank.ActivateChore();
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast;
	}

	public sealed class CryoTankStatePacket : IPacket, IHostOnlyPacket
	{
		private const int MaxTextLength = 256;
		private const int MaxEntries = 64;
		private const float MaxCoordinate = 1_000_000f;

		public int TargetNetId;
		public CryoTankPhase Phase;
		public int OpenerNetId;
		public int MinionNetId;
		public ulong MinionLifecycleRevision;
		public Vector3 Position;
		public float ArrivalTime;
		public ImmigrantOptionEntry EntityData;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid cryo tank state");
			writer.Write(TargetNetId);
			writer.Write((byte)Phase);
			writer.Write(OpenerNetId);
			writer.Write(MinionNetId);
			writer.Write(MinionLifecycleRevision);
			if (MinionNetId == 0)
				return;
			writer.Write(Position.x);
			writer.Write(Position.y);
			writer.Write(Position.z);
			writer.Write(ArrivalTime);
			EntityData.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Phase = (CryoTankPhase)reader.ReadByte();
			OpenerNetId = reader.ReadInt32();
			MinionNetId = reader.ReadInt32();
			MinionLifecycleRevision = reader.ReadUInt64();
			if (MinionNetId != 0)
			{
				Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
				ArrivalTime = reader.ReadSingle();
				EntityData = ImmigrantOptionEntry.Deserialize(reader);
			}
			if (!IsWireValid())
				throw new InvalidDataException("Invalid cryo tank state");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost)
				CryoTankSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (TargetNetId == 0 || Phase > CryoTankPhase.Off || MinionNetId == TargetNetId ||
			    OpenerNetId == TargetNetId || (MinionNetId != 0 && MinionNetId == OpenerNetId))
				return false;
			if (MinionNetId == 0)
				return MinionLifecycleRevision == 0 && Phase == CryoTankPhase.Closed && OpenerNetId == 0;
			if (MinionLifecycleRevision == 0)
				return false;
			return ValidCoordinate(Position.x) && ValidCoordinate(Position.y) && ValidCoordinate(Position.z) &&
			       IsFinite(ArrivalTime) && Math.Abs(ArrivalTime) <= MaxCoordinate && ValidDuplicant(EntityData);
		}

		private static bool ValidDuplicant(ImmigrantOptionEntry data)
			=> data.IsValid && data.IsDuplicant && ValidText(data.Name) && ValidText(data.PersonalityId) &&
			   ValidList(data.TraitIds) && ValidText(data.StressTraitId) && ValidText(data.JoyTraitId) &&
			   ValidOptionalText(data.StickerType) && data.VoiceIdx >= 0 && data.VoiceIdx <= 1_000 &&
			   ValidAptitudes(data.SkillAptitudes) && ValidLevels(data.StartingLevels);

		private static bool ValidList(List<string> values)
		{
			if (values == null || values.Count > MaxEntries)
				return false;
			foreach (string value in values)
				if (!ValidText(value))
					return false;
			return true;
		}

		private static bool ValidAptitudes(Dictionary<string, float> values)
		{
			if (values == null || values.Count > MaxEntries)
				return false;
			foreach (KeyValuePair<string, float> pair in values)
				if (!ValidText(pair.Key) || !IsFinite(pair.Value) || Math.Abs(pair.Value) > 1_000f)
					return false;
			return true;
		}

		private static bool ValidLevels(Dictionary<string, int> values)
		{
			if (values == null || values.Count > MaxEntries)
				return false;
			foreach (KeyValuePair<string, int> pair in values)
				if (!ValidText(pair.Key) || pair.Value < 0 || pair.Value > 1_000)
					return false;
			return true;
		}

		private static bool ValidCoordinate(float value)
			=> IsFinite(value) && Math.Abs(value) <= MaxCoordinate;

		private static bool ValidText(string value)
			=> !string.IsNullOrEmpty(value) && value.Length <= MaxTextLength;

		private static bool ValidOptionalText(string value)
			=> value != null && value.Length <= MaxTextLength;

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
