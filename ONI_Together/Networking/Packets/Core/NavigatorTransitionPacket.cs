using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Core
{
	/// <summary>
	/// Ordered semantic duplicant navigation event. The transition id addresses
	/// the receiver's own NavGrid definition, allowing ONI's original visual
	/// transition driver to replay ladders, poles, jumps, tubes and floor motion.
	/// </summary>
	public sealed class NavigatorTransitionPacket : IPacket, ILatencySensitivePacket
	{
		public int NetId;
		public uint Sequence;
		public long ServerTimestamp;
		public bool IsStop;
		public Vector3 SourcePosition;
		public byte TransitionId;
		public float Speed;
		public NavType StopNavType;
		public bool PlayIdle;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(Sequence);
			writer.Write(ServerTimestamp);
			writer.Write(IsStop);
			writer.Write(SourcePosition);
			if (IsStop)
			{
				writer.Write((byte)StopNavType);
				writer.Write(PlayIdle);
				return;
			}

			writer.Write(TransitionId);
			writer.Write(Speed);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			Sequence = reader.ReadUInt32();
			ServerTimestamp = reader.ReadInt64();
			IsStop = reader.ReadBoolean();
			SourcePosition = reader.ReadVector3();
			if (IsStop)
			{
				StopNavType = (NavType)reader.ReadByte();
				PlayIdle = reader.ReadBoolean();
				return;
			}

			TransitionId = reader.ReadByte();
			Speed = reader.ReadSingle();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGet(NetId, out var entity))
				return;

			if (entity.TryGetComponent<DuplicantClientController>(out var playback))
				playback.OnNavigationEventReceived(this);
		}
	}
}
