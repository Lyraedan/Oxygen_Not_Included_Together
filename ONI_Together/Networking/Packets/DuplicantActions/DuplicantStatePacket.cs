using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.DuplicantActions
{
	/// <summary>
	/// Periodic compact animation/action keyframe. Ordered Play/Queue events remain
	/// the primary source; this packet repairs loss and slow phase drift without
	/// independently writing the client animation controller.
	/// </summary>
	public sealed class DuplicantStatePacket : IPacket
	{
		public int NetId;
		public uint Sequence;
		public long ServerTimestamp;
		public DuplicantActionState ActionState;
		public int TargetCell;
		public int CurrentAnimHash;
		public float AnimElapsedTime;
		public bool IsWorking;
		public int HeldItemSymbolHash;
		public byte AnimPlayMode;
		public float AnimSpeed;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(Sequence);
			writer.Write(ServerTimestamp);
			writer.Write((byte)ActionState);
			writer.Write(TargetCell);
			writer.Write(CurrentAnimHash);
			writer.Write(AnimElapsedTime);
			writer.Write(IsWorking);
			writer.Write(HeldItemSymbolHash);
			writer.Write(AnimPlayMode);
			writer.Write(AnimSpeed);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			Sequence = reader.ReadUInt32();
			ServerTimestamp = reader.ReadInt64();
			ActionState = (DuplicantActionState)reader.ReadByte();
			TargetCell = reader.ReadInt32();
			CurrentAnimHash = reader.ReadInt32();
			AnimElapsedTime = reader.ReadSingle();
			IsWorking = reader.ReadBoolean();
			HeldItemSymbolHash = reader.ReadInt32();
			AnimPlayMode = reader.ReadByte();
			AnimSpeed = reader.ReadSingle();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;
			if (!NetworkIdentityRegistry.TryGet(NetId, out var entity))
				return;

			if (entity.TryGetComponent<DuplicantClientController>(out var playback)
				&& playback.IsPlaybackActive)
			{
				playback.OnStateReceived(this);
				return;
			}

			if (CurrentAnimHash == 0 || !entity.TryGetComponent<KBatchedAnimController>(out var kbac))
				return;

			AnimReconciliationHelper.Reconcile(
				kbac,
				new HashedString(CurrentAnimHash),
				(KAnim.PlayMode)AnimPlayMode,
				AnimSpeed,
				AnimElapsedTime,
				nameof(DuplicantStatePacket));
		}
	}

	public enum DuplicantActionState : byte
	{
		Idle = 0,
		Walking = 1,
		Working = 2,
		Building = 3,
		Digging = 4,
		Eating = 5,
		Sleeping = 6,
		Using = 7,
		Carrying = 9,
		Climbing = 10,
		Swimming = 11,
		Falling = 12,
		Disinfecting = 13,
		Other = 100,
	}
}
