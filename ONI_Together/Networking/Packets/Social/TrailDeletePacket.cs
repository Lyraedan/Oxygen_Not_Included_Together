using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Social
{
	public class TrailDeletePacket : IPacket, IClientRelayable, ISenderBoundRelay
	{
		public ulong PlayerID;
		ulong ISenderBoundRelay.RelaySenderId => PlayerID;
		public float WorldX;
		public float WorldY;

		public TrailDeletePacket()
		{
		}

		public TrailDeletePacket(Vector2 worldPos)
		{
			using var _ = Profiler.Scope();

			PlayerID = MultiplayerSession.LocalUserID;
			WorldX = worldPos.x;
			WorldY = worldPos.y;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(PlayerID);
			writer.Write(WorldX);
			writer.Write(WorldY);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			PlayerID = reader.ReadUInt64();
			WorldX = reader.ReadSingle();
			WorldY = reader.ReadSingle();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (PlayerID == MultiplayerSession.LocalUserID)
				return;

			PingManager.Instance?.DeleteTrailAtPosition(new Vector2(WorldX, WorldY));
		}
	}
}
