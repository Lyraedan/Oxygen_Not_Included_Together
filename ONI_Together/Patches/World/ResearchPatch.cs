using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using System.Collections.Generic;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(Research), "SetActiveResearch")]
	public static class ResearchPatch
	{
		public static void Postfix(Research __instance, Tech tech, bool clearQueue)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost) return;

			// Don't send packets if we're applying state from a received packet (prevents loop)
			if (ResearchStatePacket.IsApplying) return;

			string activeTechId = tech != null ? tech.Id : string.Empty;

			// Build complete packet with queue info
			var packet = new ResearchStatePacket
			{
				ActiveTechId = activeTechId,
				UnlockedTechIds = new List<string>(),
				QueuedTechIds = new List<string>()
			};

			// Populate the research queue
			try
			{
				var queueField = HarmonyLib.AccessTools.Field(typeof(Research), "queuedTech");
				if (queueField != null)
				{
					var queue = queueField.GetValue(Research.Instance) as System.Collections.IList;
					if (queue != null)
					{
						foreach (var item in queue)
						{
							var techInstance = item as TechInstance;
							if (techInstance?.tech != null)
							{
								packet.QueuedTechIds.Add(techInstance.tech.Id);
							}
						}
					}
				}
			}
			catch { }

			PacketSender.SendToAllClients(packet);
		}
	}
}

