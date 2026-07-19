using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Frosty;
using ONI_Together.Networking.Packets.DLC.Prehistoric;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Patches.DLC.Frosty;
using ONI_Together.Patches.DLC.Prehistoric;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class DynamicWorldDlcSyncTests
	{
		[UnitTest(name: "Prehistoric vine graph is bounded host state", category: "Sync")]
		public static UnitTestResult VineGraphRoundtrip()
		{
			var packet = new VineBranchStatePacket
			{
				MotherNetId = -101,
				PreviousNetId = 0,
				BranchNetId = 102,
				PrefabHash = VineBranchConfig.ID.GetHashCode(),
				Position = new Vector3(10f, 11f, 0f),
				MotherSide = VineMotherSide.Left,
				Shape = VineBranch.Shape.InCornerTopLeft,
				RootShape = VineBranch.Shape.Left,
				RootDirection = Direction.Right,
				BranchNumber = 1,
				GrowingClockwise = true,
				WildPlanted = false,
				Growth = 0.625f,
				FruitGrowth = 0.25f,
				OldAge = 0.125f
			};
			VineBranchStatePacket copy = Roundtrip(packet, new VineBranchStatePacket());
			if (copy is not IHostOnlyPacket || copy.MotherNetId != -101 || copy.BranchNetId != 102 ||
			    copy.PreviousNetId != 0 || copy.MotherSide != VineMotherSide.Left ||
			    copy.Shape != VineBranch.Shape.InCornerTopLeft || copy.RootDirection != Direction.Right ||
			    copy.BranchNumber != 1 || copy.Growth != 0.625f || copy.FruitGrowth != 0.25f ||
			    copy.OldAge != 0.125f)
				return UnitTestResult.Fail("Vine relationship or growth did not roundtrip");
			if (!VineBranchStatePacket.ShouldApply(false, true) ||
			    VineBranchStatePacket.ShouldApply(true, true) || VineBranchStatePacket.ShouldApply(false, false) ||
			    !VineBranchSync.ShouldRunGameplay(false, false, false) ||
			    !VineBranchSync.ShouldRunGameplay(true, true, false) ||
			    VineBranchSync.ShouldRunGameplay(true, false, false) ||
			    !VineBranchSync.ShouldRunGameplay(true, false, true))
				return UnitTestResult.Fail("Vine authority gate is incorrect");
			packet.BranchNumber = 13;
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Out-of-range vine branch number was accepted");
			return UnitTestResult.Pass("Vine graph identity, shape, direction and growth are bounded");
		}

		[UnitTest(name: "Carnivorous plant victim outcome is host state", category: "Sync")]
		public static UnitTestResult CarnivorousVictimRoundtrip()
		{
			var packet = new CarnivorousPlantStatePacket
			{
				Kind = CarnivorousPlantKind.Flytrap,
				PlantNetId = -201,
				VictimNetId = 202,
				HasEatenCreature = true,
				LastConsumedPrefabId = "Puft"
			};
			CarnivorousPlantStatePacket copy = Roundtrip(packet, new CarnivorousPlantStatePacket());
			if (copy is not IHostOnlyPacket || copy.Kind != CarnivorousPlantKind.Flytrap ||
			    copy.PlantNetId != -201 || copy.VictimNetId != 202 || !copy.HasEatenCreature ||
			    copy.LastConsumedPrefabId != "Puft")
				return UnitTestResult.Fail("Carnivorous victim outcome did not roundtrip");
			if (!CarnivorousPlantSync.ShouldRunGameplay(false, false, false) ||
			    !CarnivorousPlantSync.ShouldRunGameplay(true, true, false) ||
			    CarnivorousPlantSync.ShouldRunGameplay(true, false, false) ||
			    !CarnivorousPlantSync.ShouldRunGameplay(true, false, true))
				return UnitTestResult.Fail("Carnivorous plant authority gate is incorrect");
			packet.HasEatenCreature = false;
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Cleared carnivorous state accepted a victim");
			return UnitTestResult.Pass("Victim selection, despawn and eaten state are bounded host outcomes");
		}

		[UnitTest(name: "Critter trap gas outcome is replay safe", category: "Sync")]
		public static UnitTestResult CritterTrapGasRoundtrip()
		{
			var packet = new CritterTrapGasPacket
			{
				PlantNetId = -301,
				Sequence = 4,
				Cell = 100,
				Element = SimHashes.Methane,
				Mass = 2.5f,
				Temperature = 305f,
				DiseaseIndex = byte.MaxValue,
				DiseaseCount = 0
			};
			CritterTrapGasPacket copy = Roundtrip(packet, new CritterTrapGasPacket());
			if (copy is not IHostOnlyPacket || copy.PlantNetId != -301 || copy.Sequence != 4 ||
			    copy.Cell != 100 || copy.Element != SimHashes.Methane || copy.Mass != 2.5f)
				return UnitTestResult.Fail("Critter trap gas outcome did not roundtrip");
			if (!CritterTrapGasSync.IsNewSequence(3, 4) || CritterTrapGasSync.IsNewSequence(4, 4) ||
			    CritterTrapGasSync.IsNewSequence(5, 4))
				return UnitTestResult.Fail("Critter trap gas replay gate is not monotonic");
			packet.Mass = float.NaN;
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Non-finite gas mass was accepted");
			return UnitTestResult.Pass("Gas cell outcome is bounded and sequence-idempotent");
		}

		[UnitTest(name: "Spaced Out plant mutation is absolute host state", category: "Sync")]
		public static UnitTestResult PlantMutationRoundtrip()
		{
			var packet = new PlantMutationStatePacket
			{
				PlantNetId = -401,
				SpeciesHash = 402,
				SubSpeciesHash = 403,
				Analyzed = true,
				MutationIds = new List<string> { "moderatelyLoose" }
			};
			PlantMutationStatePacket copy = Roundtrip(packet, new PlantMutationStatePacket());
			if (copy is not IHostOnlyPacket || copy.PlantNetId != -401 || copy.SpeciesHash != 402 ||
			    copy.SubSpeciesHash != 403 || !copy.Analyzed || copy.MutationIds.Count != 1 ||
			    copy.MutationIds[0] != "moderatelyLoose")
				return UnitTestResult.Fail("Plant mutation state did not roundtrip");
			if (!PlantMutationSync.ShouldRunMutation(false, false, false) ||
			    !PlantMutationSync.ShouldRunMutation(true, true, false) ||
			    PlantMutationSync.ShouldRunMutation(true, false, false) ||
			    !PlantMutationSync.ShouldRunMutation(true, false, true))
				return UnitTestResult.Fail("Plant mutation authority gate is incorrect");
			packet.MutationIds.Add("tooMany");
			if (packet.IsWireValid())
				return UnitTestResult.Fail("More than one mutation was accepted");
			return UnitTestResult.Pass("Mutation IDs, analyzed flag and subspecies are absolute host state");
		}

		[UnitTest(name: "Frosty seeded comet impact is exact host outcome", category: "Sync")]
		public static UnitTestResult SpaceTreeCometRoundtrip()
		{
			var packet = new SpaceTreeImpactPacket
			{
				CometNetId = -501,
				Sequence = 2,
				Element = SimHashes.Regolith,
				MassPerCell = 25f,
				Temperature = 900f,
				DiseaseIndex = byte.MaxValue,
				DiseaseCountPerCell = 0,
				TreeImpactCell = 120,
				TreeTileMaxHeight = 3,
				Cells = new List<int> { 120, 121, 122 }
			};
			SpaceTreeImpactPacket copy = Roundtrip(packet, new SpaceTreeImpactPacket());
			if (copy is not IHostOnlyPacket || copy.CometNetId != -501 || copy.Sequence != 2 ||
			    copy.Element != SimHashes.Regolith || copy.Cells.Count != 3 || copy.TreeImpactCell != 120)
				return UnitTestResult.Fail("Seeded comet impact did not roundtrip");
			if (!SpaceTreeSeededCometSync.ShouldRunGameplay(false, false, false) ||
			    !SpaceTreeSeededCometSync.ShouldRunGameplay(true, true, false) ||
			    SpaceTreeSeededCometSync.ShouldRunGameplay(true, false, false) ||
			    !SpaceTreeSeededCometSync.ShouldRunGameplay(true, false, true) ||
			    !SpaceTreeSeededCometSync.IsNewSequence(1, 2) || SpaceTreeSeededCometSync.IsNewSequence(2, 2))
				return UnitTestResult.Fail("Seeded comet authority or replay gate is incorrect");
			if (SpaceTreeSeededCometSync.SelectTreeImpactCell(new[] { 10, 11 }, 4, 0.2f) != 10 ||
			    SpaceTreeSeededCometSync.SelectTreeImpactCell(new[] { 10, 11 }, 4, 0.3f) != 11 ||
			    SpaceTreeSeededCometSync.SelectTreeImpactCell(new[] { 10, 11 }, 4, 0.9f) != -1)
				return UnitTestResult.Fail("Seeded comet random tree cell selection diverged from build 740622");
			packet.Cells.Add(-1);
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Invalid impact cell was accepted");
			return UnitTestResult.Pass("Random impact cells, substance and tree outcome are bounded");
		}

		[UnitTest(name: "Dynamic DLC Harmony targets match build 740622", category: "Sync")]
		public static UnitTestResult HarmonyTargets()
		{
			if (!Matches(typeof(VineMother.Instance), nameof(VineMother.Instance.AttemptToSpawnBranches), typeof(void)) ||
			    !Matches(typeof(VineBranch.Instance), nameof(VineBranch.Instance.AttemptToSpawnBranch), typeof(void)) ||
			    !Matches(typeof(VineBranch.Instance), nameof(VineBranch.Instance.RecalculateMyShape), typeof(void)) ||
			    !Matches(typeof(VineBranch), "RefreshPositionPercent", typeof(void), typeof(VineBranch.Instance), typeof(float)) ||
			    !Matches(typeof(FlytrapConsumptionMonitor.Instance), nameof(FlytrapConsumptionMonitor.Instance.OnPickupableLayerObjectDetected), typeof(void), typeof(object)) ||
			    !Matches(typeof(TrapTrigger), nameof(TrapTrigger.OnCreatureOnTrap), typeof(void), typeof(object)) ||
			    !Matches(typeof(CritterTrapPlant.StatesInstance), nameof(CritterTrapPlant.StatesInstance.AddGas), typeof(void), typeof(float)) ||
			    !Matches(typeof(CritterTrapPlant.StatesInstance), nameof(CritterTrapPlant.StatesInstance.VentGas), typeof(void)) ||
			    !Matches(typeof(MutantPlant), nameof(MutantPlant.Mutate), typeof(void)) ||
			    !Matches(typeof(MutantPlant), nameof(MutantPlant.Analyze), typeof(void)) ||
			    !Matches(typeof(SpaceTreeSeededComet), "DepositTiles", typeof(void), typeof(int), typeof(Element), typeof(int), typeof(int), typeof(float)) ||
			    !Matches(typeof(SpaceTreeSeededComet), "PlantTreeOnSolidTileCreated", typeof(void), typeof(int), typeof(int)))
				return UnitTestResult.Fail("A dynamic DLC Harmony target signature changed");
			return UnitTestResult.Pass("All dynamic DLC Harmony targets match build 740622");
		}

		private static T Roundtrip<T>(T input, T output) where T : IPacket
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			if (stream.Position != stream.Length)
				throw new InvalidDataException("Packet left unread bytes");
			return output;
		}

		private static bool Matches(Type type, string name, Type returnType, params Type[] parameters)
		{
			MethodInfo method = AccessTools.Method(type, name, parameters);
			return method != null && method.ReturnType == returnType;
		}
	}
}
