using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Prehistoric;
using ONI_Together.Patches.Duplicant;
using ONI_Together.Patches.DLC.Prehistoric;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class PrehistoricSyncTests
	{
		[UnitTest(name: "Prehistoric Harmony targets match game signatures", category: "Sync")]
		public static UnitTestResult HarmonyTargetSignatures()
		{
			if (!Matches(typeof(FossilBits), nameof(FossilBits.OnSidescreenButtonPressed), typeof(void)) ||
			    !Matches(typeof(FossilBits), "DropLoot", typeof(void)) ||
			    !Matches(typeof(MinorFossilDigSite.Instance), "OnExcavateButtonPressed", typeof(void)) ||
			    !Matches(typeof(MajorFossilDigSite.Instance), "OnExcavateButtonPressed", typeof(void)) ||
			    !Matches(typeof(MinorFossilDigSite), "DropLoot", typeof(void), typeof(MinorFossilDigSite.Instance)) ||
			    !Matches(typeof(LargeImpactorStatus), "CheckArrivalUpdate", typeof(bool),
				    typeof(LargeImpactorStatus.Instance), typeof(float)))
				return UnitTestResult.Fail("A Prehistoric Harmony target signature changed");

			return UnitTestResult.Pass("All Prehistoric Harmony targets match the game assembly");
		}

		[UnitTest(name: "Prehistoric fossil requests and states enforce authority", category: "Sync")]
		public static UnitTestResult AuthorityMarkers()
		{
			if (new FossilMarkerRequestPacket() is not IClientRelayable)
				return UnitTestResult.Fail("Fossil request is not client-relayable");
			if (new FossilMarkerStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("Fossil state is not host-only");

			var directClient = new DispatchContext(42, false);
			DispatchContext verifiedClient = directClient.AsVerifiedHostBroadcast();
			if (FossilMarkerRequestPacket.ShouldAccept(true, directClient, true) ||
			    FossilMarkerRequestPacket.ShouldAccept(true, verifiedClient, false) ||
			    !FossilMarkerRequestPacket.ShouldAccept(true, verifiedClient, true) ||
			    FossilMarkerRequestPacket.ShouldAccept(false, verifiedClient, true))
				return UnitTestResult.Fail("Fossil request transport/protocol gate is incorrect");
			if (!FossilMarkerStatePacket.ShouldApply(false, true) ||
			    FossilMarkerStatePacket.ShouldApply(true, true) ||
			    FossilMarkerStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Fossil state authority gate is incorrect");

			return UnitTestResult.Pass("Requests require verified clients and states require the host");
		}

		[UnitTest(name: "Prehistoric fossil marker payload is bounded absolute state", category: "Sync")]
		public static UnitTestResult PacketBoundsAndAbsoluteState()
		{
			var input = new FossilMarkerStatePacket(new FossilMarkerPacketData
			{
				TargetKind = FossilMarkerTarget.MinorFossilDigSite,
				TargetNetId = -17,
				MarkedForDig = true
			});
			var output = new FossilMarkerStatePacket();
			using (var stream = new MemoryStream())
			{
				using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
					input.Serialize(writer);
				stream.Position = 0;
				using var reader = new BinaryReader(stream);
				output.Deserialize(reader);
				if (stream.Position != stream.Length)
					return UnitTestResult.Fail("Fossil payload left unread bytes");
			}

			if (output.Data.TargetKind != FossilMarkerTarget.MinorFossilDigSite ||
			    output.Data.TargetNetId != -17 || !output.Data.MarkedForDig ||
			    FossilMarkerSync.NeedsApply(true, true) || !FossilMarkerSync.NeedsApply(false, true))
				return UnitTestResult.Fail("Fossil absolute state did not roundtrip idempotently");

			try
			{
				new FossilMarkerStatePacket(new FossilMarkerPacketData
				{
					TargetKind = (FossilMarkerTarget)byte.MaxValue,
					TargetNetId = 1
				}).Serialize(new BinaryWriter(new MemoryStream()));
				return UnitTestResult.Fail("Unknown fossil target kind was serialized");
			}
			catch (InvalidDataException)
			{
				return UnitTestResult.Pass("Fossil state is fixed-size, validated and idempotent");
			}
		}

		[UnitTest(name: "Prehistoric major fossil marker roundtrips absolute state", category: "Sync")]
		public static UnitTestResult MajorFossilMarkerRoundtrip()
		{
			var input = new FossilMarkerStatePacket(new FossilMarkerPacketData
			{
				TargetKind = FossilMarkerTarget.MajorFossilDigSite,
				TargetNetId = -31,
				MarkedForDig = true
			});
			var output = new FossilMarkerStatePacket();
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);

			if (output.Data.TargetKind != FossilMarkerTarget.MajorFossilDigSite ||
			    output.Data.TargetNetId != -31 || !output.Data.MarkedForDig ||
			    stream.Position != stream.Length)
				return UnitTestResult.Fail("Major fossil marker did not roundtrip as absolute state");

			return UnitTestResult.Pass("Major fossil marker roundtrips as absolute state");
		}

		[UnitTest(name: "Prehistoric gameplay advances only on host", category: "Sync")]
		public static UnitTestResult HostOnlyGameplay()
		{
			if (!FossilMarkerSync.ShouldRunAuthoritativeGameplay(false, false) ||
			    !FossilMarkerSync.ShouldRunAuthoritativeGameplay(true, true) ||
			    FossilMarkerSync.ShouldRunAuthoritativeGameplay(true, false) ||
			    !LargeImpactorSync.ShouldRunAuthoritativeGameplay(false, false) ||
			    !LargeImpactorSync.ShouldRunAuthoritativeGameplay(true, true) ||
			    LargeImpactorSync.ShouldRunAuthoritativeGameplay(true, false))
				return UnitTestResult.Fail("Client gameplay authority gate is incorrect");

			return UnitTestResult.Pass("Client loot and impactor arrival updates are suppressed in-session");
		}

		[UnitTest(name: "Prehistoric mosquito target is bounded host state", category: "Sync")]
		public static UnitTestResult MosquitoTargetState()
		{
			var input = new MosquitoTargetStatePacket
			{
				MosquitoNetId = -71,
				HasVictim = true,
				VictimNetId = 72
			};
			var output = new MosquitoTargetStatePacket();
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);

			if (output.MosquitoNetId != -71 || !output.HasVictim || output.VictimNetId != 72 ||
			    stream.Position != stream.Length || output is not IHostOnlyPacket)
				return UnitTestResult.Fail("Mosquito target state did not roundtrip");
			if (!MosquitoTargetStatePacket.ShouldApply(false, true) ||
			    MosquitoTargetStatePacket.ShouldApply(true, true) ||
			    MosquitoTargetStatePacket.ShouldApply(false, false) ||
			    MosquitoHungerSync.ShouldRunSelection(true, false))
				return UnitTestResult.Fail("Mosquito target authority gate is incorrect");

			input.HasVictim = false;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Mosquito target accepted a victim ID while cleared");
			return UnitTestResult.Pass("Mosquito selection is bounded host-only absolute state");
		}

		[UnitTest(name: "Carnivorous plant pending state is best-effort and expires", category: "Sync")]
		public static UnitTestResult CarnivorousPlantPendingState()
		{
			var first = new CarnivorousPlantStatePacket
			{
				Kind = CarnivorousPlantKind.Flytrap,
				PlantNetId = 91,
				VictimNetId = 92,
				HasEatenCreature = true,
				LastConsumedPrefabId = "Hatch"
			};
			var latest = new CarnivorousPlantStatePacket
			{
				Kind = CarnivorousPlantKind.Flytrap,
				PlantNetId = 91
			};

			CarnivorousPlantSync.ResetSessionState();
			CarnivorousPlantSync.QueuePending(first, 10f);
			CarnivorousPlantSync.QueuePending(latest, 11f);
			if (CarnivorousPlantSync.PendingCount != 1 ||
			    !ReferenceEquals(CarnivorousPlantSync.GetPending(91, 11f), latest))
				return UnitTestResult.Fail("Pending plant state did not keep only the latest packet");
			if (CarnivorousPlantSync.CanMutate(plantResolved: false) ||
			    !CarnivorousPlantSync.CanMutate(plantResolved: true))
				return UnitTestResult.Fail("A missing victim blocked an otherwise resolved plant outcome");
			if (CarnivorousPlantSync.GetPending(91,
			    11f + CarnivorousPlantSync.PendingLifetimeSeconds + 1f) != null)
				return UnitTestResult.Fail("Pending plant state did not expire");

			for (int i = 0; i <= CarnivorousPlantSync.MaxPendingStates; i++)
			{
				CarnivorousPlantSync.QueuePending(new CarnivorousPlantStatePacket
				{
					Kind = CarnivorousPlantKind.CritterTrap,
					PlantNetId = 1000 + i
				}, 20f);
			}
			if (CarnivorousPlantSync.PendingCount != CarnivorousPlantSync.MaxPendingStates)
				return UnitTestResult.Fail("Pending plant states exceeded their bound");
			CarnivorousPlantSync.ResetSessionState();
			if (CarnivorousPlantSync.PendingCount != 0)
				return UnitTestResult.Fail("Pending plant states survived session reset");
			return UnitTestResult.Pass("Plant retries are latest-only, bounded, expiring, and victim best-effort");
		}

		[UnitTest(name: "Entity effects advance only from host state", category: "Sync")]
		public static UnitTestResult EntityEffectAuthority()
		{
			if (!EffectsPatch.ShouldRunMutation(false, false, false, true) ||
			    !EffectsPatch.ShouldRunMutation(true, true, false, true) ||
			    EffectsPatch.ShouldRunMutation(true, false, false, true) ||
			    !EffectsPatch.ShouldRunMutation(true, false, true, true) ||
			    !EffectsPatch.ShouldRunMutation(true, false, false, false))
				return UnitTestResult.Fail("Entity effect authority gate is incorrect");

			var packet = new ToggleEffectPacket
			{
				MinionNetId = -81,
				EffectId = MosquitoHungerMonitor.MosquitoFedEffectName,
				IsAdding = true,
				ShouldSave = true,
				TimeRemaining = 321.5f
			};
			if (!packet.IsWireValid() || !ToggleEffectPacket.ShouldApply(false, true) ||
			    ToggleEffectPacket.ShouldApply(true, true) ||
			    ToggleEffectPacket.ShouldApply(false, false) || packet is not IHostOnlyPacket)
				return UnitTestResult.Fail("Entity effect packet authority is incorrect");

			var output = new ToggleEffectPacket();
			using (var stream = new MemoryStream())
			{
				using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
					packet.Serialize(writer);
				stream.Position = 0;
				using var reader = new BinaryReader(stream);
				output.Deserialize(reader);
				if (output.TimeRemaining != 321.5f || stream.Position != stream.Length)
					return UnitTestResult.Fail("Entity effect duration did not roundtrip");
			}

			packet.TimeRemaining = float.NaN;
			if (packet.IsWireValid())
				return UnitTestResult.Fail("NaN effect duration was accepted");
			packet.TimeRemaining = 0f;
			packet.EffectId = string.Empty;
			if (packet.IsWireValid())
				return UnitTestResult.Fail("Empty effect ID was accepted");
			if (!EffectsPatch.ShouldPredictLocally(true, false, false, true) ||
			    EffectsPatch.ShouldPredictLocally(true, true, false, true) ||
			    EffectsPatch.ShouldPredictLocally(false, false, false, true))
				return UnitTestResult.Fail("Client effect prediction gate is incorrect");
			return UnitTestResult.Pass("Entity effects use bounded host snapshots with safe client prediction");
		}

		private static bool Matches(Type type, string name, Type returnType, params Type[] parameters)
		{
			MethodInfo method = AccessTools.Method(type, name, parameters);
			return method != null && method.ReturnType == returnType;
		}
	}
}
