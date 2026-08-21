using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class PrioritizeStatePacket : IPacket
	{
		public struct PriorityData
		{
			public int NetId;
			public int Cell;
			public int PriorityClass;
			public int PriorityValue;
		}

		public List<PriorityData> Priorities = new List<PriorityData>();
		public static bool IsApplying = false;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Priorities.Count);
			foreach (var p in Priorities)
			{
				writer.Write(p.NetId);
				writer.Write(p.Cell);
				writer.Write(p.PriorityClass);
				writer.Write(p.PriorityValue);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			Priorities = new List<PriorityData>(count);
			for (int i = 0; i < count; i++)
			{
				Priorities.Add(new PriorityData
				{
					NetId = reader.ReadInt32(),
					Cell = reader.ReadInt32(),
					PriorityClass = reader.ReadInt32(),
					PriorityValue = reader.ReadInt32()
				});
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			// Both host and client need to apply priority changes
			try
			{
				IsApplying = true;
				foreach (var p in Priorities)
				{
					Prioritizable prioritizable = null;
					if (NetworkIdentityRegistry.TryGet(p.NetId, out var identity) && identity != null)
					{
						prioritizable = identity.GetComponent<Prioritizable>();
					}

					// Fallback lookup by cell for items/pickupables if NetId is 0 or unmapped
					if (prioritizable == null && Grid.IsValidCell(p.Cell))
					{
						var pickupableGo = Grid.Objects[p.Cell, (int)ObjectLayer.Pickupables];
						if (pickupableGo != null)
						{
							prioritizable = pickupableGo.GetComponent<Prioritizable>();
						}
					}

					if (prioritizable != null)
					{
						var newSetting = new PrioritySetting((PriorityScreen.PriorityClass)p.PriorityClass, p.PriorityValue);
						// Only update if different to avoid event spam
						if (!prioritizable.GetMasterPriority().Equals(newSetting))
						{
							prioritizable.SetMasterPriority(newSetting);
						}
					}
				}
			}
			finally
			{
				IsApplying = false;
			}

			// If host received from client, rebroadcast to all other clients
			if (MultiplayerSession.IsHost && Priorities.Count > 0)
			{
				PacketSender.SendToAllClients(this);
			}
		}
	}
}
