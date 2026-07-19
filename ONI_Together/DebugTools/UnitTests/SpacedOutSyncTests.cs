using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SpacedOutSyncTests
	{
		[UnitTest(name: "Spaced Out wire coordinates preserve Q and R", category: "Sync")]
		public static UnitTestResult AxialWireCoordinatesPreserveQr()
		{
			AxialI location = AxialCoordinateSync.FromQr(5, -3);
			return location.q == 5 && location.r == -3
				? UnitTestResult.Pass("Wire Q/R map to the game's reversed AxialI constructor")
				: UnitTestResult.Fail($"Axial coordinate was transposed to {location.q},{location.r}");
		}

		[UnitTest(name: "Spaced Out settings enforce request and host-state authority", category: "Sync")]
		public static UnitTestResult AuthorityMarkers()
		{
			if (new RocketSettingsRequestPacket() is not IClientRelayable ||
			    new ClusterLocationFilterRequestPacket() is not IClientRelayable)
				return UnitTestResult.Fail("A client request is not relayable");
			if (new RocketSettingsStatePacket() is not IHostOnlyPacket ||
			    new ClusterLocationFilterStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("An authoritative state packet is not host-only");

			var directClient = new DispatchContext(42, false);
			DispatchContext verifiedClient = directClient.AsVerifiedHostBroadcast();
			if (RocketSettingsRequestPacket.ShouldAccept(true, directClient) ||
			    ClusterLocationFilterRequestPacket.ShouldAccept(true, directClient) ||
			    !RocketSettingsRequestPacket.ShouldAccept(true, verifiedClient) ||
			    !ClusterLocationFilterRequestPacket.ShouldAccept(true, verifiedClient))
				return UnitTestResult.Fail("Request provenance gate is incorrect");
			if (!RocketSettingsStatePacket.ShouldApply(false, true) ||
			    RocketSettingsStatePacket.ShouldApply(true, true) ||
			    RocketSettingsStatePacket.ShouldApply(false, false) ||
			    !ClusterLocationFilterStatePacket.ShouldApply(false, true) ||
			    ClusterLocationFilterStatePacket.ShouldApply(true, true) ||
			    ClusterLocationFilterStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("State authority gate is incorrect");

			bool nestedGuardWorked = false;
			SpacedOutSyncGuard.Run(() =>
			{
				SpacedOutSyncGuard.Run(() => nestedGuardWorked = SpacedOutSyncGuard.IsApplying);
			});
			if (!nestedGuardWorked || SpacedOutSyncGuard.IsApplying)
				return UnitTestResult.Fail("Recursive apply guard did not unwind");

			return UnitTestResult.Pass("Authority and recursive apply guards are enforced");
		}

		[UnitTest(name: "Spaced Out rocket settings roundtrip absolute state", category: "Sync")]
		public static UnitTestResult RocketSettingsRoundtrip()
		{
			var input = new RocketSettingsRequestPacket(new RocketSettingsPacketData
			{
				TargetKind = RocketSettingsTarget.DestinationSelector,
				TargetNetId = 17,
				HasDestination = true,
				DestinationQ = 5,
				DestinationR = -3,
				HasPad = true,
				PadNetId = 29,
				Repeat = true
			});
			RocketSettingsRequestPacket output = Roundtrip(input, new RocketSettingsRequestPacket());
			RocketSettingsPacketData data = output.Data;
			if (data.TargetKind != RocketSettingsTarget.DestinationSelector || data.TargetNetId != 17 ||
			    !data.HasDestination || data.DestinationQ != 5 || data.DestinationR != -3 ||
			    !data.HasPad || data.PadNetId != 29 || !data.Repeat)
				return UnitTestResult.Fail("Rocket settings did not roundtrip");

			var target = new RocketSettingsPacketData
			{
				TargetKind = RocketSettingsTarget.DestinationSelector,
				TargetNetId = 17,
				HasDestination = true,
				DestinationQ = 5,
				DestinationR = -3,
				HasPad = true,
				PadNetId = 29,
				Repeat = true
			};
			AxialI destination = AxialCoordinateSync.FromQr(5, -3);
			if (RocketSettingsSync.NeedsApply(destination, 29, true, target) ||
			    !RocketSettingsSync.NeedsApply(destination, 0, true, target))
				return UnitTestResult.Fail("Rocket absolute-state idempotence is incorrect");

			return UnitTestResult.Pass("Destination, pad, repeat and target identity roundtrip exactly");
		}

		[UnitTest(name: "Spaced Out rocket apply verifies every absolute field", category: "Sync")]
		public static UnitTestResult RocketSettingsPostconditionCoversEveryField()
		{
			var expected = new RocketSettingsPacketData
			{
				TargetKind = RocketSettingsTarget.DestinationSelector,
				TargetNetId = 17,
				HasDestination = true,
				DestinationQ = 5,
				DestinationR = -3,
				HasPad = true,
				PadNetId = 29,
				Repeat = true,
			};
			var actual = new RocketSettingsPacketData
			{
				TargetKind = expected.TargetKind,
				TargetNetId = expected.TargetNetId,
				HasDestination = expected.HasDestination,
				DestinationQ = expected.DestinationQ,
				DestinationR = expected.DestinationR,
				HasPad = expected.HasPad,
				PadNetId = expected.PadNetId,
				Repeat = expected.Repeat,
			};
			if (!RocketSettingsSync.SnapshotsMatch(expected, actual))
				return UnitTestResult.Fail("Equal selector snapshots did not match");
			actual.PadNetId++;
			if (RocketSettingsSync.SnapshotsMatch(expected, actual))
				return UnitTestResult.Fail("Selector pad drift passed the apply postcondition");

			var station = new RocketSettingsPacketData
			{
				TargetKind = RocketSettingsTarget.ControlStation,
				TargetNetId = 31,
				RestrictWhenGrounded = true,
			};
			var stationDrift = new RocketSettingsPacketData
			{
				TargetKind = RocketSettingsTarget.ControlStation,
				TargetNetId = 31,
				RestrictWhenGrounded = false,
			};
			return !RocketSettingsSync.SnapshotsMatch(station, stationDrift)
				? UnitTestResult.Pass("Selector and station state require exact readback")
				: UnitTestResult.Fail("Station restriction drift passed the apply postcondition");
		}

		[UnitTest(name: "Spaced Out location filters are bounded and idempotent", category: "Sync")]
		public static UnitTestResult LocationFilterBoundsAndIdempotence()
		{
			var input = new ClusterLocationFilterRequestPacket(new ClusterLocationFilterPacketData
			{
				TargetNetId = 51,
				ActiveInSpace = false,
				ActiveLocations = new List<AxialI>
				{
					Location(4, -1),
					Location(-2, 3)
				}
			});
			ClusterLocationFilterRequestPacket output = Roundtrip(input, new ClusterLocationFilterRequestPacket());
			if (output.Data.TargetNetId != 51 || output.Data.ActiveInSpace ||
			    output.Data.ActiveLocations.Count != 2 ||
			    output.Data.ActiveLocations[0] != Location(4, -1) ||
			    output.Data.ActiveLocations[1] != Location(-2, 3))
				return UnitTestResult.Fail("Location filter did not roundtrip");

			List<AxialI> canonical = ClusterLocationFilterSync.Canonicalize(new[]
			{
				Location(4, -1),
				Location(-2, 3),
				Location(4, -1)
			});
			if (canonical.Count != 2 || canonical[0] != Location(-2, 3) ||
			    canonical[1] != Location(4, -1))
				return UnitTestResult.Fail("Location filter canonicalization is unstable");
			if (ClusterLocationFilterSync.NeedsApply(false, canonical, output.Data))
				return UnitTestResult.Fail("Repeated absolute location state was not skipped");

			var invalid = new ClusterLocationFilterPacketData
			{
				TargetNetId = 1,
				ActiveLocations = new List<AxialI> { Location(2000, 0) }
			};
			try
			{
				using var stream = new MemoryStream();
				using var writer = new BinaryWriter(stream);
				invalid.Serialize(writer);
				return UnitTestResult.Fail("Out-of-bounds location was serialized");
			}
			catch (InvalidDataException)
			{
				return UnitTestResult.Pass("Location lists are canonical, bounded and idempotent");
			}
		}

		[UnitTest(name: "Spaced Out artifact inventory is host-authoritative", category: "Sync")]
		public static UnitTestResult ArtifactInventoryRoundtrip()
		{
			var input = new ArtifactInventoryStatePacket
			{
				ModuleNetId = 73,
				LocationQ = 2,
				LocationR = -1,
				Items = new List<ArtifactInventoryItemData>
				{
					new() { Id = "artifact_terrestrial_001", Mass = 1f, State = Element.State.Vacuum }
				}
			};
			ArtifactInventoryStatePacket output = Roundtrip(input, new ArtifactInventoryStatePacket());
			if (output is not IHostOnlyPacket || output.ModuleNetId != 73 ||
			    output.LocationQ != 2 || output.LocationR != -1 || output.Items.Count != 1 ||
			    output.Items[0].Id != "artifact_terrestrial_001" || output.Items[0].Mass != 1f)
				return UnitTestResult.Fail("Artifact inventory did not roundtrip as host state");

			input.Items[0].Mass = -1f;
			try
			{
				using var stream = new MemoryStream();
				using var writer = new BinaryWriter(stream);
				input.Serialize(writer);
				return UnitTestResult.Fail("Negative artifact inventory mass was serialized");
			}
			catch (InvalidDataException)
			{
				return UnitTestResult.Pass("Artifact inventory state is bounded and host-only");
			}
		}

		[UnitTest(name: "Spaced Out artifact POI replenishment is host state", category: "Sync")]
		public static UnitTestResult ArtifactPoiRoundtrip()
		{
			var input = new ArtifactPoiStatePacket
			{
				TargetNetId = 91,
				LifecycleRevision = 92,
				LocationQ = -3,
				LocationR = 4,
				PoiCharge = 0.25f,
				NumHarvests = 7,
				ArtifactToHarvest = "artifact_space_001",
				Items = new List<ArtifactInventoryItemData>
				{
					new() { Id = "artifact_space_001", Mass = 1f, State = Element.State.Vacuum }
				},
				Selector = new ArtifactSelectorStateData
				{
					Space = new List<string> { "artifact_space_001" },
					AnalyzedIds = new List<string> { "artifact_terrestrial_001" },
					AnalyzedTerrestrialCount = 1
				}
			};
			ArtifactPoiStatePacket output = Roundtrip(input, new ArtifactPoiStatePacket());
			if (output is not IHostOnlyPacket || output.TargetNetId != 91 ||
			    output.LifecycleRevision != 92 || output.LocationQ != -3 ||
			    output.PoiCharge != 0.25f || output.NumHarvests != 7 || output.Items.Count != 1 ||
			    output.Selector.Space.Count != 1 || output.Selector.AnalyzedIds.Count != 1)
				return UnitTestResult.Fail("Artifact POI inventory or selector state did not roundtrip");
			input.PoiCharge = float.NaN;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Non-finite artifact POI charge was accepted");
			if (!Matches(typeof(ArtifactPOIStates), nameof(ArtifactPOIStates.SpawnArtifactOnHexCellIfFullyCharged),
			    typeof(void), typeof(ArtifactPOIStates.Instance)) ||
			    !Matches(typeof(ArtifactPOIStates.Instance), nameof(ArtifactPOIStates.Instance.RechargePOI),
				    typeof(void), typeof(float)))
				return UnitTestResult.Fail("Artifact POI Harmony target signature changed");
			return UnitTestResult.Pass("Artifact POI inventory, charge and selector are bounded host state");
		}

		[UnitTest(name: "Spaced Out studyable uses request and host state", category: "Sync")]
		public static UnitTestResult StudyableRoundtrip()
		{
			var request = new StudyableRequestPacket
			{
				TargetNetId = 101, ExpectedMarked = false, DesiredMarked = true
			};
			StudyableRequestPacket requestCopy = Roundtrip(request, new StudyableRequestPacket());
			var state = new StudyableStatePacket
			{
				TargetNetId = 101, MarkedForStudy = true, Studied = false
			};
			StudyableStatePacket stateCopy = Roundtrip(state, new StudyableStatePacket());
			if (requestCopy is not IClientRelayable || stateCopy is not IHostOnlyPacket ||
			    requestCopy.TargetNetId != 101 || !requestCopy.DesiredMarked ||
			    stateCopy.TargetNetId != 101 || !stateCopy.MarkedForStudy || stateCopy.Studied)
				return UnitTestResult.Fail("Studyable request or absolute state did not roundtrip");
			var direct = new DispatchContext(5, false);
			if (StudyableRequestPacket.ShouldAccept(true, direct) ||
			    !StudyableRequestPacket.ShouldAccept(true, direct.AsVerifiedHostBroadcast()))
				return UnitTestResult.Fail("Studyable request provenance gate is incorrect");
			if (!Matches(typeof(Studyable), nameof(Studyable.OnSidescreenButtonPressed), typeof(void)) ||
			    !Matches(typeof(Studyable), "OnCompleteWork", typeof(void), typeof(WorkerBase)))
				return UnitTestResult.Fail("Studyable Harmony target signature changed");
			return UnitTestResult.Pass("Study designation and completion use verified host authority");
		}

		[UnitTest(name: "Spaced Out cryo tank activation and minion are host state", category: "Sync")]
		public static UnitTestResult CryoTankRoundtrip()
		{
			var request = new CryoTankActivationRequestPacket { TargetNetId = 111 };
			CryoTankActivationRequestPacket requestCopy = Roundtrip(
				request, new CryoTankActivationRequestPacket());
			var state = new CryoTankStatePacket
			{
				TargetNetId = 111,
				Phase = CryoTankPhase.Defrost,
				OpenerNetId = 112,
				MinionNetId = 113,
				MinionLifecycleRevision = 114,
				Position = new UnityEngine.Vector3(8f, 10f, 0f),
				ArrivalTime = -1500f,
				EntityData = Duplicant("Nisbet", "NISBET")
			};
			CryoTankStatePacket stateCopy = Roundtrip(state, new CryoTankStatePacket());
			if (requestCopy is not IClientRelayable || stateCopy is not IHostOnlyPacket ||
			    requestCopy.TargetNetId != 111 || stateCopy.Phase != CryoTankPhase.Defrost ||
			    stateCopy.OpenerNetId != 112 || stateCopy.MinionNetId != 113 ||
			    stateCopy.MinionLifecycleRevision != 114 ||
			    stateCopy.EntityData.Name != "Nisbet")
				return UnitTestResult.Fail("Cryo tank activation, phase or minion did not roundtrip");
			var direct = new DispatchContext(6, false);
			if (CryoTankActivationRequestPacket.ShouldAccept(true, direct) ||
			    !CryoTankActivationRequestPacket.ShouldAccept(true, direct.AsVerifiedHostBroadcast()))
				return UnitTestResult.Fail("Cryo tank request provenance gate is incorrect");
			state.ArrivalTime = float.PositiveInfinity;
			if (state.IsWireValid())
				return UnitTestResult.Fail("Non-finite cryo arrival time was accepted");
			if (!Matches(typeof(CryoTank), nameof(CryoTank.DropContents), typeof(void)) ||
			    !Matches(typeof(CryoTank), nameof(CryoTank.ActivateChore), typeof(void), typeof(object)) ||
			    !Matches(typeof(MinionStartingStats), nameof(MinionStartingStats.Apply),
				    typeof(void), typeof(UnityEngine.GameObject)))
				return UnitTestResult.Fail("Cryo tank Harmony target signature changed");
			return UnitTestResult.Pass("Cryo activation and generated duplicant are bounded host state");
		}

		private static ImmigrantOptionEntry Duplicant(string name, string personalityId)
			=> new()
			{
				EntryType = 0,
				Name = name,
				PersonalityId = personalityId,
				TraitIds = new List<string> { "AncientKnowledge" },
				StressTraitId = "StressVomiter",
				JoyTraitId = "BalloonArtist",
				StickerType = "sticker_basic",
				SkillAptitudes = new Dictionary<string, float>(),
				StartingLevels = new Dictionary<string, int>()
			};

		private static AxialI Location(int q, int r) => AxialCoordinateSync.FromQr(q, r);

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
