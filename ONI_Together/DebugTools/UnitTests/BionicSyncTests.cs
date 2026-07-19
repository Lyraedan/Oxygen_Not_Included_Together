using System.IO;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Bionic;
using ONI_Together.Patches.Bionics;
using ONI_Together.Patches.DLC.Bionic;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class BionicSyncTests
	{
		[UnitTest(name: "Bionic electrobank state is bounded and absolute", category: "Sync")]
		public static UnitTestResult ElectrobankStateBounds()
		{
			var input = new BionicElectrobankStatePacket
			{
				NetId = 404,
				CurrentHealth = 7.5f,
				Charge = 60000f,
				TimeSincePowerDrawn = 0.25f,
				HasLifetime = true,
				LifetimeRemaining = 45000f
			};
			BionicElectrobankStatePacket output = Roundtrip(input, new BionicElectrobankStatePacket());
			if (output.NetId != 404 || output.CurrentHealth != 7.5f || output.Charge != 60000f ||
			    output.TimeSincePowerDrawn != 0.25f ||
			    !output.HasLifetime || output.LifetimeRemaining != 45000f)
				return UnitTestResult.Fail("Electrobank absolute state did not roundtrip");

			input.CurrentHealth = BionicElectrobankStatePacket.MaxHealth + 0.1f;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Out-of-bounds electrobank health was accepted");
			input.CurrentHealth = 7.5f;
			input.TimeSincePowerDrawn = BionicElectrobankStatePacket.MaxTimeSincePowerDrawn + 0.1f;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Out-of-bounds radiation timer was accepted");
			return UnitTestResult.Pass("Electrobank health, charge, and lifetime are bounded absolute state");
		}

		[UnitTest(name: "Bionic runtime is host authoritative", category: "Sync")]
		public static UnitTestResult RuntimeAuthority()
		{
			if (new BionicElectrobankStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("Electrobank state is not host-only");
			if (!BionicRuntimeSync.ShouldRunAuthoritativeGameplay(false, false) ||
			    !BionicRuntimeSync.ShouldRunAuthoritativeGameplay(true, true) ||
			    BionicRuntimeSync.ShouldRunAuthoritativeGameplay(true, false))
				return UnitTestResult.Fail("Client bionic simulation was not suppressed");
			if (!BionicRuntimeSync.ShouldRunExplosion(false, false, false) ||
			    !BionicRuntimeSync.ShouldRunExplosion(true, true, false) ||
			    BionicRuntimeSync.ShouldRunExplosion(true, false, false) ||
			    !BionicRuntimeSync.ShouldRunExplosion(true, false, true))
				return UnitTestResult.Fail("Explosion apply guard is incorrect");
			if (!BionicElectrobankStatePacket.ShouldApply(false, true) ||
			    BionicElectrobankStatePacket.ShouldApply(true, true) ||
			    BionicElectrobankStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Electrobank state authority gate is incorrect");
			return UnitTestResult.Pass("Host owns bionic simulation and clients only apply host state");
		}

		[UnitTest(name: "Bionic microchip progress is host state", category: "Sync")]
		public static UnitTestResult MicrochipProgressState()
		{
			var input = new BionicMicrochipProgressStatePacket { NetId = 505, Progress = 0.75f };
			BionicMicrochipProgressStatePacket output = Roundtrip(
				input, new BionicMicrochipProgressStatePacket());
			if (output.NetId != 505 || output.Progress != 0.75f)
				return UnitTestResult.Fail("Microchip progress did not roundtrip");
			if (new BionicMicrochipProgressStatePacket() is not IHostOnlyPacket ||
			    !BionicMicrochipProgressStatePacket.ShouldApply(false, true) ||
			    BionicMicrochipProgressStatePacket.ShouldApply(true, true))
				return UnitTestResult.Fail("Microchip progress authority is incorrect");
			input.Progress = 1.01f;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Out-of-bounds microchip progress was accepted");
			if (BionicMicrochipSync.ShouldCreateMicrochip(true, false) ||
			    !BionicMicrochipSync.ShouldCreateMicrochip(true, true))
				return UnitTestResult.Fail("Client could create a duplicate microchip");
			return UnitTestResult.Pass("Progress is bounded host state and only host creates the microchip");
		}

		[UnitTest(name: "Self-charging explosion outcome is sequenced once", category: "Sync")]
		public static UnitTestResult SelfChargingExplosionSequence()
		{
			var input = new BionicSelfChargingExplosionPacket { NetId = 606, Sequence = 3 };
			BionicSelfChargingExplosionPacket output = Roundtrip(
				input, new BionicSelfChargingExplosionPacket());
			if (output.NetId != 606 || output.Sequence != 3)
				return UnitTestResult.Fail("Explosion outcome did not roundtrip");
			if (new BionicSelfChargingExplosionPacket() is not IHostOnlyPacket ||
			    !BionicSelfChargingExplosionPacket.ShouldApply(false, true) ||
			    BionicSelfChargingExplosionPacket.ShouldApply(true, true))
				return UnitTestResult.Fail("Explosion outcome authority is incorrect");
			if (!BionicExplosionSync.IsNewerSequence(2, 3) ||
			    BionicExplosionSync.IsNewerSequence(3, 3) ||
			    BionicExplosionSync.IsNewerSequence(4, 3))
				return UnitTestResult.Fail("Duplicate or stale explosion outcome was accepted");
			BionicExplosionSync.ResetSessionState();
			if (!BionicExplosionSync.BeginHostCapture(606) || !BionicExplosionSync.HasActiveCapture ||
			    !BionicExplosionSync.TryCompleteHostCapture(
				    out BionicSelfChargingExplosionPacket outcome,
				    out BionicExplosionVelocityPacket velocities) ||
			    outcome.NetId != 606 || outcome.Sequence != 1 || velocities.ExplosionNetId != 606 ||
			    velocities.Sequence != 1 || BionicExplosionSync.HasActiveCapture)
				return UnitTestResult.Fail("Explosion capture published before successful completion");
			input.Sequence = 0;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Zero explosion sequence was accepted");
			return UnitTestResult.Pass("Explosion side effects are host-only and sequence-deduplicated");
		}

		[UnitTest(name: "Bionic explosion velocity correction is absolute", category: "Sync")]
		public static UnitTestResult ExplosionVelocityCorrection()
		{
			var input = new BionicExplosionVelocityPacket { ExplosionNetId = 606, Sequence = 3 };
			input.Corrections.Add(new BionicExplosionVelocityCorrection
			{
				TargetNetId = 707,
				Velocity = new Vector2(4.5f, 3f)
			});
			BionicExplosionVelocityPacket output = Roundtrip(input, new BionicExplosionVelocityPacket());
			if (output.ExplosionNetId != 606 || output.Sequence != 3 || output.Corrections.Count != 1 ||
			    output.Corrections[0].TargetNetId != 707 || output.Corrections[0].Velocity != new Vector2(4.5f, 3f))
				return UnitTestResult.Fail("Explosion velocity correction did not roundtrip");
			if (new BionicExplosionVelocityPacket() is not IHostOnlyPacket ||
			    !BionicExplosionVelocityPacket.ShouldApply(false, true) ||
			    BionicExplosionVelocityPacket.ShouldApply(true, true))
				return UnitTestResult.Fail("Explosion velocity authority is incorrect");
			if (!BionicExplosionSync.ShouldApplyCorrectionSequence(3, 2, 3) ||
			    BionicExplosionSync.ShouldApplyCorrectionSequence(3, 3, 3) ||
			    BionicExplosionSync.ShouldApplyCorrectionSequence(2, 1, 3))
				return UnitTestResult.Fail("Velocity correction sequence gate is incorrect");
			input.Corrections[0].Velocity = new Vector2(BionicExplosionVelocityPacket.MaxVelocity + 1f, 0f);
			if (input.IsWireValid())
				return UnitTestResult.Fail("Out-of-bounds explosion velocity was accepted");
			return UnitTestResult.Pass("Host explosion velocity is bounded absolute state and idempotent");
		}

		[UnitTest(name: "Bionic assignment enforces verified request authority", category: "Sync")]
		public static UnitTestResult AssignmentAuthority()
		{
			if (new BionicAssignmentRequestPacket() is not IClientRelayable ||
			    new BionicAssignmentStatePacket() is not IHostOnlyPacket ||
			    new ExplorerGeyserRevealPacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("Bionic packet authority marker is missing");

			var direct = new DispatchContext(9, false);
			DispatchContext verified = direct.AsVerifiedHostBroadcast();
			if (BionicAssignmentRequestPacket.ShouldAccept(true, direct, true) ||
			    BionicAssignmentRequestPacket.ShouldAccept(true, verified, false) ||
			    !BionicAssignmentRequestPacket.ShouldAccept(true, verified, true))
				return UnitTestResult.Fail("Bionic request provenance gate is incorrect");
			if (!BionicAssignmentStatePacket.ShouldApply(false, true) ||
			    BionicAssignmentStatePacket.ShouldApply(true, true))
				return UnitTestResult.Fail("Bionic state authority gate is incorrect");
			return UnitTestResult.Pass("Bionic requests require verified relay and states require host");
		}

		[UnitTest(name: "Bionic velocity waits for one atomic outcome", category: "Sync")]
		public static UnitTestResult ExplosionVelocityPendingState()
		{
			var first = VelocityPacket(606, 3, 707, new Vector2(1f, 2f));
			var latest = VelocityPacket(606, 3, 707, new Vector2(3f, 4f));
			BionicExplosionSync.ResetSessionState();
			BionicExplosionSync.QueuePendingVelocity(first, 10f);
			BionicExplosionSync.QueuePendingVelocity(latest, 11f);
			BionicExplosionSync.QueuePendingVelocity(
				VelocityPacket(606, 4, 708, new Vector2(5f, 6f)), 11f);
			if (BionicExplosionSync.PendingVelocityCount != 2 ||
			    !ReferenceEquals(BionicExplosionSync.GetPendingVelocity(606, 3, 11f), latest))
				return UnitTestResult.Fail("Velocity cache was not keyed by explosion and sequence");
			if (BionicExplosionSync.CanMutateVelocities(3, 2, 3, targetsResolved: false) ||
			    !BionicExplosionSync.CanMutateVelocities(3, 2, 3, targetsResolved: true))
				return UnitTestResult.Fail("Velocity targets could mutate before outcome and target resolution");
			BionicExplosionSync.Cleanup(606);
			if (BionicExplosionSync.GetPendingVelocity(606, 3, 11f) != null ||
			    BionicExplosionSync.GetPendingVelocity(606, 4, 11f) != null ||
			    AccessTools.Method(typeof(SelfChargingElectrobank), "OnCleanUp") == null)
				return UnitTestResult.Fail("Explosion cleanup gate is missing or retained velocity outcomes");
			BionicExplosionSync.QueuePendingVelocity(
				VelocityPacket(700, 1, 701, Vector2.zero), 20f);
			if (BionicExplosionSync.GetPendingVelocity(700, 1,
			    20f + BionicExplosionSync.PendingLifetimeSeconds + 1f) != null)
				return UnitTestResult.Fail("Pending velocity outcome did not expire");

			for (int i = 0; i <= BionicExplosionSync.MaxPendingVelocities; i++)
				BionicExplosionSync.QueuePendingVelocity(
					VelocityPacket(3000 + i, 1, 4000 + i, Vector2.zero), 30f);
			if (BionicExplosionSync.PendingVelocityCount != BionicExplosionSync.MaxPendingVelocities)
				return UnitTestResult.Fail("Pending velocity outcomes exceeded their bound");
			BionicExplosionSync.ResetSessionState();
			if (BionicExplosionSync.PendingVelocityCount != 0)
				return UnitTestResult.Fail("Session reset retained velocity outcomes");
			return UnitTestResult.Pass("Velocity retries are keyed, atomic, bounded, and cleaned up");
		}

		[UnitTest(name: "Bionic upgrade assignment preserves both targets", category: "Sync")]
		public static UnitTestResult AssignmentTargetsRoundtrip()
		{
			var input = new BionicAssignmentRequestPacket(new BionicAssignmentData
			{
				UpgradeNetId = 101,
				HasAssignee = true,
				AssigneeNetId = 202
			});
			BionicAssignmentRequestPacket output = Roundtrip(input, new BionicAssignmentRequestPacket());
			if (output.Data.UpgradeNetId != 101 || !output.Data.HasAssignee || output.Data.AssigneeNetId != 202)
				return UnitTestResult.Fail("Upgrade or bionic target identity was lost");

			output.Data.HasAssignee = false;
			output.Data.AssigneeNetId = 0;
			if (!output.Data.IsWireValid())
				return UnitTestResult.Fail("Explicit unassignment state was rejected");
			return UnitTestResult.Pass("Assignment and unassignment remain distinct absolute states");
		}

		[UnitTest(name: "Explorer booster outcome is bounded and stable", category: "Sync")]
		public static UnitTestResult ExplorerOutcomeBounds()
		{
			var input = new ExplorerGeyserRevealPacket
			{
				ExplorerNetId = 303,
				WorldId = 4,
				Cell = 12345
			};
			ExplorerGeyserRevealPacket output = Roundtrip(input, new ExplorerGeyserRevealPacket());
			if (output.ExplorerNetId != 303 || output.WorldId != 4 || output.Cell != 12345)
				return UnitTestResult.Fail("Explorer reveal target did not roundtrip");
			if (ExplorerGeyserRevealSync.BuildOutcomeKey(303, 4, 12345) != "303:4:12345")
				return UnitTestResult.Fail("Explorer outcome key is unstable");
			input.Cell = ExplorerGeyserRevealPacket.MaxCell;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Out-of-bounds reveal cell was accepted");
			return UnitTestResult.Pass("Explorer random result is a bounded host-selected cell");
		}

		[UnitTest(name: "Explorer reveal capture uses positional Harmony arguments", category: "Sync")]
		public static UnitTestResult ExplorerRevealPatchSignature()
		{
			var target = AccessTools.DeclaredMethod(typeof(GridVisibility), nameof(GridVisibility.Reveal),
				new[] { typeof(int), typeof(int), typeof(int), typeof(float) });
			var postfix = AccessTools.DeclaredMethod(typeof(ExplorerGridRevealCapturePatch), "Postfix",
				new[] { typeof(int), typeof(int) });
			if (target == null || postfix == null)
				return UnitTestResult.Fail("Explorer reveal Harmony target or postfix is missing");
			var parameters = postfix.GetParameters();
			if (parameters.Length != 2 || parameters[0].Name != "__0" || parameters[1].Name != "__1")
				return UnitTestResult.Fail("Explorer reveal capture depends on unstable game parameter names");
			return UnitTestResult.Pass("Explorer reveal capture is stable across game parameter renames");
		}

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

		private static BionicExplosionVelocityPacket VelocityPacket(
			int explosionNetId, int sequence, int targetNetId, Vector2 velocity)
		{
			var packet = new BionicExplosionVelocityPacket
			{
				ExplosionNetId = explosionNetId,
				Sequence = sequence
			};
			packet.Corrections.Add(new BionicExplosionVelocityCorrection
			{
				TargetNetId = targetNetId,
				Velocity = velocity
			});
			return packet;
		}
	}
}
