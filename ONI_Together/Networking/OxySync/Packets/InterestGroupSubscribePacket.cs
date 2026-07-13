using System.IO;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Networking.OxySync.Packets
{
    public class InterestGroupSubscribePacket : IPacket
    {
        public ulong SenderId;
        public int GroupId;
        public bool Subscribe;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(SenderId);
            writer.Write(GroupId);
            writer.Write(Subscribe);
        }

        public void Deserialize(BinaryReader reader)
        {
            SenderId = reader.ReadUInt64();
            GroupId = reader.ReadInt32();
            Subscribe = reader.ReadBoolean();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();

            if (!MultiplayerSession.IsHost) return;

            if (Subscribe)
            {
                InterestGroupManager.AddPlayerToGroup(SenderId, GroupId);
                OxySyncManager.SendFullStateToPlayerForGroup(SenderId, GroupId);
            }
            else
                InterestGroupManager.RemovePlayerFromGroup(SenderId, GroupId);
        }
    }
}