using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Prehistoric;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.Prehistoric
{
	public enum VineMotherSide : byte
	{
		Left = 1,
		Right = 2
	}

	public sealed class VineBranchStatePacket : IPacket, IHostOnlyPacket
	{
		private const float MaxCoordinate = 1_000_000f;

		public int MotherNetId;
		public int PreviousNetId;
		public int BranchNetId;
		public int PrefabHash;
		public Vector3 Position;
		public VineMotherSide MotherSide;
		public VineBranch.Shape Shape;
		public VineBranch.Shape RootShape;
		public Direction RootDirection;
		public int BranchNumber;
		public bool GrowingClockwise;
		public bool WildPlanted;
		public float Growth;
		public float FruitGrowth;
		public float OldAge;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid vine branch state");
			writer.Write(MotherNetId);
			writer.Write(PreviousNetId);
			writer.Write(BranchNetId);
			writer.Write(PrefabHash);
			writer.Write(Position.x);
			writer.Write(Position.y);
			writer.Write(Position.z);
			writer.Write((byte)MotherSide);
			writer.Write((byte)Shape);
			writer.Write((byte)RootShape);
			writer.Write((byte)RootDirection);
			writer.Write((byte)BranchNumber);
			writer.Write(GrowingClockwise);
			writer.Write(WildPlanted);
			writer.Write(Growth);
			writer.Write(FruitGrowth);
			writer.Write(OldAge);
		}

		public void Deserialize(BinaryReader reader)
		{
			MotherNetId = reader.ReadInt32();
			PreviousNetId = reader.ReadInt32();
			BranchNetId = reader.ReadInt32();
			PrefabHash = reader.ReadInt32();
			Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			MotherSide = (VineMotherSide)reader.ReadByte();
			Shape = (VineBranch.Shape)reader.ReadByte();
			RootShape = (VineBranch.Shape)reader.ReadByte();
			RootDirection = (Direction)reader.ReadByte();
			BranchNumber = reader.ReadByte();
			GrowingClockwise = reader.ReadBoolean();
			WildPlanted = reader.ReadBoolean();
			Growth = reader.ReadSingle();
			FruitGrowth = reader.ReadSingle();
			OldAge = reader.ReadSingle();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid vine branch state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				VineBranchSync.Receive(this);
		}

		internal bool IsWireValid()
			=> MotherNetId != 0 && BranchNetId != 0 && PrefabHash != 0 &&
			   MotherNetId != BranchNetId && PreviousNetId != BranchNetId &&
			   (MotherSide == VineMotherSide.Left || MotherSide == VineMotherSide.Right) &&
			   Enum.IsDefined(typeof(VineBranch.Shape), Shape) &&
			   Enum.IsDefined(typeof(VineBranch.Shape), RootShape) &&
			   Enum.IsDefined(typeof(Direction), RootDirection) &&
			   BranchNumber >= 1 && BranchNumber <= 12 && ValidPosition(Position) &&
			   ValidPercent(Growth) && ValidPercent(FruitGrowth) && ValidPercent(OldAge);

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		private static bool ValidPosition(Vector3 value)
			=> ValidFinite(value.x) && ValidFinite(value.y) && ValidFinite(value.z) &&
			   Math.Abs(value.x) <= MaxCoordinate && Math.Abs(value.y) <= MaxCoordinate &&
			   Math.Abs(value.z) <= MaxCoordinate;

		private static bool ValidFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
		private static bool ValidPercent(float value) => ValidFinite(value) && value >= 0f && value <= 1f;
	}
}
