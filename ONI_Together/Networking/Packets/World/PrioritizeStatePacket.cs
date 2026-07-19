using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using ONI_Together.Networking.Packets.Tools.Prioritize;

namespace ONI_Together.Networking.Packets.World
{
	public class PrioritizeStatePacket : IPacket, IBulkablePacket, IHostOnlyPacket
	{
		internal const int MaxPriorityCount = 256;
		public struct PriorityData
		{
			public int NetId;
			public int PriorityClass;
			public int PriorityValue;
		}

		public List<PriorityData> Priorities = new List<PriorityData>();
		public static bool IsApplying = false;
		public int MaxPackSize => 256;
		public uint IntervalMs => 50;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			if (Priorities == null || Priorities.Count > MaxPriorityCount)
				throw new InvalidDataException("Invalid priority state count");
			writer.Write(Priorities.Count);
			foreach (var p in Priorities)
			{
				Validate(p);
				writer.Write(p.NetId);
				writer.Write(p.PriorityClass);
				writer.Write(p.PriorityValue);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			if (count < 0 || count > MaxPriorityCount)
				throw new InvalidDataException($"Invalid priority state count: {count}");
			Priorities = new List<PriorityData>(count);
			for (int i = 0; i < count; i++)
			{
				var priority = new PriorityData
				{
					NetId = reader.ReadInt32(),
					PriorityClass = reader.ReadInt32(),
					PriorityValue = reader.ReadInt32()
				};
				Validate(priority);
				Priorities.Add(priority);
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				return;

			try
			{
				IsApplying = true;
				foreach (var p in Priorities)
				{
					if (NetworkIdentityRegistry.TryGet(p.NetId, out var identity) && identity != null)
					{
						var prioritizable = identity.GetComponent<Prioritizable>();
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
			}
			finally
			{
				IsApplying = false;
			}
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		private static void Validate(PriorityData priority)
		{
			if (priority.NetId == 0 ||
			    !PriorityAuthority.IsValidStatePriority(priority.PriorityClass, priority.PriorityValue))
				throw new InvalidDataException("Invalid priority state entry");
		}
	}
}
