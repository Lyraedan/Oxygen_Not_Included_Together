using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.States;
using ONI_Together.Scripts.Buildings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Buildings
{
	internal class OperationalStatePacket : IPacket
	{
		public OperationalStatePacket() { }
		public OperationalStatePacket(Operational o)
		{
			using var _ = Profiler.Scope();

			NetId = o.GetNetId();
			if (NetId == 0)
				return;
			IsOperational = o.IsOperational;
			IsFunctional = o.IsFunctional;
			IsActive = o.IsActive;
			IsPowered = o.GetFlag(EnergyConsumer.PoweredFlag);
		}

		public int NetId;
		public bool IsOperational, IsFunctional, IsActive, IsPowered;
		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			IsOperational = reader.ReadBoolean();
			IsFunctional = reader.ReadBoolean();
			IsActive = reader.ReadBoolean();
			IsPowered = reader.ReadBoolean();
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(IsOperational);
			writer.Write(IsFunctional);
			writer.Write(IsActive);
			writer.Write(IsPowered);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGet(NetId, out var entity))
				return;
			if (!entity.TryGetComponent<ClientReceiver_Operational>(out var client))
				return;

			client.IsOperational = IsOperational;
			client.IsFunctional = IsFunctional;
			client.IsActive = IsActive;
			client.IsPowered = IsPowered;
		}
	}
}
