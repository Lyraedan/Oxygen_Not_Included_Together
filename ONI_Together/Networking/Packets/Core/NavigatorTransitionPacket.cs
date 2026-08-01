/*

using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Core
{
	public class NavigatorTransitionPacket : IPacket
	{
		public int NetId;
		public uint Sequence;
		public bool IsStop;

		public Vector3 SourcePosition;
		public sbyte TransitionX;
		public sbyte TransitionY;
		public float Speed;
		public float AnimSpeed;
		public string Anim;
		public string PreAnim;
		public bool IsLooping;
		public byte StartNavType;
		public byte EndNavType;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(Sequence);
			writer.Write(IsStop);

			if (IsStop)
			{
				writer.Write(SourcePosition);
				writer.Write(EndNavType);
				return;
			}

			writer.Write(SourcePosition);
			writer.Write(TransitionX);
			writer.Write(TransitionY);
			writer.Write(Speed);
			writer.Write(AnimSpeed);
			writer.Write(Anim ?? "");
			writer.Write(PreAnim ?? "");
			writer.Write(IsLooping);
			writer.Write(StartNavType);
			writer.Write(EndNavType);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			Sequence = reader.ReadUInt32();
			IsStop = reader.ReadBoolean();

			if (IsStop)
			{
				SourcePosition = reader.ReadVector3();
				EndNavType = reader.ReadByte();
				return;
			}

			SourcePosition = reader.ReadVector3();
			TransitionX = reader.ReadSByte();
			TransitionY = reader.ReadSByte();
			Speed = reader.ReadSingle();
			AnimSpeed = reader.ReadSingle();
			Anim = reader.ReadString();
			PreAnim = reader.ReadString();
			IsLooping = reader.ReadBoolean();
			StartNavType = reader.ReadByte();
			EndNavType = reader.ReadByte();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGet(NetId, out var entity))
				return;

			var clientController = entity.GetComponent<DuplicantClientController>();
			if (clientController == null)
				return;

			if (IsStop)
			{
				clientController.OnStopReceived((NavType)EndNavType, SourcePosition, Sequence);
			}
			else
			{
				clientController.OnTransitionReceived(this);
			}
		}
	}
}

*/
