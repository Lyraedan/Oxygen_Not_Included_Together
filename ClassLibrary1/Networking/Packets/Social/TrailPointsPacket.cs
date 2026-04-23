using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using Shared.Profiling;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ONI_MP.Networking;

namespace ONI_MP.Networking.Packets.Social
{
	public class TrailPointsPacket : IPacket
	{
		public ulong PlayerID;
		public Color PlayerColor;
		public bool IsNewStroke;
		public List<Vector2> Points = new List<Vector2>();

		public TrailPointsPacket()
		{
		}

		public TrailPointsPacket(List<Vector2> points, bool isNewStroke)
		{
			using var _ = Profiler.Scope();

			PlayerID = MultiplayerSession.LocalUserID;
			PlayerColor = CursorManager.Instance.color;
			IsNewStroke = isNewStroke;
			Points = points;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(PlayerID);
			writer.Write(PlayerColor.r);
			writer.Write(PlayerColor.g);
			writer.Write(PlayerColor.b);
			writer.Write(IsNewStroke);
			writer.Write(Points.Count);
			for (int i = 0; i < Points.Count; i++)
			{
				writer.Write(Points[i].x);
				writer.Write(Points[i].y);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			PlayerID = reader.ReadUInt64();
			float r = reader.ReadSingle();
			float g = reader.ReadSingle();
			float b = reader.ReadSingle();
			PlayerColor = new Color(r, g, b, 1f);
			IsNewStroke = reader.ReadBoolean();
			int count = reader.ReadInt32();
			Points = new List<Vector2>(count);
			for (int i = 0; i < count; i++)
			{
				float x = reader.ReadSingle();
				float y = reader.ReadSingle();
				Points.Add(new Vector2(x, y));
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (PlayerID == MultiplayerSession.LocalUserID)
				return;

			PingManager.Instance?.AddRemoteTrailPoints(PlayerID, Points, PlayerColor, IsNewStroke);

			if (MultiplayerSession.IsHost)
				PacketSender.SendToAllOtherPeers(this);
		}
	}
}
