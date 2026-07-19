using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public enum PlantLifecycleOperation : byte
	{
		Spawn = 0,
		Remove = 1,
	}

	public class PlantLifecyclePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		public PlantLifecycleOperation Operation;
		public PlantData Plant;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write((byte)Operation);
			Plant.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Operation = (PlantLifecycleOperation)reader.ReadByte();
			if (Operation is not PlantLifecycleOperation.Spawn and not PlantLifecycleOperation.Remove)
				throw new InvalidDataException($"Invalid plant lifecycle operation: {Operation}");
			Plant = PlantData.Deserialize(reader);
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
				return;

			if (PlantGrowthSyncer.Instance?.OnPlantLifecycleReceived(this) == true)
				return;

			if (Game.Instance != null)
			{
				Game.Instance.StartCoroutine(RetryApply(Clone()));
			}
		}

		private PlantLifecyclePacket Clone()
		{
			return new PlantLifecyclePacket
			{
				Operation = Operation,
				Plant = Plant
			};
		}

		private static IEnumerator RetryApply(PlantLifecyclePacket packet)
		{
			for (int attempt = 0; attempt < 10; attempt++)
			{
				yield return null;

				if (!MultiplayerSession.InSession || MultiplayerSession.IsHost)
					yield break;

				if (PlantGrowthSyncer.Instance?.OnPlantLifecycleReceived(packet) == true)
					yield break;
			}

			DebugConsole.LogWarning($"[PlantLifecyclePacket] Failed to apply {packet.Operation} for plant {packet.Plant.PlantPrefabTag} at cell {packet.Plant.Cell}");
		}
	}
}
