using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Aquatic;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.Aquatic
{
	public enum AquaticShearableAmount : byte
	{
		ScaleGrowth,
		ElementGrowth
	}

	public enum UnderwaterVentPhase : byte
	{
		Off,
		Erupting,
		Blocked,
		Unblocking
	}

	public enum UnderwaterDrillPhase : byte
	{
		Off,
		Idle,
		MissingDiamonds,
		Working
	}

	public sealed class AquaticShearingOutcomePacket : IPacket, IHostOnlyPacket
	{
		private const float MaxGrowth = 100f;
		private const float MaxVelocity = 100f;

		public int CritterNetId;
		public int StationNetId;
		public AquaticShearableAmount AmountKind;
		public float Growth;
		public int ProductNetId;
		public Vector2 ProductVelocity;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsValid())
				throw new InvalidDataException("Invalid aquatic shearing outcome");

			writer.Write(CritterNetId);
			writer.Write(StationNetId);
			writer.Write((byte)AmountKind);
			writer.Write(Growth);
			writer.Write(ProductNetId);
			writer.Write(ProductVelocity.x);
			writer.Write(ProductVelocity.y);
		}

		public void Deserialize(BinaryReader reader)
		{
			CritterNetId = reader.ReadInt32();
			StationNetId = reader.ReadInt32();
			AmountKind = (AquaticShearableAmount)reader.ReadByte();
			Growth = reader.ReadSingle();
			ProductNetId = reader.ReadInt32();
			ProductVelocity = new Vector2(reader.ReadSingle(), reader.ReadSingle());
			if (!IsValid())
				throw new InvalidDataException("Invalid aquatic shearing outcome");
		}

		public void OnDispatched()
		{
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				return;

			if (NetworkIdentityRegistry.TryGet(CritterNetId, out var identity))
				AquaticSync.TryApplyGrowth(identity.gameObject, AmountKind, Growth);
			if (StationNetId != 0 &&
			    NetworkIdentityRegistry.TryGetComponent(StationNetId, out UnderwaterShearingStaion station))
				station.HideShearableSymbol();
			ApplyProductVelocity(ProductNetId, ProductVelocity);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		private bool IsValid()
			=> CritterNetId != 0 && ProductNetId != 0 &&
			   AmountKind <= AquaticShearableAmount.ElementGrowth &&
			   IsFinite(Growth) && Growth >= 0f && Growth <= MaxGrowth &&
			   IsFinite(ProductVelocity.x) && IsFinite(ProductVelocity.y) &&
			   Math.Abs(ProductVelocity.x) <= MaxVelocity && Math.Abs(ProductVelocity.y) <= MaxVelocity;

		internal static bool ShouldApplyProductVelocity(
			bool hasFaller,
			Vector2 currentVelocity,
			Vector2 targetVelocity)
			=> !hasFaller || currentVelocity != targetVelocity;

		private static void ApplyProductVelocity(int productNetId, Vector2 velocity)
		{
			if (!NetworkIdentityRegistry.TryGet(productNetId, out var identity))
				return;
			GameObject product = identity.gameObject;
			bool hasFaller = GameComps.Fallers.Has(product);
			Vector2 currentVelocity = hasFaller
				? GameComps.Fallers.GetData(GameComps.Fallers.GetHandle(product)).initialVelocity
				: Vector2.zero;
			if (!ShouldApplyProductVelocity(hasFaller, currentVelocity, velocity))
				return;
			if (hasFaller)
				GameComps.Fallers.Remove(product);
			GameComps.Fallers.Add(product, velocity);
		}

		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}

	public sealed class UnderwaterVentStatePacket : IPacket, IHostOnlyPacket
	{
		private const float MaxBuildUp = 2f;
		private const float MaxBubbleMass = 10_000f;
		private static readonly Dictionary<(int WorldId, int Cell), int> LastBubbleSequence = new();

		public static void ResetSessionState() => LastBubbleSequence.Clear();

		public int WorldId;
		public int Cell;
		public float BuildUp;
		public UnderwaterVentPhase Phase;
		public int BubbleSequence;
		public bool HasBubble;
		public SimHashes BubbleElement;
		public Vector3 BubblePosition;
		public float BubbleMass;
		public float BubbleTemperature;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsValid())
				throw new InvalidDataException("Invalid underwater vent state");

			writer.Write(WorldId);
			writer.Write(Cell);
			writer.Write(BuildUp);
			writer.Write((byte)Phase);
			writer.Write(BubbleSequence);
			writer.Write(HasBubble);
			if (!HasBubble)
				return;

			writer.Write((int)BubbleElement);
			writer.Write(BubblePosition.x);
			writer.Write(BubblePosition.y);
			writer.Write(BubblePosition.z);
			writer.Write(BubbleMass);
			writer.Write(BubbleTemperature);
		}

		public void Deserialize(BinaryReader reader)
		{
			WorldId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			BuildUp = reader.ReadSingle();
			Phase = (UnderwaterVentPhase)reader.ReadByte();
			BubbleSequence = reader.ReadInt32();
			HasBubble = reader.ReadBoolean();
			if (HasBubble)
			{
				BubbleElement = (SimHashes)reader.ReadInt32();
				BubblePosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
				BubbleMass = reader.ReadSingle();
				BubbleTemperature = reader.ReadSingle();
			}

			if (!IsValid())
				throw new InvalidDataException("Invalid underwater vent state");
		}

		public void OnDispatched()
		{
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				return;

			UnderwaterVent.Instance vent = UnderwaterVentSync.FindVent(WorldId, Cell);
			if (vent == null)
				return;

			UnderwaterVentSync.ApplyState(vent, BuildUp, Phase);
			if (!HasBubble || BubbleManager.instance == null || !TryClaimBubble(WorldId, Cell, BubbleSequence))
				return;

			BubbleManager.instance.SpawnBubble(
				BubbleElement,
				BubblePosition,
				BubbleMass,
				BubbleTemperature,
				BubbleManager.Disease.None);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal static bool IsNewBubbleSequence(int previous, int candidate)
			=> candidate > previous;

		internal static void ForgetBubbleSequence(int worldId, int cell)
			=> LastBubbleSequence.Remove((worldId, cell));

		private bool IsValid()
		{
			if (WorldId < 0 || Cell < 0 || !IsFinite(BuildUp) || BuildUp < 0f || BuildUp > MaxBuildUp ||
			    Phase > UnderwaterVentPhase.Unblocking || BubbleSequence < 0)
				return false;
			if (!HasBubble)
				return true;
			return BubbleElement != SimHashes.Vacuum && IsFinite(BubblePosition.x) &&
			       IsFinite(BubblePosition.y) && IsFinite(BubblePosition.z) &&
			       IsFinite(BubbleMass) && BubbleMass > 0f && BubbleMass <= MaxBubbleMass &&
			       IsFinite(BubbleTemperature) && BubbleTemperature > 0f;
		}

		internal static bool TryClaimBubble(int worldId, int cell, int sequence)
		{
			var key = (worldId, cell);
			int previous = LastBubbleSequence.TryGetValue(key, out int value) ? value : 0;
			if (!IsNewBubbleSequence(previous, sequence))
				return false;
			LastBubbleSequence[key] = sequence;
			return true;
		}

		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}

	public sealed class UnderwaterDrillStatePacket : IPacket, IHostOnlyPacket
	{
		internal const float MaxDiamondMass = 10_000f;

		public int DrillNetId;
		public float Progress;
		public float DiamondMass;
		public UnderwaterDrillPhase Phase;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsValid(DrillNetId, Progress, DiamondMass, Phase))
				throw new InvalidDataException("Invalid underwater drill state");

			writer.Write(DrillNetId);
			writer.Write(Progress);
			writer.Write(DiamondMass);
			writer.Write((byte)Phase);
		}

		public void Deserialize(BinaryReader reader)
		{
			DrillNetId = reader.ReadInt32();
			Progress = reader.ReadSingle();
			DiamondMass = reader.ReadSingle();
			Phase = (UnderwaterDrillPhase)reader.ReadByte();
			if (!IsValid(DrillNetId, Progress, DiamondMass, Phase))
				throw new InvalidDataException("Invalid underwater drill state");
		}

		public void OnDispatched()
		{
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost) ||
			    !NetworkIdentityRegistry.TryGet(DrillNetId, out var identity))
				return;

			UnderwaterVentDrill.Instance drill = identity.gameObject.GetSMI<UnderwaterVentDrill.Instance>();
			if (drill != null)
				UnderwaterDrillSync.ApplyState(drill, Progress, DiamondMass, Phase);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal static bool IsValid(
			int drillNetId,
			float progress,
			float diamondMass,
			UnderwaterDrillPhase phase)
			=> drillNetId != 0 && IsFinite(progress) && progress >= 0f && progress <= 1.1f &&
			   IsFinite(diamondMass) && diamondMass >= 0f && diamondMass <= MaxDiamondMass &&
			   phase <= UnderwaterDrillPhase.Working;

		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
