using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class HighEnergyParticleStatePacket : IPacket, IHostOnlyPacket
	{
		private const float MaxCoordinate = 1_000_000f;
		public int NetId;
		public int Revision;
		public Vector3 Position;
		public EightDirection Direction;
		public float Speed;
		public float Payload;
		public int CapturedByNetId;
		public HighEnergyParticle.CollisionType Collision;
		public int CaptureStorageNetId;
		public float CaptureStoredParticles;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid HEP state");
			writer.Write(NetId);
			writer.Write(Revision);
			writer.Write(Position.x);
			writer.Write(Position.y);
			writer.Write(Position.z);
			writer.Write((byte)Direction);
			writer.Write(Speed);
			writer.Write(Payload);
			writer.Write(CapturedByNetId);
			writer.Write((byte)Collision);
			writer.Write(CaptureStorageNetId);
			writer.Write(CaptureStoredParticles);
		}

		public void Deserialize(BinaryReader reader)
		{
			NetId = reader.ReadInt32();
			Revision = reader.ReadInt32();
			Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			Direction = (EightDirection)reader.ReadByte();
			Speed = reader.ReadSingle();
			Payload = reader.ReadSingle();
			CapturedByNetId = reader.ReadInt32();
			Collision = (HighEnergyParticle.CollisionType)reader.ReadByte();
			CaptureStorageNetId = reader.ReadInt32();
			CaptureStoredParticles = reader.ReadSingle();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid HEP state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				HighEnergyParticleSync.TryApply(this);
		}

		internal bool IsWireValid()
			=> NetId != 0 && Revision >= 0 && Revision <= 1_000_000 && ValidVector(Position) &&
			   Direction >= EightDirection.Up && Direction <= EightDirection.UpRight &&
			   IsFinite(Speed) && Speed >= 0f && Speed <= 10_000f &&
			   IsFinite(Payload) && Payload >= 0f && Payload <= 1_000_000_000f &&
			   CapturedByNetId != NetId &&
			   Collision >= HighEnergyParticle.CollisionType.None &&
			   Collision <= HighEnergyParticle.CollisionType.PassThrough &&
			   IsFinite(CaptureStoredParticles) && CaptureStoredParticles >= 0f &&
			   CaptureStoredParticles <= 1_000_000_000f &&
			   (CaptureStorageNetId != 0 || CaptureStoredParticles == 0f);

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		private static bool ValidVector(Vector3 value)
			=> IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) &&
			   Math.Abs(value.x) <= MaxCoordinate && Math.Abs(value.y) <= MaxCoordinate &&
			   Math.Abs(value.z) <= MaxCoordinate;

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
