using System;
using System.IO;
using System.Reflection;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using ONI_Together.Patches.DLC.Aquatic;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class AquaticSyncTests
	{
		[UnitTest(name: "Aquatic gameplay accepts only host authority", category: "Sync")]
		public static UnitTestResult HostAuthority()
		{
			if (!AquaticSync.ShouldRunAuthoritativeGameplay(false, false) ||
			    !AquaticSync.ShouldRunAuthoritativeGameplay(true, true) ||
			    AquaticSync.ShouldRunAuthoritativeGameplay(true, false))
				return UnitTestResult.Fail("Aquatic gameplay authority gate is incorrect");
			if (new AquaticShearingOutcomePacket() is not IHostOnlyPacket ||
			    new UnderwaterVentStatePacket() is not IHostOnlyPacket ||
			    new UnderwaterDrillStatePacket() is not IHostOnlyPacket ||
			    new SeaTreeBranchStatePacket() is not IHostOnlyPacket ||
			    new PunchClamStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("An aquatic authority packet is not host-only");
			if (!AquaticShearingOutcomePacket.ShouldApply(false, true) ||
			    AquaticShearingOutcomePacket.ShouldApply(true, true) ||
			    AquaticShearingOutcomePacket.ShouldApply(false, false) ||
			    !UnderwaterVentStatePacket.ShouldApply(false, true) ||
			    UnderwaterVentStatePacket.ShouldApply(true, true) ||
			    UnderwaterVentStatePacket.ShouldApply(false, false) ||
			    !UnderwaterDrillStatePacket.ShouldApply(false, true) ||
			    UnderwaterDrillStatePacket.ShouldApply(true, true) ||
			    UnderwaterDrillStatePacket.ShouldApply(false, false) ||
			    !SeaTreeBranchStatePacket.ShouldApply(false, true) ||
			    SeaTreeBranchStatePacket.ShouldApply(true, true) ||
			    !PunchClamStatePacket.ShouldApply(false, true) ||
			    PunchClamStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Aquatic packet authority gate is incorrect");

			return UnitTestResult.Pass("Only the host advances aquatic gameplay and clients accept host state");
		}

		[UnitTest(name: "Sea tree and punch clam metadata targets match game assembly", category: "Sync")]
		public static UnitTestResult SeaTreeAndPunchClamMetadata()
		{
			if (!HasInstanceMethod(typeof(SeaTreeRoot.Instance), "AttemptToSpawnBranches", Type.EmptyTypes) ||
			    !HasInstanceMethod(typeof(SeaTreeBranch.Instance), "AttemptToSpawnBranch", Type.EmptyTypes) ||
			    !HasInstanceMethod(typeof(SeaTreeBranch.Instance), "OnSpawnedByDiscovery", new[] { typeof(object) }) ||
			    !HasInstanceMethod(typeof(SeaTreeBranch.Instance), "SpawnCritter", Type.EmptyTypes) ||
			    !HasInstanceMethod(typeof(PunchClamMonitor.Instance), "SearchForClam", new[] { typeof(float) }) ||
			    !HasInstanceMethod(typeof(PunchClamOpenStates.Instance), "OpenClam", Type.EmptyTypes) ||
			    !HasInstanceMethod(typeof(ClamHarvestable), "PunchOpen", Type.EmptyTypes) ||
			    !HasInstanceMethod(typeof(ClamHarvestable), "OnCompleteWork", new[] { typeof(WorkerBase) }))
				return UnitTestResult.Fail("A sea tree or punch clam Harmony target changed");

			if (!SameDlc(new SeaTreeRootConfig().GetRequiredDlcIds(), DlcManager.DLC5) ||
			    !SameDlc(new SeaTreeBranchConfig().GetRequiredDlcIds(), DlcManager.DLC5) ||
			    !SameDlc(new ClamConfig().GetRequiredDlcIds(), DlcManager.DLC5))
				return UnitTestResult.Fail("A sea tree or punch clam target is no longer DLC5-only");

			return UnitTestResult.Pass("Sea tree and punch clam targets match the installed DLC5 assembly");
		}

		[UnitTest(name: "Sea tree and punch clam state packets roundtrip", category: "Sync")]
		public static UnitTestResult SeaTreeAndPunchClamRoundtrip()
		{
			SeaTreeBranchStatePacket branch = Roundtrip(new SeaTreeBranchStatePacket
			{
				RootNetId = 21, PreviousNetId = 22, BranchNetId = 23, ChildNetId = 24,
				PrefabHash = 25, Position = new Vector3(1f, 2f, 3f),
				Maturity = 0.5f, FruitMaturity = 0.75f, OldAge = 12f, FruitSequence = 4
			}, new SeaTreeBranchStatePacket());
			if (branch.RootNetId != 21 || branch.PreviousNetId != 22 || branch.BranchNetId != 23 ||
			    branch.ChildNetId != 24 || branch.PrefabHash != 25 || branch.Position != new Vector3(1f, 2f, 3f) ||
			    branch.Maturity != 0.5f || branch.FruitMaturity != 0.75f || branch.OldAge != 12f ||
			    branch.FruitSequence != 4)
				return UnitTestResult.Fail("Sea tree branch state did not roundtrip");

			PunchClamStatePacket clam = Roundtrip(new PunchClamStatePacket
			{
				PuncherNetId = 31, TargetClamNetId = 32,
				HasClamState = true, ClamNetId = 32, HasBeenOpened = true
			}, new PunchClamStatePacket());
			if (clam.PuncherNetId != 31 || clam.TargetClamNetId != 32 || !clam.HasClamState ||
			    clam.ClamNetId != 32 || !clam.HasBeenOpened)
				return UnitTestResult.Fail("Punch clam state did not roundtrip");

			return UnitTestResult.Pass("Sea tree relation/outcome and punch clam absolute state roundtrip");
		}

		[UnitTest(name: "Sea tree and punch clam state application is idempotent", category: "Sync")]
		public static UnitTestResult SeaTreeAndPunchClamIdempotence()
		{
			var state = new SeaTreeAmounts(0.5f, 0.75f, 12f);
			if (SeaTreeBranchSync.NeedsAmountApply(state, state) ||
			    !SeaTreeBranchSync.NeedsAmountApply(state, new SeaTreeAmounts(1f, 0f, 0f)))
				return UnitTestResult.Fail("Sea tree absolute amounts are not idempotent");
			if (!SeaTreeBranchStatePacket.IsNewFruitSequence(4, 5) ||
			    SeaTreeBranchStatePacket.IsNewFruitSequence(4, 4) ||
			    SeaTreeBranchStatePacket.IsNewFruitSequence(4, 3))
				return UnitTestResult.Fail("Sea tree fruit outcome sequence is not idempotent");
			if (PunchClamSync.NeedsOpenedApply(true, true) || !PunchClamSync.NeedsOpenedApply(false, true))
				return UnitTestResult.Fail("Punch clam absolute state is not idempotent");

			return UnitTestResult.Pass("Repeated sea tree and punch clam packets cannot duplicate outcomes");
		}

		[UnitTest(name: "Aquatic Harmony targets and DLC guards match game metadata", category: "Sync")]
		public static UnitTestResult MetadataTargets()
		{
			MethodBase completion = AquaticSync.ResolveShearingCompletionMethod();
			if (completion is not MethodInfo completionMethod || completionMethod.ReturnType != typeof(void))
				return UnitTestResult.Fail("Underwater shearing completion target was not found");
			ParameterInfo[] parameters = completion.GetParameters();
			if (parameters.Length != 2 || parameters[0].ParameterType != typeof(GameObject) ||
			    parameters[1].ParameterType != typeof(WorkerBase))
				return UnitTestResult.Fail("Underwater shearing completion signature changed");
			MethodInfo fallerAdd = typeof(FallerComponents).GetMethod(
				nameof(FallerComponents.Add),
				new[] { typeof(GameObject), typeof(Vector2) });
			if (fallerAdd == null)
				return UnitTestResult.Fail("Faller initial-velocity capture target was not found");
			if (!SameDlc(new UnderwaterShearingStationConfig().GetRequiredDlcIds(), DlcManager.DLC5) ||
			    !SameDlc(new UnderwaterVentConfig().GetRequiredDlcIds(), DlcManager.DLC5) ||
			    !SameDlc(new UnderwaterVentDrillConfig().GetRequiredDlcIds(), DlcManager.DLC5))
				return UnitTestResult.Fail("An aquatic target is no longer guarded by DLC5");

			return UnitTestResult.Pass("Harmony targets and DLC5 guards match the installed game assembly");
		}

		[UnitTest(name: "Aquatic shearing outcome packet roundtrips", category: "Sync")]
		public static UnitTestResult ShearingPacketRoundtrip()
		{
			AquaticShearingOutcomePacket shear = Roundtrip(
				new AquaticShearingOutcomePacket
				{
					CritterNetId = 11,
					StationNetId = 12,
					AmountKind = AquaticShearableAmount.ElementGrowth,
					Growth = 0f,
					ProductNetId = 13,
					ProductVelocity = new Vector2(-0.5f, 3f)
				},
				new AquaticShearingOutcomePacket());
			if (shear.CritterNetId != 11 || shear.StationNetId != 12 ||
			    shear.AmountKind != AquaticShearableAmount.ElementGrowth || shear.Growth != 0f ||
			    shear.ProductNetId != 13 || shear.ProductVelocity != new Vector2(-0.5f, 3f))
				return UnitTestResult.Fail("Aquatic shearing outcome did not roundtrip");
			return UnitTestResult.Pass("Aquatic shearing outcome roundtrips exactly");
		}

		[UnitTest(name: "Aquatic absolute state packets roundtrip", category: "Sync")]
		public static UnitTestResult PacketRoundtrip()
		{
			UnderwaterVentStatePacket vent = Roundtrip(
				new UnderwaterVentStatePacket
				{
					WorldId = 2,
					Cell = 31,
					BuildUp = 0.5f,
					Phase = UnderwaterVentPhase.Erupting,
					BubbleSequence = 7,
					HasBubble = true,
					BubbleElement = SimHashes.Methane,
					BubblePosition = new Vector3(3f, 4f, -1f),
					BubbleMass = 0.25f,
					BubbleTemperature = 373.15f
				},
				new UnderwaterVentStatePacket());
			if (vent.WorldId != 2 || vent.Cell != 31 || vent.BuildUp != 0.5f ||
			    vent.Phase != UnderwaterVentPhase.Erupting || vent.BubbleSequence != 7 ||
			    !vent.HasBubble || vent.BubbleElement != SimHashes.Methane ||
			    vent.BubblePosition != new Vector3(3f, 4f, -1f) || vent.BubbleMass != 0.25f ||
			    vent.BubbleTemperature != 373.15f)
				return UnitTestResult.Fail("Underwater vent state did not roundtrip");

			UnderwaterDrillStatePacket drill = Roundtrip(
				new UnderwaterDrillStatePacket
				{
					DrillNetId = 19,
					Progress = 0.75f,
					DiamondMass = 125f,
					Phase = UnderwaterDrillPhase.Working
				},
				new UnderwaterDrillStatePacket());
			if (drill.DrillNetId != 19 || drill.Progress != 0.75f || drill.DiamondMass != 125f ||
			    drill.Phase != UnderwaterDrillPhase.Working)
				return UnitTestResult.Fail("Underwater drill state did not roundtrip");

			return UnitTestResult.Pass("Aquatic outcomes and absolute state roundtrip exactly");
		}

		[UnitTest(name: "Aquatic state application is idempotent", category: "Sync")]
		public static UnitTestResult Idempotence()
		{
			if (UnderwaterVentSync.NeedsApply(
				    0.5f, UnderwaterVentPhase.Erupting,
				    0.5f, UnderwaterVentPhase.Erupting) ||
			    !UnderwaterVentSync.NeedsApply(
				    0.5f, UnderwaterVentPhase.Erupting,
				    1f, UnderwaterVentPhase.Blocked))
				return UnitTestResult.Fail("Underwater vent absolute state idempotence is incorrect");
			if (UnderwaterDrillSync.NeedsApply(
				    0.25f, 150f, UnderwaterDrillPhase.Working,
				    0.25f, 150f, UnderwaterDrillPhase.Working) ||
			    !UnderwaterDrillSync.NeedsApply(
				    0.25f, 150f, UnderwaterDrillPhase.Working,
				    0.5f, 125f, UnderwaterDrillPhase.Working))
				return UnitTestResult.Fail("Underwater drill absolute state idempotence is incorrect");
			if (!UnderwaterVentStatePacket.IsNewBubbleSequence(7, 8) ||
			    UnderwaterVentStatePacket.IsNewBubbleSequence(7, 7) ||
			    UnderwaterVentStatePacket.IsNewBubbleSequence(7, 6))
				return UnitTestResult.Fail("Bubble outcome de-duplication is incorrect");
			var velocity = new Vector2(-0.5f, 3f);
			if (AquaticShearingOutcomePacket.ShouldApplyProductVelocity(true, velocity, velocity) ||
			    !AquaticShearingOutcomePacket.ShouldApplyProductVelocity(true, Vector2.zero, velocity) ||
			    !AquaticShearingOutcomePacket.ShouldApplyProductVelocity(false, Vector2.zero, velocity))
				return UnitTestResult.Fail("Shearing product velocity de-duplication is incorrect");

			return UnitTestResult.Pass("Repeated state and outcomes cannot duplicate production or velocity");
		}

		private static bool SameDlc(string[] actual, string[] expected)
		{
			if (actual == null || expected == null || actual.Length != expected.Length)
				return false;
			for (int i = 0; i < actual.Length; i++)
			{
				if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal))
					return false;
			}
			return true;
		}

		private static bool HasInstanceMethod(Type type, string name, Type[] parameters)
			=> type.GetMethod(
				name,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null,
				parameters,
				null) != null;

		private static T Roundtrip<T>(T input, T output) where T : IPacket
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			return output;
		}
	}
}
