using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Aquatic
{
	internal static class OxyCoralSync
	{
		private static readonly Dictionary<(int, int), long> HostSequences = new();

		public static void ResetSessionState() => HostSequences.Clear();

		internal static long NextSequence(int worldId, int sourceCell)
		{
			var key = (worldId, sourceCell);
			HostSequences.TryGetValue(key, out long previous);
			long sequence = previous == long.MaxValue ? 1 : previous + 1;
			HostSequences[key] = sequence;
			return sequence;
		}

		internal static bool ShouldRunProduction(bool inSession, bool isHost)
			=> !inSession || isHost;

		internal static void ProduceAndBroadcast(OxyCoral.Instance coral, float dt)
		{
			if (coral == null || dt <= 0f || coral.def.OutputBubbleCells == null ||
			    coral.def.OutputBubbleCells.Length == 0)
				return;
			int sourceCell = Grid.PosToCell(coral);
			if (!Grid.IsValidCell(sourceCell))
				return;
			int index = Random.Range(0, coral.def.OutputBubbleCells.Length);
			int outputCell = Grid.OffsetCell(sourceCell, coral.def.OutputBubbleCells[index]);
			float rate = coral.IsWild ? coral.def.OxygenProductionRate * 0.25f :
				coral.def.OxygenProductionRate;
			float mass = rate * dt;
			if (!Grid.IsValidCell(outputCell) || mass < 1E-09f)
				return;

			float temperature = coral.GetComponent<PrimaryElement>().Temperature;
			Traverse.Create(coral).Method("CreateOxygenBubble", outputCell, mass).GetValue();
			int worldId = coral.gameObject.GetMyWorldId();
			PacketSender.SendToAllClients(new OxyCoralBubblePacket
			{
				WorldId = worldId,
				SourceCell = sourceCell,
				OutputCell = outputCell,
				Sequence = NextSequence(worldId, sourceCell),
				Mass = mass,
				Temperature = temperature
			}, PacketSendMode.ReliableImmediate);
		}
	}

	[HarmonyPatch(typeof(OxyCoral.Instance), nameof(OxyCoral.Instance.ProduceOxygenUpdate), typeof(float))]
	internal static class OxyCoralProduceOxygenPatch
	{
		internal static bool Prefix(OxyCoral.Instance __instance, float dt)
		{
			if (!MultiplayerSession.InSession)
				return true;
			if (MultiplayerSession.IsHost)
				OxyCoralSync.ProduceAndBroadcast(__instance, dt);
			return false;
		}
	}
}
