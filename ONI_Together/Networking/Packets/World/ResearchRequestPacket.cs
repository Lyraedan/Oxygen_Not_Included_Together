using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class ResearchRequestPacket : IPacket
	{
		public string TechId { get; set; }

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(TechId ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			TechId = reader.ReadString();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost) return;

			// Host received request from client
			if (!string.IsNullOrEmpty(TechId))
			{
				var tech = Db.Get().Techs.TryGet(TechId);
				if (tech != null)
				{
					// Get the ResearchScreen for visual updates on host
					object researchScreen = null;
					if (ManagementMenu.Instance != null)
					{
						researchScreen = HarmonyLib.Traverse.Create(ManagementMenu.Instance)
							.Field("researchScreen")
							.GetValue();
					}

					// First, deselect all current queue items visually
					try
					{
						var queueField = HarmonyLib.AccessTools.Field(typeof(Research), "queuedTech");
						if (queueField != null)
						{
							var localQueue = queueField.GetValue(Research.Instance) as System.Collections.IList;
							if (localQueue != null && localQueue.Count > 0 && researchScreen != null)
							{
								foreach (var item in localQueue)
								{
									var techInstance = item as TechInstance;
									if (techInstance?.tech != null)
									{
										try
										{
											HarmonyLib.Traverse.Create(researchScreen)
												.Method("SelectAllEntries", new Type[] { typeof(Tech), typeof(bool) })
												.GetValue(techInstance.tech, false);
										}
										catch (Exception ex) { DebugConsole.LogError($"[ResearchRequestPacket] Error deselecting entry: {ex}"); }
									}
								}
							}
						}
					}
					catch (Exception ex) { DebugConsole.LogError($"[ResearchRequestPacket] Error clearing queue visuals: {ex}"); }

					// Set the new research
					Research.Instance.SetActiveResearch(tech, true);

					// Select the new research visually on host
					if (researchScreen != null)
					{
						try
						{
							HarmonyLib.Traverse.Create(researchScreen)
								.Method("SelectAllEntries", new Type[] { typeof(Tech), typeof(bool) })
								.GetValue(tech, true);
						}
						catch (Exception ex) { DebugConsole.LogError($"[ResearchRequestPacket] Error selecting entry: {ex}"); }
					}

					// ResearchPatch will trigger and sync back to all clients
				}
			}
		}
	}
}

