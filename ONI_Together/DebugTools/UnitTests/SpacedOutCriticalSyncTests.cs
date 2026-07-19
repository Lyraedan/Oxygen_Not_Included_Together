using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SpacedOutCriticalSyncTests
	{
		[UnitTest(name: "Spaced Out HEP state is bounded host outcome", category: "Sync")]
		public static UnitTestResult HepOutcomeRoundtrip()
		{
			var packet = new HighEnergyParticleStatePacket
			{
				NetId = -17,
				Revision = 2,
				Position = new Vector3(12.5f, 8.5f, 0f),
				Direction = EightDirection.Right,
				Speed = 20f,
				Payload = 80f,
				CapturedByNetId = -19,
				Collision = HighEnergyParticle.CollisionType.Captured,
				CaptureStorageNetId = -18,
				CaptureStoredParticles = 320f
			};
			HighEnergyParticleStatePacket copy = Roundtrip(packet, new HighEnergyParticleStatePacket());
			if (copy is not IHostOnlyPacket || copy.NetId != -17 || copy.Revision != 2 ||
			    copy.Position != packet.Position || copy.Direction != EightDirection.Right ||
			    copy.Speed != 20f || copy.Payload != 80f || copy.CapturedByNetId != -19 ||
			    copy.CaptureStorageNetId != -18 ||
			    copy.CaptureStoredParticles != 320f ||
			    copy.Collision != HighEnergyParticle.CollisionType.Captured)
				return UnitTestResult.Fail("HEP absolute outcome did not roundtrip");
			if (!HighEnergyParticleStatePacket.ShouldApply(false, true) ||
			    HighEnergyParticleStatePacket.ShouldApply(true, true) ||
			    HighEnergyParticleStatePacket.ShouldApply(false, false) ||
			    HighEnergyParticleSync.NeedsApply(2, 2) || !HighEnergyParticleSync.NeedsApply(2, 3))
				return UnitTestResult.Fail("HEP authority or idempotence gate is incorrect");
			if (!Matches(typeof(HighEnergyParticle), "Capture", typeof(void), typeof(HighEnergyParticlePort)) ||
			    !Matches(typeof(HighEnergyParticle), nameof(HighEnergyParticle.MovingUpdate), typeof(void), typeof(float)) ||
			    !Matches(typeof(HighEnergyParticle), nameof(HighEnergyParticle.Collide), typeof(void),
				    typeof(HighEnergyParticle.CollisionType)) ||
			    !Matches(typeof(HighEnergyParticleSpawner), nameof(HighEnergyParticleSpawner.LauncherUpdate),
				    typeof(void), typeof(float)) ||
			    !Matches(typeof(ManualHighEnergyParticleSpawner), nameof(ManualHighEnergyParticleSpawner.LauncherUpdate),
				    typeof(void)) ||
			    !Matches(typeof(HighEnergyParticleRedirector), "LaunchParticle", typeof(void)))
				return UnitTestResult.Fail("HEP Harmony target signature changed");
			if (!HighEnergyParticleSync.ShouldRunProducer(false, false) ||
			    !HighEnergyParticleSync.ShouldRunProducer(true, true) ||
			    HighEnergyParticleSync.ShouldRunProducer(true, false) ||
			    HighEnergyParticleSync.ShouldPublishProducerOutcome(false, -17) ||
			    !HighEnergyParticleSync.ShouldPublishProducerOutcome(true, -17) ||
			    HighEnergyParticleSync.ShouldPublishProducerOutcome(true, 0) ||
			    !HighEnergyParticleSync.ShouldDestroyUnassignedSpawn(true, true, false, 0) ||
			    HighEnergyParticleSync.ShouldDestroyUnassignedSpawn(true, true, true, 0) ||
			    HighEnergyParticleSync.ShouldDestroyUnassignedSpawn(true, false, false, 0))
				return UnitTestResult.Fail("HEP producer authority, delayed outcome or ghost gate is incorrect");
			packet.Payload = float.NaN;
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Non-finite HEP payload was accepted");
			return UnitTestResult.Pass("HEP lifecycle, movement and collision outcome are bounded host state");
		}

		[UnitTest(name: "Spaced Out HEP cleanup revisions are bounded tombstones", category: "Sync")]
		public static UnitTestResult HepCleanupRevisionLifecycle()
		{
			HighEnergyParticleSync.ResetSessionState();
			HighEnergyParticleSync.RecordTombstone(41, 7, 10f);
			if (!HighEnergyParticleSync.IsTombstoned(41, 7, 11f) ||
			    !HighEnergyParticleSync.IsTombstoned(41, 6, 11f) ||
			    HighEnergyParticleSync.IsTombstoned(41, 8, 11f) ||
			    HighEnergyParticleSync.IsTombstoned(41, 7,
			    10f + HighEnergyParticleSync.TombstoneLifetimeSeconds + 1f))
				return UnitTestResult.Fail("HEP tombstone revision or expiry gate is incorrect");
			HighEnergyParticleSync.RecordTombstone(41, 7, 10f);
			bool committed = false;
			HighEnergyParticleSync.CompleteApply(41, false, true, () => committed = true);
			if (committed || !HighEnergyParticleSync.IsTombstoned(41, 7, 11f))
				return UnitTestResult.Fail("Cleaned-up HEP lost its tombstone during apply completion");
			HighEnergyParticleSync.CompleteApply(41, true, true, () => committed = true);
			if (!committed || HighEnergyParticleSync.IsTombstoned(41, 7, 11f))
				return UnitTestResult.Fail("Surviving HEP did not commit and clear its tombstone");

			for (int i = 0; i <= HighEnergyParticleSync.MaxTombstones; i++)
				HighEnergyParticleSync.RecordTombstone(1000 + i, i, 20f);
			if (HighEnergyParticleSync.TombstoneCount > HighEnergyParticleSync.MaxTombstones)
				return UnitTestResult.Fail("HEP tombstones exceeded their capacity");
			HighEnergyParticleSync.ResetSessionState();
			if (HighEnergyParticleSync.TombstoneCount != 0)
				return UnitTestResult.Fail("HEP tombstones survived session reset");
			return UnitTestResult.Pass("HEP active revisions live on particles and cleanup tombstones are bounded");
		}

		[UnitTest(name: "Spaced Out HEP direction is request-only absolute host state", category: "Sync")]
		public static UnitTestResult HepDirectionRoundtrip()
		{
			var request = new HighEnergyParticleDirectionRequestPacket
			{
				TargetNetId = -31,
				ExpectedDirection = EightDirection.Up,
				DesiredDirection = EightDirection.DownRight
			};
			HighEnergyParticleDirectionRequestPacket requestCopy = Roundtrip(
				request, new HighEnergyParticleDirectionRequestPacket());
			if (requestCopy is not IClientRelayable || requestCopy.TargetNetId != -31 ||
			    requestCopy.ExpectedDirection != EightDirection.Up ||
			    requestCopy.DesiredDirection != EightDirection.DownRight)
				return UnitTestResult.Fail("HEP direction request did not roundtrip");

			var state = new HighEnergyParticleDirectionStatePacket
			{
				TargetNetId = -31,
				Direction = EightDirection.DownRight
			};
			HighEnergyParticleDirectionStatePacket stateCopy = Roundtrip(
				state, new HighEnergyParticleDirectionStatePacket());
			if (stateCopy is not IHostOnlyPacket || stateCopy.TargetNetId != -31 ||
			    stateCopy.Direction != EightDirection.DownRight ||
			    !HighEnergyParticleDirectionStatePacket.ShouldApply(false, true) ||
			    HighEnergyParticleDirectionStatePacket.ShouldApply(true, true) ||
			    HighEnergyParticleDirectionStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("HEP absolute direction state authority is incorrect");

			DispatchContext directClient = new(41, false);
			DispatchContext verifiedClient = directClient.AsVerifiedHostBroadcast();
			if (!HighEnergyParticleDirectionRequestPacket.ShouldAccept(true, verifiedClient) ||
			    HighEnergyParticleDirectionRequestPacket.ShouldAccept(true, directClient) ||
			    HighEnergyParticleDirectionRequestPacket.ShouldAccept(false, verifiedClient) ||
			    !HighEnergyParticleDirectionSync.ShouldRunSetter(false, false, false,
				    EightDirection.Up, EightDirection.Right) ||
			    !HighEnergyParticleDirectionSync.ShouldRunSetter(true, true, false,
				    EightDirection.Up, EightDirection.Right) ||
			    HighEnergyParticleDirectionSync.ShouldRunSetter(true, false, false,
				    EightDirection.Up, EightDirection.Right) ||
			    !HighEnergyParticleDirectionSync.ShouldRunSetter(true, false, true,
				    EightDirection.Up, EightDirection.Right) ||
			    !HighEnergyParticleDirectionSync.ShouldRunSetter(true, false, false,
				    EightDirection.Up, EightDirection.Up))
				return UnitTestResult.Fail("HEP direction request-only setter gate is incorrect");

			if (!HighEnergyParticleDirectionSync.IsSupportedTargetType(typeof(HighEnergyParticleSpawner)) ||
			    !HighEnergyParticleDirectionSync.IsSupportedTargetType(typeof(ManualHighEnergyParticleSpawner)) ||
			    !HighEnergyParticleDirectionSync.IsSupportedTargetType(typeof(HighEnergyParticleRedirector)) ||
			    HighEnergyParticleDirectionSync.IsSupportedTargetType(typeof(DevHEPSpawner)) ||
			    HighEnergyParticleDirectionSync.IsSupportedTargetType(typeof(HEPBridgeTileVisualizer)) ||
			    !Matches(typeof(HighEnergyParticleSpawner), "set_Direction", typeof(void),
				    typeof(EightDirection)) ||
			    !Matches(typeof(ManualHighEnergyParticleSpawner), "set_Direction", typeof(void),
				    typeof(EightDirection)) ||
			    !Matches(typeof(HighEnergyParticleRedirector), "set_Direction", typeof(void),
				    typeof(EightDirection)))
				return UnitTestResult.Fail("HEP direction target validation or Harmony signature changed");

			request.DesiredDirection = (EightDirection)8;
			state.Direction = (EightDirection)8;
			if (request.IsWireValid() || state.IsWireValid())
				return UnitTestResult.Fail("Out-of-range HEP direction was accepted");
			return UnitTestResult.Pass("HEP direction clients request and host broadcasts bounded absolute state");
		}

		[UnitTest(name: "Spaced Out railgun payload is absolute bounded flight state", category: "Sync")]
		public static UnitTestResult RailGunPayloadRoundtrip()
		{
			var packet = new RailGunPayloadStatePacket
			{
				SourceRailGunNetId = -100,
				PayloadNetId = -101,
				Revision = 3,
				Phase = RailGunPayloadPhase.Landing,
				SourceQ = -2,
				SourceR = 3,
				DestinationQ = 5,
				DestinationR = -4,
				DestinationWorld = 7,
				Position = new Vector3(123.5f, 99f, 0f),
				TakeoffVelocity = 35f,
				SourceParticles = 120f,
				SymbolSwapIndex = 1,
				Items = new List<RailGunPayloadItemData>
				{
					new()
					{
						NetId = -102, PrefabHash = SimHashes.Water.CreateTag().GetHashCode(),
						Mass = 20f, Temperature = 300f, DiseaseIndex = byte.MaxValue
					}
				}
			};
			RailGunPayloadStatePacket copy = Roundtrip(packet, new RailGunPayloadStatePacket());
			if (copy is not IHostOnlyPacket || copy.SourceRailGunNetId != -100 ||
			    copy.PayloadNetId != -101 || copy.Revision != 3 || copy.SourceParticles != 120f ||
			    copy.SymbolSwapIndex != 1 ||
			    copy.Phase != RailGunPayloadPhase.Landing || copy.SourceQ != -2 ||
			    copy.DestinationR != -4 || copy.DestinationWorld != 7 || copy.Items.Count != 1 ||
			    copy.Items[0].NetId != -102 || copy.Items[0].Mass != 20f)
				return UnitTestResult.Fail("Railgun payload storage or flight state did not roundtrip");
			if (!RailGunPayloadStatePacket.ShouldApply(false, true) ||
			    RailGunPayloadStatePacket.ShouldApply(true, true) ||
			    RailGunPayloadStatePacket.ShouldApply(false, false) ||
			    RailGunPayloadSync.NeedsApply(3, 3) || !RailGunPayloadSync.NeedsApply(3, 4))
				return UnitTestResult.Fail("Railgun payload authority or idempotence gate is incorrect");
			if (!Matches(typeof(RailGun), "LaunchProjectile", typeof(void)) ||
			    !Matches(typeof(RailGunPayload.StatesInstance), nameof(RailGunPayload.StatesInstance.Launch),
				    typeof(void), typeof(AxialI), typeof(AxialI)) ||
			    !Matches(typeof(RailGunPayload.StatesInstance), nameof(RailGunPayload.StatesInstance.StartLand),
				    typeof(void)) ||
			    !Matches(typeof(RailGunPayload.StatesInstance), nameof(RailGunPayload.StatesInstance.UpdateLanding),
				    typeof(bool), typeof(float)))
				return UnitTestResult.Fail("Railgun payload Harmony target signature changed");
			packet.Items[0].Temperature = float.PositiveInfinity;
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Non-finite payload item temperature was accepted");
			return UnitTestResult.Pass("Railgun payload storage, route, flight and landing are absolute host state");
		}

		[UnitTest(name: "Spaced Out railgun deferred payload state is latest and bounded", category: "Sync")]
		public static UnitTestResult RailGunPendingLifecycle()
		{
			RailGunPayloadSync.ResetSessionState();
			RailGunPayloadSync.CachePending(new RailGunPayloadStatePacket { PayloadNetId = 51, Revision = 2 }, 10f);
			RailGunPayloadSync.CachePending(new RailGunPayloadStatePacket { PayloadNetId = 51, Revision = 4 }, 11f);
			RailGunPayloadSync.CachePending(new RailGunPayloadStatePacket { PayloadNetId = 51, Revision = 3 }, 12f);
			if (!RailGunPayloadSync.TryGetPendingRevision(51, 12f, out int revision) || revision != 4)
				return UnitTestResult.Fail("Railgun pending state did not coalesce to the latest revision");
			if (RailGunPayloadSync.TryGetPendingRevision(51,
				    11f + RailGunPayloadSync.PendingLifetimeSeconds + 1f, out _))
				return UnitTestResult.Fail("Railgun pending state did not expire");

			for (int i = 0; i <= RailGunPayloadSync.MaxPendingPayloads; i++)
				RailGunPayloadSync.CachePending(
					new RailGunPayloadStatePacket { PayloadNetId = 1000 + i, Revision = i }, 20f);
			if (RailGunPayloadSync.PendingCount > RailGunPayloadSync.MaxPendingPayloads)
				return UnitTestResult.Fail("Railgun pending states exceeded their capacity");
			RailGunPayloadSync.ResetSessionState();
			if (RailGunPayloadSync.PendingCount != 0)
				return UnitTestResult.Fail("Railgun pending states survived session reset");
			return UnitTestResult.Pass("Railgun unresolved state coalesces to the latest bounded retry");
		}

		[UnitTest(name: "Spaced Out reactor meltdown emits bounded host outcomes", category: "Sync")]
		public static UnitTestResult ReactorMeltdownRoundtrip()
		{
			var packet = new ReactorMeltdownOutcomePacket
			{
				ReactorNetId = -201,
				Revision = 4,
				MeltdownMassRemaining = 6f,
				TimeSinceMeltdownEmit = 0.1f,
				Comets = new List<ReactorMeltdownCometData>
				{
					new()
					{
						NetId = -202, Position = new Vector3(8f, 12f, 0f),
						Velocity = new Vector2(-10f, 15f), Rotation = 45f,
						Mass = 1f, Temperature = 500f, DiseaseIndex = byte.MaxValue
					}
				},
				Cells = new List<ReactorMeltdownCellData>
				{
					new() { Cell = 123, Mass = 0.5f, Temperature = 3000f, DiseaseCount = 25 }
				}
			};
			ReactorMeltdownOutcomePacket copy = Roundtrip(packet, new ReactorMeltdownOutcomePacket());
			if (copy is not IHostOnlyPacket || copy.ReactorNetId != -201 || copy.Revision != 4 ||
			    copy.Comets.Count != 1 || copy.Comets[0].NetId != -202 || copy.Cells.Count != 1 ||
			    copy.Cells[0].Cell != 123 || copy.MeltdownMassRemaining != 6f)
				return UnitTestResult.Fail("Reactor meltdown outcome did not roundtrip");
			if (!ReactorMeltdownOutcomePacket.ShouldApply(false, true) ||
			    ReactorMeltdownOutcomePacket.ShouldApply(true, true) ||
			    ReactorMeltdownOutcomePacket.ShouldApply(false, false) ||
			    ReactorMeltdownSync.NeedsApply(4, 4) || !ReactorMeltdownSync.NeedsApply(4, 5))
				return UnitTestResult.Fail("Reactor outcome authority or idempotence gate is incorrect");
			MethodBase target = ReactorMeltdownUpdatePatch.TargetMethod();
			if (target is not MethodInfo method || method.ReturnType != typeof(void) ||
			    ReactorMeltdownUpdatePatch.ShouldRunOriginal(true) ||
			    !ReactorMeltdownUpdatePatch.ShouldRunOriginal(false))
				return UnitTestResult.Fail("Reactor meltdown update target or session gate changed");
			packet.Cells[0].Mass = -1f;
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Negative meltdown cell mass was accepted");
			return UnitTestResult.Pass("Reactor comet and cell outcomes run once on host and are bounded");
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

		private static bool Matches(Type type, string name, Type returnType, params Type[] parameters)
		{
			MethodInfo method = AccessTools.Method(type, name, parameters);
			return method != null && method.ReturnType == returnType;
		}
	}
}
