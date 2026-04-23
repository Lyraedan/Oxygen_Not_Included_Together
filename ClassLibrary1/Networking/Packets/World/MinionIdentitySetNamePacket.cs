using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Patches.World;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Shared.Profiling;

namespace ONI_MP.Networking.Packets.World
{
	internal class MinionIdentitySetNamePacket : IPacket
	{
		public int NetId;
		public string NewName;

		public MinionIdentitySetNamePacket() { }
		public MinionIdentitySetNamePacket(int netId, string newName)
		{
			using var _ = Profiler.Scope();

			NetId = netId;
			NewName = newName;
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			NewName = reader.ReadString();
		}
		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(NewName);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!NetworkIdentityRegistry.TryGetComponent<MinionIdentity>(NetId, out var identity))
			{
				DebugConsole.LogWarning("Could not find MinionIdentity with net id " + NetId);
				return;
			}
			MinionIdentity_Patches.ApplyPacketName(identity, NewName);
			Utils.RefreshIfSelected(identity);
		}
	}
}
