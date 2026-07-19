using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Synchronization;
using System;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class ResourceCountPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{

		// Using a dictionary is heavy, so let's Serialize a list of tag hashes/names and amounts.
		// Tag (string) -> Amount (float)
		public Dictionary<string, float> Resources = new Dictionary<string, float>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Resources.Count);
			foreach (var kvp in Resources)
			{
				writer.Write(kvp.Key);
				writer.Write(kvp.Value);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			Resources.Clear();
			for (int i = 0; i < count; i++)
			{
				string key = reader.ReadString();
				float val = reader.ReadSingle();
				Resources[key] = val;
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost) return;
			Apply();
		}

		private void Apply()
		{
			using var _ = Profiler.Scope();

			// Update local cache for the patch to use
			ResourceSyncer.ClientResources = Resources;

			// Ensure these resources are "Discovered" so they show up in the UI list
			if (DiscoveredResources.Instance != null)
			{
				foreach (var kvp in Resources)
				{
					Tag tag = TagManager.Create(kvp.Key);
					// DiscoveredResources.Instance.Discover(tag);
					// To avoid spamming notifications or issues, we can check if already discovered.
					if (!DiscoveredResources.Instance.IsDiscovered(tag))
					{
						try
						{
							DiscoveredResources.Instance.Discover(tag);
						}
						catch (Exception ex)
						{
							DebugConsole.LogError($"[ResourceCountPacket] Error discovering resource: {ex}");
						}
					}
				}
			}
		}
	}
}
