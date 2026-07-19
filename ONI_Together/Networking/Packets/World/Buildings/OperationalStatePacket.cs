using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Scripts.Buildings;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Buildings
{
	internal class OperationalStatePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		private const string RevisionDomain = "operational";

		public OperationalStatePacket() { }
		public OperationalStatePacket(Operational o)
		{
			using var _ = Profiler.Scope();
			TryPopulate(o);
		}

		public int NetId;
		public ulong LifecycleRevision;
		public ulong Revision;
		public bool IsActive, IsOperational, IsFunctional;

		internal static bool TryCreate(Operational operational, out OperationalStatePacket packet)
		{
			packet = new OperationalStatePacket();
			if (packet.TryPopulate(operational))
				return true;
			packet = null;
			return false;
		}

		private bool TryPopulate(Operational operational)
		{
			if (operational == null || operational.IsNullOrDestroyed()
			    || !operational.TryGetComponent(out NetworkIdentity identity)
			    || identity.NetId == 0 || identity.LifecycleRevision == 0
			    || identity.IsLifecycleTerminal
			    || !NetworkIdentityRegistry.IsRegistered(identity, identity.NetId)
			    || NetworkIdentityRegistry.GetLastLifecycleRevision(identity.NetId)
			       != identity.LifecycleRevision
			    || NetworkIdentityRegistry.IsLifecycleTombstoned(identity.NetId))
				return false;
			NetId = identity.NetId;
			LifecycleRevision = identity.LifecycleRevision;
			Revision = NetworkIdentityRegistry.NextAuthorityRevision();
			IsOperational = operational.IsOperational;
			IsFunctional = operational.IsFunctional;
			IsActive = operational.IsActive;
			return true;
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			LifecycleRevision = reader.ReadUInt64();
			Revision = reader.ReadUInt64();
			IsOperational = reader.ReadBoolean();
			IsFunctional = reader.ReadBoolean();
			IsActive = reader.ReadBoolean();
			if (NetId == 0 || LifecycleRevision == 0 || Revision == 0)
				throw new InvalidDataException("Invalid operational state NetId");
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (NetId == 0 || LifecycleRevision == 0 || Revision == 0)
				throw new InvalidDataException("Invalid operational state NetId");

			writer.Write(NetId);
			writer.Write(LifecycleRevision);
			writer.Write(Revision);
			writer.Write(IsOperational);
			writer.Write(IsFunctional);
			writer.Write(IsActive);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			ulong current = NetworkIdentityRegistry.GetLastLifecycleRevision(NetId);
			if (!ShouldApplyLifecycle(
				    current, NetworkIdentityRegistry.IsLifecycleTombstoned(NetId),
				    LifecycleRevision)
			    || !NetworkIdentityRegistry.IsNewerRevision(
				    NetworkIdentityRegistry.GetLastStateRevision(NetId, RevisionDomain), Revision)
			    || !NetworkIdentityRegistry.TryGet(NetId, out NetworkIdentity entity)
			    || entity.LifecycleRevision != LifecycleRevision
			    || !NetworkIdentityRegistry.IsRegistered(entity, NetId))
				return;
			if (!entity.TryGetComponent<ClientReceiver_Operational>(out var client))
				return;

			client.ApplySnapshot(IsActive, IsOperational, IsFunctional);
			NetworkIdentityRegistry.TryAcceptStateRevision(NetId, RevisionDomain, Revision);
		}

		internal static bool ShouldApplyLifecycle(
			ulong current, bool tombstoned, ulong incoming)
			=> incoming != 0 && current == incoming && !tombstoned;
	}
}
