using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Patches.DLC.Aquatic;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.Aquatic
{
	public enum MinnowPoiOperation : byte
	{
		Discover,
		ToggleDelivery,
		AcknowledgeCompletion
	}

	public enum MinnowPoiPhase : byte
	{
		Off,
		Waiting,
		Working,
		CompletionPending,
		CompletionAcknowledged,
		Completed
	}

	public sealed class MinnowPoiRequestPacket : IPacket, IClientRelayable
	{
		public int TargetNetId;
		public MinnowPoiOperation Operation;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid Minnow POI request");
			writer.Write(TargetNetId);
			writer.Write((byte)Operation);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Operation = (MinnowPoiOperation)reader.ReadByte();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid Minnow POI request");
		}

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool verified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (ShouldAccept(MultiplayerSession.IsHost, context, verified))
				MinnowPoiSync.TryHandleRequest(this);
		}

		internal bool IsWireValid() => TargetNetId != 0 && Operation <= MinnowPoiOperation.AcknowledgeCompletion;

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;
	}

	public sealed class MinnowPoiStatePacket : IPacket, IHostOnlyPacket
	{
		public int TargetNetId;
		public MinnowPoiPhase Phase;
		public bool HasShownQuestPopup;
		public bool HasShownCompletedPopup;
		public bool IsCompleted;
		public bool DeliveryEnabled;
		public int QuestsCompleted;
		public bool AllQuestsCompleted;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid Minnow POI state");
			writer.Write(TargetNetId);
			writer.Write((byte)Phase);
			writer.Write(HasShownQuestPopup);
			writer.Write(HasShownCompletedPopup);
			writer.Write(IsCompleted);
			writer.Write(DeliveryEnabled);
			writer.Write(QuestsCompleted);
			writer.Write(AllQuestsCompleted);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Phase = (MinnowPoiPhase)reader.ReadByte();
			HasShownQuestPopup = reader.ReadBoolean();
			HasShownCompletedPopup = reader.ReadBoolean();
			IsCompleted = reader.ReadBoolean();
			DeliveryEnabled = reader.ReadBoolean();
			QuestsCompleted = reader.ReadInt32();
			AllQuestsCompleted = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid Minnow POI state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				MinnowPoiSync.TryApplyState(this);
		}

		internal bool IsWireValid()
		{
			if (TargetNetId == 0 || Phase > MinnowPoiPhase.Completed || QuestsCompleted < 0 || QuestsCompleted > 3)
				return false;
			if (HasShownCompletedPopup && !IsCompleted)
				return false;
			return !AllQuestsCompleted || QuestsCompleted == 3;
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost) => !localIsHost && senderIsHost;
	}

	public sealed class MinnowSpawnStatePacket : IPacket, IHostOnlyPacket
	{
		private const int MaxTextLength = 256;
		private const int MaxEntries = 64;
		private const float MaxCoordinate = 1_000_000f;

		public int SourceNetId;
		public int MinionNetId;
		public ulong LifecycleRevision;
		public Vector3 Position;
		public float ArrivalTime;
		public int SkillPoints;
		public ImmigrantOptionEntry EntityData;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid Minnow spawn state");
			writer.Write(SourceNetId);
			writer.Write(MinionNetId);
			writer.Write(LifecycleRevision);
			writer.Write(Position.x);
			writer.Write(Position.y);
			writer.Write(Position.z);
			writer.Write(ArrivalTime);
			writer.Write(SkillPoints);
			EntityData.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			SourceNetId = reader.ReadInt32();
			MinionNetId = reader.ReadInt32();
			LifecycleRevision = reader.ReadUInt64();
			Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			ArrivalTime = reader.ReadSingle();
			SkillPoints = reader.ReadInt32();
			EntityData = ImmigrantOptionEntry.Deserialize(reader);
			if (!IsWireValid())
				throw new InvalidDataException("Invalid Minnow spawn state");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost)
				MinnowPoiSync.TryApplyMinnowSpawn(this);
		}

		internal bool IsWireValid()
		{
			return SourceNetId != 0 && MinionNetId != 0 && LifecycleRevision != 0 &&
			       SourceNetId != MinionNetId &&
			       ValidNumber(Position.x) && ValidNumber(Position.y) && ValidNumber(Position.z) &&
			       ValidNumber(ArrivalTime) && SkillPoints >= 0 && SkillPoints <= 1_000 &&
			       EntityData.IsDuplicant && EntityData.PersonalityId == "MINNOW" &&
			       ValidText(EntityData.Name) && ValidText(EntityData.StressTraitId) &&
			       ValidText(EntityData.JoyTraitId) && ValidOptionalText(EntityData.StickerType) &&
			       EntityData.VoiceIdx >= 0 && EntityData.VoiceIdx <= 1_000 &&
			       ValidList(EntityData.TraitIds) && ValidAptitudes(EntityData.SkillAptitudes) &&
			       ValidLevels(EntityData.StartingLevels);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost, bool entityExists)
			=> !localIsHost && senderIsHost && !entityExists;

		internal static bool CanApplyLifecycle(
			ulong currentRevision, bool tombstoned, ulong incomingRevision)
			=> incomingRevision != 0 && currentRevision <= incomingRevision &&
			   (currentRevision != incomingRevision || !tombstoned);

		private static bool ValidNumber(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= MaxCoordinate;

		private static bool ValidText(string value) => !string.IsNullOrEmpty(value) && value.Length <= MaxTextLength;
		private static bool ValidOptionalText(string value) => value != null && value.Length <= MaxTextLength;

		private static bool ValidList(List<string> values)
		{
			if (values == null || values.Count > MaxEntries)
				return false;
			foreach (string value in values)
				if (!ValidText(value)) return false;
			return true;
		}

		private static bool ValidAptitudes(Dictionary<string, float> values)
		{
			if (values == null || values.Count > MaxEntries)
				return false;
			foreach (KeyValuePair<string, float> pair in values)
				if (!ValidText(pair.Key) || !ValidNumber(pair.Value)) return false;
			return true;
		}

		private static bool ValidLevels(Dictionary<string, int> values)
		{
			if (values == null || values.Count > MaxEntries)
				return false;
			foreach (KeyValuePair<string, int> pair in values)
				if (!ValidText(pair.Key) || pair.Value < 0 || pair.Value > 1_000) return false;
			return true;
		}
	}
}

