using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.DuplicantActions
{
	/// <summary>
	/// Synchronizes high-level duplicant state (action type, work target, etc.)
	/// This helps clients understand what the duplicant is doing beyond just animations.
	/// Includes continuous animation reconciliation to self-correct desync.
	/// </summary>
	public class DuplicantStatePacket : IPacket
	{
		public int NetId;
		public DuplicantActionState ActionState;
		public int TargetCell;          // Cell of work target (-1 if none)
		public string CurrentAnimName;  // specific animation override
		public float AnimElapsedTime;   // Elapsed time in current animation
		public bool IsWorking;          // Whether actively working on something
		public string HeldItemSymbol;   // For syncing guns/tools/carryables current animation
		public int AnimPlayMode;        // KAnim.PlayMode for continuous anim reconciliation
		public float AnimSpeed;         // Playback speed for continuous anim reconciliation

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write((int)ActionState);
			writer.Write(TargetCell);
			writer.Write(CurrentAnimName ?? string.Empty);
			writer.Write(AnimElapsedTime);
			writer.Write(IsWorking);
			writer.Write(HeldItemSymbol ?? string.Empty);
			writer.Write(AnimPlayMode);
			writer.Write(AnimSpeed);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			ActionState = (DuplicantActionState)reader.ReadInt32();
			TargetCell = reader.ReadInt32();
			CurrentAnimName = reader.ReadString();
			AnimElapsedTime = reader.ReadSingle();
			IsWorking = reader.ReadBoolean();
			HeldItemSymbol = reader.ReadString();
			AnimPlayMode = reader.ReadInt32();
			AnimSpeed = reader.ReadSingle();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGetComponent<KBatchedAnimController>(NetId, out var kbac))
				return;

			if (string.IsNullOrEmpty(CurrentAnimName))
				return;

			AnimReconciliationHelper.Reconcile(
				kbac,
				new HashedString(CurrentAnimName),
				(KAnim.PlayMode)AnimPlayMode,
				AnimSpeed,
				AnimElapsedTime,
				nameof(DuplicantStatePacket));
		}
	}

	/// <summary>
	/// High-level action states for duplicants
	/// </summary>
	public enum DuplicantActionState : byte
	{
		Idle = 0,
		Walking = 1,
		Working = 2,
		Building = 3,
		Digging = 4,
		Eating = 5,
		Sleeping = 6,
		Using = 7,       // Using a machine/station
		Carrying = 9,
		Climbing = 10,
		Swimming = 11,
		Falling = 12,
		Disinfecting = 13,
		Other = 100
	}
}
