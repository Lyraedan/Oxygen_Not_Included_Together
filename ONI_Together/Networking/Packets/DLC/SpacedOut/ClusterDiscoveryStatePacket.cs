using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public enum ClusterDiscoveryKind : byte
	{
		Fog,
		Meteor
	}

	public sealed class ClusterDiscoveryStatePacket : IPacket, IHostOnlyPacket
	{
		private const int MaxWorldId = 1024;
		private const float MaxArrivalTime = 1_000_000_000f;
		public ClusterDiscoveryKind Kind;
		public int LocationQ;
		public int LocationR;
		public int DestinationWorldId;
		public float MeteorArrivalTime;
		public float Progress;
		public bool Complete;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid cluster discovery state");
			writer.Write((byte)Kind);
			writer.Write(LocationQ);
			writer.Write(LocationR);
			writer.Write(DestinationWorldId);
			writer.Write(MeteorArrivalTime);
			writer.Write(Progress);
			writer.Write(Complete);
		}

		public void Deserialize(BinaryReader reader)
		{
			Kind = (ClusterDiscoveryKind)reader.ReadByte();
			LocationQ = reader.ReadInt32();
			LocationR = reader.ReadInt32();
			DestinationWorldId = reader.ReadInt32();
			MeteorArrivalTime = reader.ReadSingle();
			Progress = reader.ReadSingle();
			Complete = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid cluster discovery state");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost)
				ClusterDiscoverySync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (Kind > ClusterDiscoveryKind.Meteor ||
			    !RocketSettingsPacketData.CoordinateWithinBounds(LocationQ, LocationR) ||
			    float.IsNaN(Progress) || float.IsInfinity(Progress) || Progress < 0f || Progress > 1f ||
			    Complete && Progress != 1f)
				return false;
			if (Kind == ClusterDiscoveryKind.Fog)
				return DestinationWorldId == 0 && MeteorArrivalTime == 0f;
			return DestinationWorldId >= 0 && DestinationWorldId <= MaxWorldId &&
			       !float.IsNaN(MeteorArrivalTime) && !float.IsInfinity(MeteorArrivalTime) &&
			       MeteorArrivalTime >= 0f && MeteorArrivalTime <= MaxArrivalTime;
		}

		internal static bool NeedsApply(float currentProgress, bool currentComplete, ClusterDiscoveryStatePacket state)
			=> currentComplete != state.Complete || Math.Abs(currentProgress - state.Progress) > 0.0001f;
	}
}
