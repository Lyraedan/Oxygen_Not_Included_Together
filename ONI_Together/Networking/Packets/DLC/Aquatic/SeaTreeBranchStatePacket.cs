using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Aquatic;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.Aquatic
{
	public sealed class SeaTreeBranchStatePacket : IPacket, IHostOnlyPacket
	{
		private const float MaxCoordinate = 1_000_000f;
		private const float MaxAmount = 1_000_000f;
		private static readonly Dictionary<int, int> LastFruitSequence = new();

		public static void ResetSessionState() => LastFruitSequence.Clear();

		public int RootNetId;
		public int PreviousNetId;
		public int BranchNetId;
		public int ChildNetId;
		public int PrefabHash;
		public Vector3 Position;
		public float Maturity;
		public float FruitMaturity;
		public float OldAge;
		public int FruitSequence;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid sea tree branch state");

			writer.Write(RootNetId);
			writer.Write(PreviousNetId);
			writer.Write(BranchNetId);
			writer.Write(ChildNetId);
			writer.Write(PrefabHash);
			writer.Write(Position.x);
			writer.Write(Position.y);
			writer.Write(Position.z);
			writer.Write(Maturity);
			writer.Write(FruitMaturity);
			writer.Write(OldAge);
			writer.Write(FruitSequence);
		}

		public void Deserialize(BinaryReader reader)
		{
			RootNetId = reader.ReadInt32();
			PreviousNetId = reader.ReadInt32();
			BranchNetId = reader.ReadInt32();
			ChildNetId = reader.ReadInt32();
			PrefabHash = reader.ReadInt32();
			Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			Maturity = reader.ReadSingle();
			FruitMaturity = reader.ReadSingle();
			OldAge = reader.ReadSingle();
			FruitSequence = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid sea tree branch state");
		}

		public void OnDispatched()
		{
			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				return;
			SeaTreeBranchSync.Receive(this);
		}

		internal bool IsWireValid()
		{
			if (RootNetId == 0 || BranchNetId == 0 || PrefabHash == 0 || FruitSequence < 0)
				return false;
			if (RootNetId == BranchNetId || PreviousNetId == BranchNetId || ChildNetId == BranchNetId)
				return false;
			return ValidVector(Position) && ValidAmount(Maturity) &&
			       ValidAmount(FruitMaturity) && ValidAmount(OldAge);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal static bool IsNewFruitSequence(int previous, int candidate)
			=> candidate > previous;

		internal static bool TryClaimFruitSequence(int branchNetId, int candidate)
		{
			int previous = LastFruitSequence.TryGetValue(branchNetId, out int value) ? value : 0;
			if (!IsNewFruitSequence(previous, candidate))
				return false;
			LastFruitSequence[branchNetId] = candidate;
			return true;
		}

		private static bool ValidAmount(float value)
			=> ValidFinite(value) && value >= 0f && value <= MaxAmount;

		private static bool ValidVector(Vector3 value)
			=> ValidFinite(value.x) && ValidFinite(value.y) && ValidFinite(value.z) &&
			   Math.Abs(value.x) <= MaxCoordinate && Math.Abs(value.y) <= MaxCoordinate &&
			   Math.Abs(value.z) <= MaxCoordinate;

		private static bool ValidFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
