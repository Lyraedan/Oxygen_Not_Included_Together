using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.World.Buildings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Buildings
{
	internal class UserNameableChangePacket : IPacket
	{
		public int NetId;
		public string NewName;

		public UserNameableChangePacket() { }
		public UserNameableChangePacket(int netId, string newName)
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

			if (!NetworkIdentityRegistry.TryGetComponent<UserNameable>(NetId, out var nameable))
			{
				DebugConsole.LogWarning("Could not find UserNameable with net id " + NetId);
				return;
			}
			UserNameablePatch.ApplyPacketName(nameable, NewName);
			Utils.RefreshIfSelected(nameable);
		}
	}
}
