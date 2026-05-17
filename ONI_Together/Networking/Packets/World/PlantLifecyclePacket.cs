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

	public class PlantLifecyclePacket : IPacket
	{
		public PlantLifecycleOperation Operation;
		public PlantData Plant;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write((byte)Operation);
			writer.Write(Plant.PlantNetId);
			writer.Write(Plant.ReceptacleNetId);
			writer.Write(Plant.Cell);
			writer.Write(Plant.PlantPrefabTag ?? string.Empty);
			writer.Write(Plant.Maturity);
			writer.Write(Plant.IsWilting);
			writer.Write(Plant.IsHarvestReady);
			writer.Write(Plant.IsWild);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Operation = (PlantLifecycleOperation)reader.ReadByte();
			Plant = new PlantData
			{
				PlantNetId = reader.ReadInt32(),
				ReceptacleNetId = reader.ReadInt32(),
				Cell = reader.ReadInt32(),
				PlantPrefabTag = reader.ReadString(),
				Maturity = reader.ReadSingle(),
				IsWilting = reader.ReadBoolean(),
				IsHarvestReady = reader.ReadBoolean(),
				IsWild = reader.ReadBoolean()
			};
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
