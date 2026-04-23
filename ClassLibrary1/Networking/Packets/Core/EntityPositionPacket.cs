using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking.Packets.Core
{
public class EntityPositionPacket : IPacket
{
	public int NetId;
	public Vector3 Position;
	public bool FlipX;
	public bool FlipY;
	public NavType NavType;
	public long Timestamp;

    public void Serialize(BinaryWriter writer)
	{
		using var _ = Profiler.Scope();

		writer.Write(NetId);
		writer.Write(Position);
		writer.Write(FlipX);
		writer.Write(FlipY);
		writer.Write((byte)NavType);
		writer.Write(Timestamp);
	}

	public void Deserialize(BinaryReader reader)
	{
		using var _ = Profiler.Scope();

		NetId = reader.ReadInt32();
		Position = reader.ReadVector3();
		FlipX = reader.ReadBoolean();
		FlipY = reader.ReadBoolean();
		NavType = (NavType)reader.ReadByte();
		Timestamp = reader.ReadInt64();
	}

	public void OnDispatched()
	{
		using var _ = Profiler.Scope();

		if (MultiplayerSession.IsHost) return;

		if (NetworkIdentityRegistry.TryGet(NetId, out var entity))
		{
			EntityPositionHandler handler = entity.GetComponent<EntityPositionHandler>();
			if (!handler)
				return;

			if (handler.serverTimestamp > Timestamp)
				return;

            handler.serverPosition = Position;
            handler.serverTimestamp = Timestamp;
            handler.serverFlipX = FlipX;
			handler.serverFlipY = FlipY;
			handler.serverNavType = NavType;
        }
		else
		{
			DebugConsole.LogWarning($"[Packets] Could not find entity with NetId {NetId}");
		}
	}
}

}