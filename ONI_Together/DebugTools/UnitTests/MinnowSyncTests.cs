using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Patches.DLC.Aquatic;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class MinnowSyncTests
	{
		[UnitTest(name: "Minnow quest packets enforce host authority", category: "Sync")]
		public static UnitTestResult Authority()
		{
			var direct = new DispatchContext(42, false);
			var verified = direct.AsVerifiedHostBroadcast();
			if (new MinnowPoiRequestPacket() is not IClientRelayable ||
			    new MinnowPoiStatePacket() is not IHostOnlyPacket ||
			    new MinnowSpawnStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("A Minnow packet has the wrong authority marker");
			if (MinnowPoiRequestPacket.ShouldAccept(true, direct, true) ||
			    MinnowPoiRequestPacket.ShouldAccept(true, verified, false) ||
			    !MinnowPoiRequestPacket.ShouldAccept(true, verified, true) ||
			    MinnowPoiRequestPacket.ShouldAccept(false, verified, true) ||
			    !MinnowPoiStatePacket.ShouldApply(false, true) ||
			    MinnowPoiStatePacket.ShouldApply(true, true) ||
			    !MinnowSpawnStatePacket.ShouldApply(false, true, false) ||
			    MinnowSpawnStatePacket.ShouldApply(false, true, true))
				return UnitTestResult.Fail("Minnow authority gate is incorrect");
			return UnitTestResult.Pass("Minnow mutations run on the host and clients accept host state only");
		}

		[UnitTest(name: "Minnow request and absolute quest state roundtrip", category: "Sync")]
		public static UnitTestResult QuestRoundtrip()
		{
			MinnowPoiRequestPacket request = Roundtrip(new MinnowPoiRequestPacket
			{
				TargetNetId = 17,
				Operation = MinnowPoiOperation.AcknowledgeCompletion
			}, new MinnowPoiRequestPacket());
			if (request.TargetNetId != 17 || request.Operation != MinnowPoiOperation.AcknowledgeCompletion)
				return UnitTestResult.Fail("Minnow request did not roundtrip");

			MinnowPoiStatePacket state = Roundtrip(new MinnowPoiStatePacket
			{
				TargetNetId = 17,
				Phase = MinnowPoiPhase.Completed,
				HasShownQuestPopup = true,
				HasShownCompletedPopup = true,
				IsCompleted = true,
				DeliveryEnabled = false,
				QuestsCompleted = 3,
				AllQuestsCompleted = true
			}, new MinnowPoiStatePacket());
			if (state.TargetNetId != 17 || state.Phase != MinnowPoiPhase.Completed ||
			    !state.HasShownQuestPopup || !state.HasShownCompletedPopup || !state.IsCompleted ||
			    state.DeliveryEnabled || state.QuestsCompleted != 3 || !state.AllQuestsCompleted)
				return UnitTestResult.Fail("Minnow absolute quest state did not roundtrip");
			return UnitTestResult.Pass("Minnow requests and absolute quest state roundtrip exactly");
		}

		[UnitTest(name: "Minnow duplicant spawn state roundtrips exact stats", category: "Sync")]
		public static UnitTestResult SpawnRoundtrip()
		{
			var stats = new ImmigrantOptionEntry
			{
				EntryType = 0,
				Name = "Minnow",
				PersonalityId = "MINNOW",
				TraitIds = new List<string> { "AncientKnowledge" },
				StressTraitId = "BingeEat",
				JoyTraitId = "BalloonArtist",
				VoiceIdx = 1,
				StickerType = string.Empty,
				SkillAptitudes = new Dictionary<string, float>(),
				StartingLevels = new Dictionary<string, int> { ["Athletics"] = 4 }
			};
			MinnowSpawnStatePacket packet = Roundtrip(new MinnowSpawnStatePacket
			{
				SourceNetId = 17,
				MinionNetId = 23,
				LifecycleRevision = 24,
				Position = new Vector3(1f, 2f, 3f),
				ArrivalTime = -2100f,
				SkillPoints = 3,
				EntityData = stats
			}, new MinnowSpawnStatePacket());
			if (packet.SourceNetId != 17 || packet.MinionNetId != 23 || packet.LifecycleRevision != 24 ||
			    packet.Position != new Vector3(1f, 2f, 3f) || packet.ArrivalTime != -2100f ||
			    packet.SkillPoints != 3 || packet.EntityData.PersonalityId != "MINNOW" ||
			    packet.EntityData.StartingLevels["Athletics"] != 4)
				return UnitTestResult.Fail("Minnow spawn state did not roundtrip");
			return UnitTestResult.Pass("Minnow identity, stats, arrival time, and skill points roundtrip exactly");
		}

		[UnitTest(name: "Minnow state application is idempotent", category: "Sync")]
		public static UnitTestResult Idempotence()
		{
			var current = new MinnowPoiSnapshot(17, MinnowPoiPhase.Working, true, false, false, true, 1, false);
			var changed = new MinnowPoiSnapshot(17, MinnowPoiPhase.Completed, true, true, true, false, 3, true);
			if (MinnowPoiSync.NeedsApply(current, current) || !MinnowPoiSync.NeedsApply(current, changed) ||
			    !MinnowSpawnStatePacket.ShouldApply(false, true, false) ||
			    MinnowSpawnStatePacket.ShouldApply(false, true, true))
				return UnitTestResult.Fail("Minnow idempotence gate is incorrect");
			return UnitTestResult.Pass("Repeated Minnow snapshots and spawn outcomes are idempotent");
		}

		[UnitTest(name: "Minnow Harmony targets match build 740622", category: "Sync")]
		public static UnitTestResult Metadata()
		{
			if (MinnowPoiSync.ResolveShowQuestPopupMethod() == null ||
			    MinnowPoiSync.ResolveCompletionAcknowledgedMethod() == null ||
			    MinnowPoiSync.ResolveSpawnMinnowMethod() == null ||
			    MinnowPoiSync.ResolveSpawnRewardMethod() == null ||
			    MinnowPoiSync.ResolveUnlockAchievementMethod() == null ||
			    MinnowPoiSync.ResolveHasEnoughMassMethod() == null)
				return UnitTestResult.Fail("A Minnow Harmony target changed");
			if (!SameDlc(new MinnowImperativePOIAConfig().GetRequiredDlcIds(), DlcManager.DLC5) ||
			    !SameDlc(new MinnowImperativePOIBConfig().GetRequiredDlcIds(), DlcManager.DLC5) ||
			    !SameDlc(new MinnowImperativePOICConfig().GetRequiredDlcIds(), DlcManager.DLC5))
				return UnitTestResult.Fail("A Minnow POI is no longer DLC5-only");
			return UnitTestResult.Pass("Minnow targets and DLC guard match build 740622");
		}

		private static bool SameDlc(string[] actual, string[] expected)
		{
			if (actual == null || expected == null || actual.Length != expected.Length)
				return false;
			for (int i = 0; i < actual.Length; i++)
				if (actual[i] != expected[i])
					return false;
			return true;
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
	}
}
