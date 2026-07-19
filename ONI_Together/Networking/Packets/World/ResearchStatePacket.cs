using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class ResearchStatePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		internal const int MaxTechCount = 4096;
		private const int MaxTechIdLength = 256;
		public List<string> UnlockedTechIds = new List<string>();
		public List<string> QueuedTechIds = new List<string>(); // Full queue from host
		public string ActiveTechId; // Current research selection

		// Flag to prevent infinite loop: when applying state, don't send new packets
		public static bool IsApplying = false;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(UnlockedTechIds.Count);
			foreach (var id in UnlockedTechIds)
			{
				writer.Write(id);
			}

			writer.Write(QueuedTechIds.Count);
			foreach (var id in QueuedTechIds)
			{
				writer.Write(id);
			}

			writer.Write(ActiveTechId ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			if (count < 0 || count > MaxTechCount)
				throw new InvalidDataException($"Invalid unlocked tech count: {count}");
			UnlockedTechIds = new List<string>(count);
			for (int i = 0; i < count; i++)
			{
				UnlockedTechIds.Add(ReadTechId(reader));
			}

			int queueCount = reader.ReadInt32();
			if (queueCount < 0 || queueCount > MaxTechCount)
				throw new InvalidDataException($"Invalid queued tech count: {queueCount}");
			QueuedTechIds = new List<string>(queueCount);
			for (int i = 0; i < queueCount; i++)
			{
				QueuedTechIds.Add(ReadTechId(reader));
			}

			ActiveTechId = ReadTechId(reader);
		}

		private static string ReadTechId(BinaryReader reader)
		{
			string value = reader.ReadString();
			if (value.Length > MaxTechIdLength)
				throw new InvalidDataException("Research tech ID is too long");
			return value;
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;

			// Set flag to prevent ResearchPatch from sending packets while we apply state
			IsApplying = true;
			try
			{
				ProcessResearchState(UnlockedTechIds, QueuedTechIds, ActiveTechId);
			}
			finally
			{
				IsApplying = false;
			}
		}

		private void ProcessResearchState(List<string> unlockedIds, List<string> queuedIds, string activeTechId)
		{
			using var _ = Profiler.Scope();

			if (Research.Instance == null) return;

			try
			{
				// Get the ResearchScreen for visual updates (use Traverse since field is not public)
				object researchScreen = null;
				if (ManagementMenu.Instance != null)
				{
					researchScreen = HarmonyLib.Traverse.Create(ManagementMenu.Instance)
						.Field("researchScreen")
						.GetValue();
				}

				// First, explicitly clear the visual state for all queued research
				try
				{
					var queueField = HarmonyLib.AccessTools.Field(typeof(Research), "queuedTech");
					if (queueField != null)
					{
						var localQueue = queueField.GetValue(Research.Instance) as System.Collections.IList;
						if (localQueue != null && localQueue.Count > 0)
						{
							// Log and deselect visually
							var techNames = new List<string>();
							foreach (var item in localQueue)
							{
								var techInstance = item as TechInstance;
								if (techInstance?.tech != null)
								{
									techNames.Add(techInstance.tech.Id);

									// Deselect visually using ResearchScreen
									if (researchScreen != null)
									{
										try
										{
											HarmonyLib.Traverse.Create(researchScreen)
												.Method("SelectAllEntries", new Type[] { typeof(Tech), typeof(bool) })
												.GetValue(techInstance.tech, false);
										}
										catch (Exception ex) { DebugConsole.LogError($"[ResearchStatePacket] Error deselecting entry: {ex}"); }
									}
								}
							}
							DebugConsole.Log($"[ResearchLog] Clearing queue of {localQueue.Count} items: {string.Join(", ", techNames)}");

							// Clear the queue
							localQueue.Clear();
						}
					}
				}
				catch (Exception ex)
				{
					DebugConsole.LogWarning($"[ResearchLog] Failed to clear queue: {ex}");
				}

				// Now set the host's active research
				if (!string.IsNullOrEmpty(activeTechId))
				{
					var tech = Db.Get().Techs.Get(activeTechId);
					if (tech != null)
					{
						DebugConsole.Log($"[ResearchLog] Setting active research to: {tech.Name}");
						Research.Instance.SetActiveResearch(tech, true);

						// Select visually using ResearchScreen
						if (researchScreen != null)
						{
							try
							{
								HarmonyLib.Traverse.Create(researchScreen)
									.Method("SelectAllEntries", new Type[] { typeof(Tech), typeof(bool) })
									.GetValue(tech, true);
							}
							catch (Exception ex) { DebugConsole.LogError($"[ResearchStatePacket] Error selecting entry: {ex}"); }
						}
					}
				}

				// Sync unlocked techs
				int unlockedCount = 0;
				foreach (var techId in unlockedIds)
				{
					var tech = Db.Get().Techs.Get(techId);
					if (tech == null) continue;

					var techInst = Research.Instance.Get(tech);
					if (techInst != null && !techInst.IsComplete())
					{
						techInst.Purchased();

						// Trigger the game event to notify all listeners (PlanScreen, etc.)
						try
						{
							Game.Instance?.Trigger((int)GameHashes.ResearchComplete, tech);
						}
						catch (Exception ex) { DebugConsole.LogError($"[ResearchStatePacket] Error triggering ResearchComplete: {ex}"); }

						unlockedCount++;
					}
				}

				if (unlockedCount > 0)
				{
					DebugConsole.Log($"[Client] Synced {unlockedCount} unlocked technologies from host.");
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[ResearchStatePacket] Failed to process research state: {ex}");
			}
		}
	}
}
