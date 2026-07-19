using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Social
{
	public class PingPacket : IPacket, IClientRelayable, ISenderBoundRelay
	{
		public ulong PlayerID;
		ulong ISenderBoundRelay.RelaySenderId => PlayerID;
		public float WorldX;
		public float WorldY;
		public Color PlayerColor;

		public PingPacket()
		{
		}

		public PingPacket(Vector3 worldPosition)
		{
			using var _ = Profiler.Scope();

			PlayerID = MultiplayerSession.LocalUserID;
			WorldX = worldPosition.x;
			WorldY = worldPosition.y;
			PlayerColor = CursorManager.Instance.color;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(PlayerID);
			writer.Write(WorldX);
			writer.Write(WorldY);
			writer.Write(PlayerColor.r);
			writer.Write(PlayerColor.g);
			writer.Write(PlayerColor.b);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			PlayerID = reader.ReadUInt64();
			WorldX = reader.ReadSingle();
			WorldY = reader.ReadSingle();
			float r = reader.ReadSingle();
			float g = reader.ReadSingle();
			float b = reader.ReadSingle();
			PlayerColor = new Color(r, g, b, 1f);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (PlayerID == MultiplayerSession.LocalUserID)
				return;

			PingManager.Instance?.ShowPing(PlayerID, new Vector3(WorldX, WorldY, 0f), PlayerColor);
		}
	}
}
