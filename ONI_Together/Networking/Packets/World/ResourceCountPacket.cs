using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Synchronization;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	/// <summary>
	/// Host -> clients: what the inventory screen and the build menu of one world
	/// should say.
	///
	/// A client counts resources from its own copies of the pickupables, and those
	/// copies are not what the host has: ore merged on the ground, anything dropped
	/// out of a container, whatever a lost packet skipped. The visible symptom was a
	/// client at 0 kg of a metal the host had a tonne of, and a build menu that
	/// refused it. The numbers of the host are the answer the client should give -
	/// the host does the actual building from its own stock either way.
	///
	/// One packet per world. Per tag: the hash, GetAmount (what the build menu
	/// checks: the total minus what pending builds have already claimed) and
	/// GetTotalAmount.
	/// </summary>
	public class ResourceCountPacket : IPacket
	{
		public int WorldId;
		public int[] TagHashes = System.Array.Empty<int>();
		public float[] Available = System.Array.Empty<float>();
		public float[] Total = System.Array.Empty<float>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(WorldId);
			writer.Write(TagHashes.Length);
			for (int i = 0; i < TagHashes.Length; i++)
			{
				writer.Write(TagHashes[i]);
				writer.Write(Available[i]);
				writer.Write(Total[i]);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			WorldId = reader.ReadInt32();
			int count = reader.ReadInt32();
			TagHashes = new int[count];
			Available = new float[count];
			Total = new float[count];
			for (int i = 0; i < count; i++)
			{
				TagHashes[i] = reader.ReadInt32();
				Available[i] = reader.ReadSingle();
				Total[i] = reader.ReadSingle();
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;
			ResourceSyncer.ApplyHostCounts(WorldId, TagHashes, Available, Total);
		}
	}
}
