using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Frosty;
using ONI_Together.Patches.DLC.Frosty;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class FrostySyncTests
	{
		[UnitTest(name: "Frosty synchronization enforces host authority", category: "Sync")]
		public static UnitTestResult AuthorityMarkers()
		{
			if (new GeothermalControllerRequestPacket() is not IClientRelayable)
				return UnitTestResult.Fail("Controller request is not relayable");
			if (new GeothermalControllerStatePacket() is not IHostOnlyPacket ||
			    new GeothermalVentStatePacket() is not IHostOnlyPacket ||
			    new MiniCometStatePacket() is not IHostOnlyPacket ||
			    new IceKettleStatePacket() is not IHostOnlyPacket ||
			    new IceKettleExhaustPacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("A Frosty outcome packet is not host-only");
			if (!PacketRegistry.HasRegisteredPacket(typeof(IceKettleExhaustPacket)))
				return UnitTestResult.Fail("Ice kettle exhaust packet was not auto-registered");

			var direct = new DispatchContext(7, false);
			DispatchContext verified = direct.AsVerifiedHostBroadcast();
			if (GeothermalControllerRequestPacket.ShouldAccept(true, direct, true) ||
			    GeothermalControllerRequestPacket.ShouldAccept(true, verified, false) ||
			    !GeothermalControllerRequestPacket.ShouldAccept(true, verified, true))
				return UnitTestResult.Fail("Controller request provenance gate is incorrect");
			if (!MiniCometStatePacket.ShouldApply(false, true) ||
			    MiniCometStatePacket.ShouldApply(true, true) || MiniCometStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Mini comet authority gate is incorrect");
			return UnitTestResult.Pass("Frosty requests and outcomes enforce transport authority");
		}

		[UnitTest(name: "Frosty controller and vent states roundtrip exactly", category: "Sync")]
		public static UnitTestResult GeothermalRoundtrip()
		{
			var request = new GeothermalControllerRequestPacket(
				40,
				GeothermalController.ProgressState.NOT_STARTED,
				GeothermalController.ProgressState.FETCHING_STEEL);
			GeothermalControllerRequestPacket requestCopy = Roundtrip(
				request, new GeothermalControllerRequestPacket());
			if (requestCopy.TargetNetId != 40 ||
			    requestCopy.ExpectedProgress != GeothermalController.ProgressState.NOT_STARTED ||
			    requestCopy.DesiredProgress != GeothermalController.ProgressState.FETCHING_STEEL ||
			    GeothermalControllerRequestPacket.IsValidTransition(
				    GeothermalController.ProgressState.COMPLETE,
				    GeothermalController.ProgressState.NOT_STARTED))
				return UnitTestResult.Fail("Controller request transition is ambiguous or replayable");

			var controller = new GeothermalControllerStatePacket
			{
				TargetNetId = 41,
				Progress = GeothermalController.ProgressState.COMPLETE,
				Phase = GeothermalControllerPhase.OnlineVentingLoop
			};
			GeothermalControllerStatePacket controllerCopy = Roundtrip(
				controller, new GeothermalControllerStatePacket());
			if (controllerCopy.TargetNetId != 41 ||
			    controllerCopy.Progress != GeothermalController.ProgressState.COMPLETE ||
			    controllerCopy.Phase != GeothermalControllerPhase.OnlineVentingLoop)
				return UnitTestResult.Fail("Controller progress or phase did not roundtrip");

			var vent = new GeothermalVentStatePacket
			{
				TargetNetId = 42,
				RecentMass = 120f,
				HasEmitterElement = true,
				EmitterElement = Element(SimHashes.Steam, 8f, false),
				AvailableMaterial = new List<GeothermalElementState>
				{
					Element(SimHashes.Steam, 8f, false),
					Element(SimHashes.Iron, 4f, true)
				}
			};
			GeothermalVentStatePacket ventCopy = Roundtrip(vent, new GeothermalVentStatePacket());
			if (ventCopy.TargetNetId != 42 || ventCopy.AvailableMaterial.Count != 2 ||
			    ventCopy.EmitterElement.Element != SimHashes.Steam)
				return UnitTestResult.Fail("Vent material or selected emission did not roundtrip");
			return UnitTestResult.Pass("Controller phase and selected vent emission are absolute");
		}

		[UnitTest(name: "Frosty ice kettle state roundtrips exactly", category: "Sync")]
		public static UnitTestResult IceKettleRoundtrip()
		{
			var packet = new IceKettleStatePacket
			{
				TargetNetId = -73,
				Revision = 17,
				MeltingTimer = 4.25f,
				FuelStorage = Storage(Item(101, 1001, 8f, 290f, 2, 11)),
				KettleStorage = Storage(Item(102, 1002, 75f, 255f, 3, 12)),
				OutputStorage = Storage(Item(103, 1003, 75f, 278f, 4, 13))
			};
			var exhaust = new IceKettleExhaustPacket
			{
				TargetNetId = -73,
				Exhaust = new IceKettleExhaustState
				{
					Element = SimHashes.CarbonDioxide,
					Mass = 0.8f,
					Temperature = 290f,
					Sequence = 9
				}
			};
			IceKettleStatePacket copy = Roundtrip(packet, new IceKettleStatePacket());
			if (copy is not IHostOnlyPacket || copy.TargetNetId != -73 || copy.Revision != 17 ||
			    copy.MeltingTimer != 4.25f ||
			    copy.FuelStorage.Items.Count != 1 || copy.FuelStorage.Items[0].NetId != 101 ||
			    copy.KettleStorage.Items[0].DiseaseCount != 12 ||
			    copy.OutputStorage.Items[0].Temperature != 278f)
				return UnitTestResult.Fail("Ice kettle timer or storage state did not roundtrip");
			IceKettleExhaustPacket exhaustCopy = Roundtrip(exhaust, new IceKettleExhaustPacket());
			if (exhaustCopy is not IHostOnlyPacket || exhaustCopy.TargetNetId != -73 ||
			    exhaustCopy.Exhaust.Element != SimHashes.CarbonDioxide ||
			    exhaustCopy.Exhaust.Mass != 0.8f || exhaustCopy.Exhaust.Temperature != 290f ||
			    exhaustCopy.Exhaust.Sequence != 9)
				return UnitTestResult.Fail("Ice kettle exhaust event did not roundtrip independently");
			copy.MeltingTimer = float.PositiveInfinity;
			if (copy.IsWireValid())
				return UnitTestResult.Fail("Non-finite ice kettle timer was accepted");
			copy.MeltingTimer = 0f;
			for (int i = 0; i < IceKettleStorageState.MaxItems; i++)
				copy.FuelStorage.Items.Add(Item(200 + i, 1200 + i, 1f, 280f, byte.MaxValue, 0));
			if (copy.IsWireValid())
				return UnitTestResult.Fail("Oversized ice kettle storage state was accepted");
			return UnitTestResult.Pass("Ice kettle snapshot and sequenced exhaust event roundtrip independently");
		}

		[UnitTest(name: "Frosty ice kettle gameplay is host authoritative", category: "Sync")]
		public static UnitTestResult IceKettleAuthority()
		{
			if (!IceKettleStatePacket.ShouldApply(false, true) ||
			    IceKettleStatePacket.ShouldApply(true, true) || IceKettleStatePacket.ShouldApply(false, false) ||
			    !IceKettleExhaustPacket.ShouldApply(false, true) ||
			    IceKettleExhaustPacket.ShouldApply(true, true) ||
			    IceKettleExhaustPacket.ShouldApply(false, false) ||
			    !IceKettleSync.ShouldRunGameplay(false, false, false) ||
			    !IceKettleSync.ShouldRunGameplay(true, true, false) ||
			    IceKettleSync.ShouldRunGameplay(true, false, false) ||
			    !IceKettleSync.ShouldRunGameplay(true, false, true))
				return UnitTestResult.Fail("Ice kettle gameplay or state authority gate is incorrect");
			if (!Matches(typeof(IceKettle), nameof(IceKettle.MeltingTimerUpdate), typeof(void),
				    typeof(IceKettle.Instance), typeof(float)) ||
			    !Matches(typeof(IceKettle), nameof(IceKettle.MeltNextBatch), typeof(void),
				    typeof(IceKettle.Instance)))
				return UnitTestResult.Fail("An IceKettle Harmony target signature changed");
			return UnitTestResult.Pass("Client melting is suppressed and both packet types are host-only");
		}

		[UnitTest(name: "Frosty mini comet actual state is bounded", category: "Sync")]
		public static UnitTestResult MiniCometBounds()
		{
			var packet = new MiniCometStatePacket
			{
				TargetNetId = 77,
				Position = new Vector3(10f, 20f, 0f),
				Offset = new Vector3(1f, 2f, 0f),
				Velocity = new Vector2(-7f, 9f),
				Rotation = 25f,
				Element = SimHashes.Iron,
				Mass = 20f,
				Temperature = 800f,
				DiseaseIndex = byte.MaxValue,
				DiseaseCount = 0,
				Targeted = true
			};
			MiniCometStatePacket copy = Roundtrip(packet, new MiniCometStatePacket());
			if (copy.TargetNetId != 77 || copy.Velocity != new Vector2(-7f, 9f) ||
			    copy.Offset != new Vector3(1f, 2f, 0f) || copy.Element != SimHashes.Iron || !copy.Targeted)
				return UnitTestResult.Fail("Mini comet target or trajectory did not roundtrip");

			packet.Velocity = new Vector2(float.PositiveInfinity, 0f);
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Non-finite mini comet velocity was accepted");
			return UnitTestResult.Pass("Mini comet identity, trajectory and material are bounded");
		}

		[UnitTest(name: "Frosty ice kettle state is revisioned and expires", category: "Sync")]
		public static UnitTestResult IceKettlePendingState()
		{
			var first = new IceKettleStatePacket { TargetNetId = 94, Revision = 2, MeltingTimer = 1f };
			var latest = new IceKettleStatePacket { TargetNetId = 94, Revision = 4, MeltingTimer = 2f };
			var stale = new IceKettleStatePacket { TargetNetId = 94, Revision = 3, MeltingTimer = 3f };
			IceKettleSync.ResetSessionState();
			if (IceKettleSync.NextHostRevision(94) != 1 || IceKettleSync.NextHostRevision(94) != 2)
				return UnitTestResult.Fail("Host kettle revisions are not monotonic");
			IceKettleSync.ResetSessionState();
			IceKettleSync.QueuePending(first, 10f);
			IceKettleSync.QueuePending(latest, 11f);
			IceKettleSync.QueuePending(stale, 12f);
			if (IceKettleSync.PendingCount != 1 ||
			    !IceKettleSync.TryGetPendingRevision(94, 12f, out long revision) || revision != 4 ||
			    IceKettleSync.NeedsApply(4, 3) || !IceKettleSync.NeedsApply(3, 4))
				return UnitTestResult.Fail("Pending kettle state did not keep only the greatest revision");
			if (IceKettleSync.CanMutate(kettleResolved: false, storagesResolved: true) ||
			    IceKettleSync.CanMutate(kettleResolved: true, storagesResolved: false) ||
			    !IceKettleSync.CanMutate(kettleResolved: true, storagesResolved: true))
				return UnitTestResult.Fail("Kettle timer or inventory could mutate before resolution");
			if (IceKettleSync.TryGetPendingRevision(94,
			    11f + IceKettleSync.PendingLifetimeSeconds + 1f, out _))
				return UnitTestResult.Fail("Pending kettle state did not expire");

			for (int i = 0; i <= IceKettleSync.MaxPendingStates; i++)
				IceKettleSync.QueuePending(new IceKettleStatePacket
				{
					TargetNetId = 2000 + i,
					Revision = i + 1
				}, 20f);
			if (IceKettleSync.PendingCount != IceKettleSync.MaxPendingStates)
				return UnitTestResult.Fail("Pending kettle states exceeded their bound");
			IceKettleSync.ResetSessionState();
			if (IceKettleSync.PendingCount != 0)
				return UnitTestResult.Fail("Pending kettle states survived session reset");
			return UnitTestResult.Pass("Kettle retries are atomic, revisioned, bounded, and expiring");
		}

		[UnitTest(name: "Frosty ice kettle exhaust events remain independent", category: "Sync")]
		public static UnitTestResult IceKettleExhaustPendingLifecycle()
		{
			var sequenceNine = Exhaust(94, 9);
			var sequenceTen = Exhaust(94, 10);
			IceKettleSync.ResetSessionState();
			IceKettleSync.QueuePendingExhaust(sequenceNine, 10f);
			IceKettleSync.QueuePendingExhaust(sequenceTen, 11f);
			IceKettleSync.QueuePendingExhaust(sequenceNine, 12f);
			float afterNineExpires = 10f + IceKettleSync.PendingLifetimeSeconds + 0.5f;
			if (IceKettleSync.PendingExhaustCount != 2 ||
			    IceKettleSync.TryGetPendingExhaust(94, 9, afterNineExpires) ||
			    !IceKettleSync.TryGetPendingExhaust(94, 10, afterNineExpires))
				return UnitTestResult.Fail("Independent exhaust sequences were coalesced or their TTL was refreshed");

			IceKettleSync.ResetSessionState();
			if (!IceKettleSync.TryMarkExhaustApplied(94, 10) ||
			    IceKettleSync.TryMarkExhaustApplied(94, 10) ||
			    !IceKettleSync.TryMarkExhaustApplied(94, 9))
				return UnitTestResult.Fail("Exhaust replay protection is not exact per target and sequence");

			IceKettleSync.ResetSessionState();
			for (int i = 0; i <= IceKettleSync.MaxPendingExhausts; i++)
				IceKettleSync.QueuePendingExhaust(Exhaust(95, (ulong)i + 1), 20f);
			if (IceKettleSync.PendingExhaustCount != IceKettleSync.MaxPendingExhausts)
				return UnitTestResult.Fail("Pending exhaust events exceeded their bound");
			IceKettleSync.ResetSessionState();
			if (IceKettleSync.PendingExhaustCount != 0 ||
			    !IceKettleSync.TryMarkExhaustApplied(94, 10))
				return UnitTestResult.Fail("Exhaust lifecycle state survived session reset");
			return UnitTestResult.Pass("Exhaust events retain every sequence, expire, cap, deduplicate, and reset");
		}

		[UnitTest(name: "Frosty space tree branch choice is host authoritative", category: "Sync")]
		public static UnitTestResult SpaceTreeBranchRoundtrip()
		{
			var packet = new SpaceTreeBranchStatePacket
			{
				TrunkNetId = 81,
				Slot = 3,
				BranchNetId = 82,
				PrefabHash = 83,
				Position = new Vector3(7f, 9f, -1f),
				Growth = 0.625f
			};
			SpaceTreeBranchStatePacket copy = Roundtrip(packet, new SpaceTreeBranchStatePacket());
			if (copy is not IHostOnlyPacket || copy.TrunkNetId != 81 || copy.Slot != 3 ||
			    copy.BranchNetId != 82 || copy.PrefabHash != 83 || copy.Position != packet.Position ||
			    copy.Growth != 0.625f)
				return UnitTestResult.Fail("Space tree branch relation or growth did not roundtrip");
			if (!SpaceTreeBranchSync.ShouldRunGameplay(false, false) ||
			    !SpaceTreeBranchSync.ShouldRunGameplay(true, true) ||
			    SpaceTreeBranchSync.ShouldRunGameplay(true, false))
				return UnitTestResult.Fail("Client random branch selection was not suppressed");
			if (!Matches(typeof(PlantBranchGrower.Instance),
			    nameof(PlantBranchGrower.Instance.SpawnRandomBranch), typeof(bool), typeof(float)))
				return UnitTestResult.Fail("PlantBranchGrower Harmony target signature changed");
			packet.Growth = 1.1f;
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Out-of-range branch growth was accepted");
			return UnitTestResult.Pass("Space tree slot, identity and growth are bounded host state");
		}

		private static GeothermalElementState Element(SimHashes element, float mass, bool solid)
			=> new()
			{
				Element = element,
				Mass = mass,
				Temperature = 500f,
				IsSolid = solid,
				DiseaseIndex = byte.MaxValue
			};

		private static IceKettleStorageState Storage(IceKettleItemState item)
			=> new() { Items = new List<IceKettleItemState> { item } };

		private static IceKettleExhaustPacket Exhaust(int targetNetId, ulong sequence)
			=> new()
			{
				TargetNetId = targetNetId,
				Exhaust = new IceKettleExhaustState
				{
					Element = SimHashes.CarbonDioxide,
					Mass = 0.8f,
					Temperature = 290f,
					Sequence = sequence
				}
			};

		private static IceKettleItemState Item(
			int netId, int tagHash, float mass, float temperature, byte diseaseIndex, int diseaseCount)
			=> new()
			{
				NetId = netId,
				TagHash = tagHash,
				Mass = mass,
				Temperature = temperature,
				DiseaseIndex = diseaseIndex,
				DiseaseCount = diseaseCount
			};

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
