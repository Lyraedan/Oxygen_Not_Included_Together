using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.Chores
{
	public class ChoreErrandsSubscribePacket : IPacket
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
				DuplicantChoreBroadcaster.SubscribedNetIds.Add(DupeNetId);
				DuplicantChoreBroadcaster.PendingImmediate.Add(DupeNetId);
			}
			else
			{
				DuplicantChoreBroadcaster.SubscribedNetIds.Remove(DupeNetId);
				DuplicantChoreBroadcaster.PendingImmediate.Remove(DupeNetId);
			}
		}
	}
}
