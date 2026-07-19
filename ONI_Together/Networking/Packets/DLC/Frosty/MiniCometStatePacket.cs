using System;
using System.IO;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Frosty;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.Frosty
{
	public sealed class MiniCometStatePacket : IPacket, IHostOnlyPacket
	{
		private const float MaxCoordinate = 1_000_000f;
		public int TargetNetId;
		public Vector3 Position;
		public Vector3 Offset;
		public Vector2 Velocity;
		public float Rotation;
		public SimHashes Element;
		public float Mass;
		public float Temperature;
		public byte DiseaseIndex;
		public int DiseaseCount;
		public bool Targeted;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid mini comet state");
			writer.Write(TargetNetId);
			WriteVector3(writer, Position);
			WriteVector3(writer, Offset);
			writer.Write(Velocity.x);
			writer.Write(Velocity.y);
			writer.Write(Rotation);
			writer.Write((int)Element);
			writer.Write(Mass);
			writer.Write(Temperature);
			writer.Write(DiseaseIndex);
			writer.Write(DiseaseCount);
			writer.Write(Targeted);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Position = ReadVector3(reader);
			Offset = ReadVector3(reader);
			Velocity = new Vector2(reader.ReadSingle(), reader.ReadSingle());
			Rotation = reader.ReadSingle();
			Element = (SimHashes)reader.ReadInt32();
			Mass = reader.ReadSingle();
			Temperature = reader.ReadSingle();
			DiseaseIndex = reader.ReadByte();
			DiseaseCount = reader.ReadInt32();
			Targeted = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid mini comet state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				MiniCometSync.Receive(this);
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 && ValidVector(Position) && ValidVector(Offset) &&
			   ValidFinite(Velocity.x) && ValidFinite(Velocity.y) &&
			   Math.Abs(Velocity.x) <= 1000f && Math.Abs(Velocity.y) <= 1000f &&
			   ValidFinite(Rotation) && Math.Abs(Rotation) <= 36000f &&
			   Element != SimHashes.Vacuum && ValidFinite(Mass) && Mass >= 0f && Mass <= 1_000_000f &&
			   ValidFinite(Temperature) && Temperature >= 0f && Temperature <= 10000f && DiseaseCount >= 0;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		private static bool ValidVector(Vector3 value)
			=> ValidFinite(value.x) && ValidFinite(value.y) && ValidFinite(value.z) &&
			   Math.Abs(value.x) <= MaxCoordinate && Math.Abs(value.y) <= MaxCoordinate &&
			   Math.Abs(value.z) <= MaxCoordinate;

		private static bool ValidFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

		private static void WriteVector3(BinaryWriter writer, Vector3 value)
		{
			writer.Write(value.x);
			writer.Write(value.y);
			writer.Write(value.z);
		}

		private static Vector3 ReadVector3(BinaryReader reader)
			=> new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
	}
}
