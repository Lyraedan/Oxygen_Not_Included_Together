using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.DLC;
using ONI_Together.Patches.DLC.Prehistoric;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class LargeImpactorSyncTests
	{
		[UnitTest(name: "Large impactor accepts only host authority", category: "Sync")]
		public static UnitTestResult HostAuthority()
		{
			if (!LargeImpactorSync.ShouldRunAuthoritativeGameplay(false, false))
				return UnitTestResult.Fail("Offline impactor gameplay was blocked");
			if (!LargeImpactorSync.ShouldRunAuthoritativeGameplay(true, true))
				return UnitTestResult.Fail("Host impactor gameplay was blocked");
			if (LargeImpactorSync.ShouldRunAuthoritativeGameplay(true, false))
				return UnitTestResult.Fail("Client impactor gameplay was accepted");
			if (!LargeImpactorStatePacket.ShouldApply(false, true) ||
			    LargeImpactorStatePacket.ShouldApply(true, true) ||
			    LargeImpactorStatePacket.ShouldApply(false, false) ||
			    !LargeImpactorOutcomePacket.ShouldApply(false, true) ||
			    LargeImpactorOutcomePacket.ShouldApply(true, true) ||
			    LargeImpactorOutcomePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Packet authority gate is incorrect");

			return UnitTestResult.Pass("Only the host runs impactor gameplay and clients accept only host state");
		}

		[UnitTest(name: "Large impactor absolute state is idempotent", category: "Sync")]
		public static UnitTestResult AbsoluteStateIsIdempotent()
		{
			if (LargeImpactorStatePacket.NeedsApply(
				    900, false, LargeImpactorPhase.Alive,
				    900, false, LargeImpactorPhase.Alive))
				return UnitTestResult.Fail("Identical absolute state was not skipped");
			if (!LargeImpactorStatePacket.NeedsApply(
				    900, false, LargeImpactorPhase.Alive,
				    700, false, LargeImpactorPhase.Alive))
				return UnitTestResult.Fail("Changed absolute health was skipped");
			if (!LargeImpactorStatePacket.NeedsApply(
				    700, false, LargeImpactorPhase.Alive,
				    700, true, LargeImpactorPhase.Landing))
				return UnitTestResult.Fail("Changed impactor phase was skipped");

			return UnitTestResult.Pass("Repeated absolute state is a no-op while changed state applies");
		}

		[UnitTest(name: "Large impactor outcome roundtrip is complete", category: "Sync")]
		public static UnitTestResult OutcomeRoundtrip()
		{
			LargeImpactorOutcomePacket output = Roundtrip(CreatePacket());
			if (!output.IsValid || output.EventId != 71 || output.WorldId != 3)
				return UnitTestResult.Fail("Outcome identity did not roundtrip");
			if (output.Pois.Count != 1 || output.Pois[0].PrefabId != "TestPoi" ||
			    output.Pois[0].Q != 5 || output.Pois[0].R != -2)
				return UnitTestResult.Fail("POI outcome did not roundtrip");
			if (output.Destinations.Count != 1)
				return UnitTestResult.Fail("Destination outcome did not roundtrip");

			LargeImpactorDestinationData destination = output.Destinations[0];
			if (destination.Id != 99 || destination.Type != "TestDestination" ||
			    !destination.StartAnalyzed || destination.Distance != 4 ||
			    destination.ActivePeriod != 2.5f || destination.InactivePeriod != 1.25f ||
			    destination.StartingOrbitPercentage != 0.75f || destination.AvailableMass != 1200f)
				return UnitTestResult.Fail("Destination scalar state did not roundtrip");
			if (destination.RecoverableElements.Count != 1 ||
			    destination.RecoverableElements[0].Element != (SimHashes)42 ||
			    destination.RecoverableElements[0].Mass != 33.5f)
				return UnitTestResult.Fail("Destination element state did not roundtrip");
			if (destination.ResearchOpportunities.Count != 1 ||
			    destination.ResearchOpportunities[0].Description != "Test research" ||
			    destination.ResearchOpportunities[0].DataValue != 8 ||
			    !destination.ResearchOpportunities[0].Completed ||
			    destination.ResearchOpportunities[0].DiscoveredRareResource != (SimHashes)43 ||
			    destination.ResearchOpportunities[0].DiscoveredRareItem != "TestItem")
				return UnitTestResult.Fail("Destination research state did not roundtrip");

			return UnitTestResult.Pass("POI and complete destination state roundtrip without rerolling");
		}

		[UnitTest(name: "Large impactor outcome rejects duplicate destination IDs", category: "Sync")]
		public static UnitTestResult RejectsDuplicateDestinationIds()
		{
			LargeImpactorOutcomePacket input = CreatePacket();
			input.Destinations.Add(CreateDestination());
			LargeImpactorOutcomePacket output = Roundtrip(input);
			if (output.IsValid)
				return UnitTestResult.Fail("Duplicate destination ID was accepted");
			if (!LargeImpactorOutcomePacket.ContainsDestinationId(new List<int> { 10, 99 }, 99))
				return UnitTestResult.Fail("Existing destination ID was not detected");
			if (LargeImpactorOutcomePacket.ContainsDestinationId(new List<int> { 10, 99 }, 100))
				return UnitTestResult.Fail("Unknown destination ID was treated as existing");

			var destinations = new List<SpaceDestination>();
			int notifications = 0;
			if (!LargeImpactorOutcomePacket.InsertDestination(
				    destinations, CreateDestination(), _ => notifications++))
				return UnitTestResult.Fail("New destination was not inserted");
			if (LargeImpactorOutcomePacket.InsertDestination(
				    destinations, CreateDestination(), _ => notifications++))
				return UnitTestResult.Fail("Duplicate destination was inserted");
			if (destinations.Count != 1 || notifications != 1)
				return UnitTestResult.Fail("Destination insertion notification was not exactly once");

			return UnitTestResult.Pass("Duplicate IDs are rejected and exact inserts notify once");
		}

		private static LargeImpactorOutcomePacket CreatePacket()
		{
			return new LargeImpactorOutcomePacket
			{
				EventId = 71,
				WorldId = 3,
				Pois = new List<LargeImpactorPoiOutcome>
				{
					new() { PrefabId = "TestPoi", Q = 5, R = -2 }
				},
				Destinations = new List<LargeImpactorDestinationData> { CreateDestination() }
			};
		}

		private static LargeImpactorDestinationData CreateDestination()
		{
			return new LargeImpactorDestinationData
			{
				Id = 99,
				Type = "TestDestination",
				StartAnalyzed = true,
				Distance = 4,
				ActivePeriod = 2.5f,
				InactivePeriod = 1.25f,
				StartingOrbitPercentage = 0.75f,
				AvailableMass = 1200f,
				RecoverableElements = new List<LargeImpactorElementData>
				{
					new() { Element = (SimHashes)42, Mass = 33.5f }
				},
				ResearchOpportunities = new List<LargeImpactorResearchData>
				{
					new()
					{
						Description = "Test research",
						DataValue = 8,
						Completed = true,
						DiscoveredRareResource = (SimHashes)43,
						DiscoveredRareItem = "TestItem"
					}
				}
			};
		}

		private static LargeImpactorOutcomePacket Roundtrip(LargeImpactorOutcomePacket input)
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			input.Serialize(writer);
			writer.Flush();
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			var output = new LargeImpactorOutcomePacket();
			output.Deserialize(reader);
			return output;
		}
	}
}
