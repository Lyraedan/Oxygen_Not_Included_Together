using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.Frosty;
using UnityEngine;

namespace ONI_Together.Patches.DLC.Frosty
{
	internal static class SpaceTreeSeededCometSync
	{
		private static readonly Dictionary<int, int> HostSequences = new();
		private static readonly MethodInfo PlantTreeMethod = AccessTools.Method(
			typeof(SpaceTreeSeededComet), "PlantTreeOnSolidTileCreated");

		public static void ResetSessionState() => HostSequences.Clear();

		internal static int NextSequence(int cometNetId)
		{
			HostSequences.TryGetValue(cometNetId, out int previous);
			int next = previous == int.MaxValue ? 1 : previous + 1;
			HostSequences[cometNetId] = next;
			return next;
		}

		internal static bool ShouldRunGameplay(bool inSession, bool isHost, bool applying)
			=> applying || !inSession || isHost;

		internal static bool IsNewSequence(int previous, int candidate) => candidate > previous;

		internal static bool TryCapture(SpaceTreeSeededComet comet, int cell, Element element,
			int world, int previousCell, float temperature, out SpaceTreeImpactPacket packet)
		{
			packet = null;
			if (comet == null || element == null)
				return false;
			NetworkIdentity identity = comet.gameObject.AddOrGet<NetworkIdentity>();
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			int depth = Traverse.Create(comet).Method("GetDepthOfElement", cell, element, world).GetValue<int>();
			float ratio = (float)(depth - comet.addTilesMinHeight) /
			              (comet.addTilesMaxHeight - comet.addTilesMinHeight);
			float scale = float.IsNaN(ratio) ? 1f : 1f - ratio;
			int tileCount = Mathf.Min(comet.addTiles,
				Mathf.Clamp(Mathf.RoundToInt(comet.addTiles * scale), 1, comet.addTiles));
			if (tileCount <= 0)
				return false;

			var cells = new List<int>(tileCount);
			ListPool<int, Comet>.PooledList viable = ListPool<int, Comet>.Allocate();
			FloodFill.BreadthCollect(previousCell, candidate =>
				!Grid.IsValidCellInWorld(candidate, world) || Grid.Solid[candidate]
					? FloodFill.BoundaryCheckResult.Halt
					: FloodFill.BoundaryCheckResult.Continue, viable, 11);
			for (int i = 0; i < viable.Count && cells.Count < tileCount; i++)
				cells.Add(viable[i]);
			viable.Recycle();
			if (cells.Count == 0)
				return false;

			UnityEngine.Random.State randomState = UnityEngine.Random.state;
			float randomValue = UnityEngine.Random.value;
			UnityEngine.Random.state = randomState;
			int treeImpactCell = SelectTreeImpactCell(cells, tileCount, randomValue);
			packet = new SpaceTreeImpactPacket
			{
				CometNetId = identity.NetId,
				Sequence = NextSequence(identity.NetId),
				Element = element.id,
				MassPerCell = Traverse.Create(comet).Field("addTileMass").GetValue<float>() / comet.addTiles,
				Temperature = temperature,
				DiseaseIndex = comet.diseaseIdx,
				DiseaseCountPerCell = comet.addDiseaseCount / tileCount,
				TreeImpactCell = treeImpactCell,
				TreeTileMaxHeight = comet.addTilesMaxHeight,
				Cells = cells
			};
			return packet.IsWireValid();
		}

		internal static int SelectTreeImpactCell(IReadOnlyList<int> cells, int tileCount, float randomValue)
		{
			if (cells == null || tileCount <= 0 || randomValue < 0f || randomValue > 1f)
				return -1;
			for (int i = 0; i < cells.Count; i++)
			{
				if (randomValue <= (float)(i + 1) / tileCount)
					return cells[i];
			}
			return -1;
		}

		internal static void Apply(SpaceTreeImpactPacket packet)
		{
			if (packet == null || !packet.IsWireValid())
				return;
			foreach (int cell in packet.Cells)
			{
				int callback = cell == packet.TreeImpactCell
					? Game.Instance.callbackManager.Add(new Game.CallbackInfo(
						() => PlantTreeMethod?.Invoke(null, new object[] { cell, packet.TreeTileMaxHeight }))).index
					: -1;
				SimMessages.AddRemoveSubstance(cell, packet.Element, CellEventLogger.Instance.ElementEmitted,
					packet.MassPerCell, packet.Temperature, packet.DiseaseIndex,
					packet.DiseaseCountPerCell, true, callback);
			}
		}
	}

	[HarmonyPatch(typeof(SpaceTreeSeededComet), "DepositTiles")]
	internal static class SpaceTreeSeededCometDepositPatch
	{
		internal static bool Prefix(SpaceTreeSeededComet __instance, int cell, Element element,
			int world, int prev_cell, float temperature)
		{
			if (!SpaceTreeSeededCometSync.ShouldRunGameplay(
			    MultiplayerSession.InSession, MultiplayerSession.IsHost, false))
				return false;
			if (MultiplayerSession.IsHostInSession && SpaceTreeSeededCometSync.TryCapture(
			    __instance, cell, element, world, prev_cell, temperature, out SpaceTreeImpactPacket packet))
				PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
			return true;
		}
	}
}
