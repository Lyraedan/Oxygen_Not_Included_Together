using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.World
{
    public class StatusItemsSubscribePacket : IPacket
    {
        public int DupeNetId;
        public bool Subscribe;

        public void Serialize(BinaryWriter writer)
        {
            using var _ = Profiler.Scope();
            writer.Write(DupeNetId);
            writer.Write(Subscribe);
        }

        public void Deserialize(BinaryReader reader)
        {
            using var _ = Profiler.Scope();
            DupeNetId = reader.ReadInt32();
            Subscribe = reader.ReadBoolean();
        }

        public void OnDispatched()
        {
            using var _ = Profiler.Scope();
            if (!MultiplayerSession.IsHost) return;
            if (Subscribe)
            {
                DuplicantStatusBroadcaster.SubscribedNetIds.Add(DupeNetId);
                DuplicantStatusBroadcaster.PendingImmediate.Add(DupeNetId);
            }
            else
            {
                DuplicantStatusBroadcaster.SubscribedNetIds.Remove(DupeNetId);
                DuplicantStatusBroadcaster.PendingImmediate.Remove(DupeNetId);
            }
        }
    }
}
