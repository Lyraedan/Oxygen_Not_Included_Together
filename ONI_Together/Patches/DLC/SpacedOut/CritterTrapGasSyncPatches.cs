using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal struct CritterTrapGasCapture
	{
		internal int PlantNetId;
		internal int Cell;
		internal SimHashes Element;
		internal float Mass;
		internal float Temperature;
		internal byte DiseaseIndex;
		internal int DiseaseCount;
	}

	internal static class CritterTrapGasSync
	{
		private static readonly Dictionary<int, int> HostSequences = new();

		public static void ResetSessionState() => HostSequences.Clear();

		internal static int NextSequence(int plantNetId)
		{
			HostSequences.TryGetValue(plantNetId, out int previous);
			int next = previous == int.MaxValue ? 1 : previous + 1;
			HostSequences[plantNetId] = next;
			return next;
		}

		internal static bool IsNewSequence(int previous, int candidate) => candidate > previous;

		internal static bool TryCapture(CritterTrapPlant.StatesInstance smi, out CritterTrapGasCapture capture)
		{
			capture = default;
			if (smi?.gameObject == null)
				return false;
			Storage storage = smi.gameObject.GetComponent<Storage>();
			CritterTrapPlant plant = smi.gameObject.GetComponent<CritterTrapPlant>();
			PrimaryElement gas = storage?.FindPrimaryElement(plant?.outputElement ?? SimHashes.Vacuum);
			NetworkIdentity identity = smi.gameObject.AddOrGet<NetworkIdentity>();
			if (gas == null || plant == null || gas.Mass <= 0f)
				return false;
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			capture = new CritterTrapGasCapture
			{
				PlantNetId = identity.NetId,
				Cell = Grid.PosToCell(smi.transform.GetPosition()),
				Element = gas.ElementID,
				Mass = gas.Mass,
				Temperature = gas.Temperature,
				DiseaseIndex = gas.DiseaseIdx,
				DiseaseCount = gas.DiseaseCount
			};
			return capture.PlantNetId != 0 && capture.Cell >= 0;
		}

		internal static void Send(CritterTrapGasCapture capture)
		{
			if (!MultiplayerSession.IsHostInSession || capture.PlantNetId == 0)
				return;
			PacketSender.SendToAllClients(new CritterTrapGasPacket
			{
				PlantNetId = capture.PlantNetId,
				Sequence = NextSequence(capture.PlantNetId),
				Cell = capture.Cell,
				Element = capture.Element,
				Mass = capture.Mass,
				Temperature = capture.Temperature,
				DiseaseIndex = capture.DiseaseIndex,
				DiseaseCount = capture.DiseaseCount
			}, PacketSendMode.ReliableImmediate);
		}

		internal static void Apply(CritterTrapGasPacket packet)
		{
			if (packet == null || !packet.IsWireValid() || !Grid.IsValidCell(packet.Cell))
				return;
			SimMessages.AddRemoveSubstance(packet.Cell, packet.Element,
				CellEventLogger.Instance.Dumpable, packet.Mass, packet.Temperature,
				packet.DiseaseIndex, packet.DiseaseCount);
		}
	}

	[HarmonyPatch(typeof(CritterTrapPlant.StatesInstance), nameof(CritterTrapPlant.StatesInstance.AddGas))]
	internal static class CritterTrapAddGasPatch
	{
		internal static bool Prefix()
			=> !MultiplayerSession.InSession || !MultiplayerSession.IsClient;
	}

	[HarmonyPatch(typeof(CritterTrapPlant.StatesInstance), nameof(CritterTrapPlant.StatesInstance.VentGas))]
	internal static class CritterTrapVentGasPatch
	{
		internal static bool Prefix(CritterTrapPlant.StatesInstance __instance, ref CritterTrapGasCapture __state)
		{
			if (MultiplayerSession.InSession && MultiplayerSession.IsClient)
				return false;
			if (MultiplayerSession.IsHostInSession)
				CritterTrapGasSync.TryCapture(__instance, out __state);
			return true;
		}

		internal static void Postfix(CritterTrapGasCapture __state) => CritterTrapGasSync.Send(__state);
	}
}
