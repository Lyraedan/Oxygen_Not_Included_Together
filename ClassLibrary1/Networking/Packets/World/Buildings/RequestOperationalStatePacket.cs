using ONI_MP.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking;
namespace ONI_MP.Networking.Packets.World.Buildings
{
	internal class RequestOperationalStatePacket : IPacket
	{
		public RequestOperationalStatePacket() { }
		public RequestOperationalStatePacket(MonoBehaviour o)
		{
			using var _ = Profiler.Scope();

			NetId = o.GetNetId();
		}

		public int NetId;
		public bool IsActive, IsOperational, IsFunctional;
		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			if (!NetworkIdentityRegistry.TryGet(NetId, out var entity))
				return;
			if (!entity.TryGetComponent<Operational>(out var server))
				return;

			server.IsOperational = server.IsOperational;
		}
	}
}