namespace ONI_Together.Patches.DLC.Aquatic
{
	internal readonly struct MinnowPoiSnapshot : IEquatable<MinnowPoiSnapshot>
	{
		internal readonly int TargetNetId;
		internal readonly MinnowPoiPhase Phase;
		internal readonly bool HasShownQuestPopup;
		internal readonly bool HasShownCompletedPopup;
		internal readonly bool IsCompleted;
		internal readonly bool DeliveryEnabled;
		internal readonly int QuestsCompleted;
		internal readonly bool AllQuestsCompleted;

		internal MinnowPoiSnapshot(int targetNetId, MinnowPoiPhase phase,
			bool hasShownQuestPopup, bool hasShownCompletedPopup, bool isCompleted,
			bool deliveryEnabled, int questsCompleted, bool allQuestsCompleted)
		{
			TargetNetId = targetNetId;
			Phase = phase;
			HasShownQuestPopup = hasShownQuestPopup;
			HasShownCompletedPopup = hasShownCompletedPopup;
			IsCompleted = isCompleted;
			DeliveryEnabled = deliveryEnabled;
			QuestsCompleted = questsCompleted;
			AllQuestsCompleted = allQuestsCompleted;
		}

		public bool Equals(MinnowPoiSnapshot other)
			=> TargetNetId == other.TargetNetId && Phase == other.Phase &&
			   HasShownQuestPopup == other.HasShownQuestPopup &&
			   HasShownCompletedPopup == other.HasShownCompletedPopup &&
			   IsCompleted == other.IsCompleted && DeliveryEnabled == other.DeliveryEnabled &&
			   QuestsCompleted == other.QuestsCompleted && AllQuestsCompleted == other.AllQuestsCompleted;
	}
}
