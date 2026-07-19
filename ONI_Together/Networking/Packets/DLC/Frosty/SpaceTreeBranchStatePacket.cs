using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Frosty;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DLC.Frosty
{
	public sealed class SpaceTreeBranchStatePacket : IPacket, IHostOnlyPacket
	{
		private const int MaxSlot = 63;
		private const float MaxCoordinate = 1_000_000f;

		public int TrunkNetId;
		public int Slot;
		public int BranchNetId;
		public int PrefabHash;
		public Vector3 Position;
		public float Growth;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid space tree branch state");
			writer.Write(TrunkNetId);
			writer.Write(Slot);
			writer.Write(BranchNetId);
			writer.Write(PrefabHash);
			writer.Write(Position.x);
			writer.Write(Position.y);
			writer.Write(Position.z);
			writer.Write(Growth);
		}

		public void Deserialize(BinaryReader reader)
		{
			TrunkNetId = reader.ReadInt32();
			Slot = reader.ReadInt32();
			BranchNetId = reader.ReadInt32();
			PrefabHash = reader.ReadInt32();
			Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			Growth = reader.ReadSingle();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid space tree branch state");
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost || !PacketHandler.CurrentContext.SenderIsHost)
				return;
			SpaceTreeBranchSync.Receive(this);
		}

		internal bool IsWireValid()
		{
			if (TrunkNetId == 0 || BranchNetId == 0 || TrunkNetId == BranchNetId ||
			    PrefabHash == 0 || Slot < 0 || Slot > MaxSlot || Growth < 0f || Growth > 1f)
				return false;
			return IsFinite(Growth) && IsCoordinate(Position.x) && IsCoordinate(Position.y) &&
			       IsCoordinate(Position.z);
		}

		private static bool IsCoordinate(float value)
			=> IsFinite(value) && Math.Abs(value) <= MaxCoordinate;

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
