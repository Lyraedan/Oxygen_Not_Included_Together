using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;
using ONI_MP.Networking;

namespace ONI_MP.Networking.Packets.Animation
{
	internal class AnimSyncPacket : IPacket
	{
		public int NetId;
		public int AnimHash;
		public byte Mode;
		public float Speed;
		public float ElapsedTime;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(AnimHash);
			writer.Write(Mode);
			writer.Write(Speed);
			writer.Write(ElapsedTime);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			AnimHash = reader.ReadInt32();
			Mode = reader.ReadByte();
			Speed = reader.ReadSingle();
			ElapsedTime = reader.ReadSingle();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (AnimHash == 0)
				return;

			if (!NetworkIdentityRegistry.TryGetComponent<KBatchedAnimController>(NetId, out var kbac))
				return;

			AnimReconciliationHelper.Reconcile(
				kbac,
				new HashedString(AnimHash),
				(KAnim.PlayMode)Mode,
				Speed,
				ElapsedTime,
				nameof(AnimSyncPacket));

			if (kbac.TryGetComponent<AnimStateSyncer>(out var syncer))
				syncer.MarkSnapshotReceived();
		}
	}
}
