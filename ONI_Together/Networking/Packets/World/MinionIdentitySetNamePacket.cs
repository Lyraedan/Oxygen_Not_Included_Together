using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DuplicantActions;
using ONI_Together.Patches.World;
using ONI_Together.Patches.World.Buildings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	internal class MinionIdentitySetNamePacket : IPacket, IClientRelayable
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
