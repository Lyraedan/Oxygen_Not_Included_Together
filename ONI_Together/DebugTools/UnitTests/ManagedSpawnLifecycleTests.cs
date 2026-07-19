using System.Collections.Generic;
using System.IO;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Patches.DLC.SpacedOut;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ManagedSpawnLifecycleTests
	{
		[UnitTest(name: "Managed spawn suppression is nested and resettable", category: "Sync")]
		public static UnitTestResult ManagedSpawnSuppressionScope()
		{
			NetworkIdentity.ResetSessionState();
			NetworkIdentity.BeginManagedSpawn();
			NetworkIdentity.BeginManagedSpawn();
			NetworkIdentity.EndManagedSpawn();
			if (!NetworkIdentity.IsManagedSpawnSuppressed)
				return UnitTestResult.Fail("Nested managed spawn scope ended too early");
			NetworkIdentity.ResetSessionState();
			if (NetworkIdentity.IsManagedSpawnSuppressed)
				return UnitTestResult.Fail("Session reset retained managed spawn suppression");
			return UnitTestResult.Pass("Managed spawn suppression is balanced and session-scoped");
		}

		[UnitTest(name: "Telepad spawn carries lifecycle and rejects stale materialization", category: "Sync")]
		public static UnitTestResult TelepadLifecycleRoundtrip()
		{
			var input = new TelepadEntitySpawnPacket
			{
				NetId = 71,
				Revision = 9,
				Pos = new Vector3(12f, 4f, 0f),
				EntityData = Duplicant()
			};
			TelepadEntitySpawnPacket output = Roundtrip(input);
			if (output.NetId != 71 || output.Revision != 9 || output.EntityData.Name != "Ada")
				return UnitTestResult.Fail("Telepad lifecycle metadata did not roundtrip");
			if (TelepadEntitySpawnPacket.ShouldMaterialize(true, true, 0, false, 9, false) ||
			    TelepadEntitySpawnPacket.ShouldMaterialize(false, false, 0, false, 9, false) ||
			    TelepadEntitySpawnPacket.ShouldMaterialize(false, true, 10, false, 9, false) ||
			    TelepadEntitySpawnPacket.ShouldMaterialize(false, true, 9, true, 9, false) ||
			    TelepadEntitySpawnPacket.ShouldMaterialize(false, true, 9, false, 9, true) ||
			    !TelepadEntitySpawnPacket.ShouldMaterialize(false, true, 9, false, 9, false) ||
			    !TelepadEntitySpawnPacket.ShouldMaterialize(false, true, 9, false, 10, true))
				return UnitTestResult.Fail("Telepad stale, duplicate or replacement gate is incorrect");
			return UnitTestResult.Pass("Telepad custom spawn is lifecycle-bound and idempotent");
		}

		[UnitTest(name: "Cryo minion lifecycle rejects stale and tombstoned state", category: "Sync")]
		public static UnitTestResult CryoLifecycleGate()
		{
			if (CryoTankSync.CanApplyLifecycle(12, false, 11) ||
			    CryoTankSync.CanApplyLifecycle(12, true, 12) ||
			    !CryoTankSync.CanApplyLifecycle(12, false, 12) ||
			    !CryoTankSync.CanApplyLifecycle(12, true, 13))
				return UnitTestResult.Fail("Cryo minion lifecycle gate is incorrect");
			return UnitTestResult.Pass("Cryo custom minion state accepts only live current or newer lifecycle");
		}

		[UnitTest(name: "Authority cleanup advances reusable lifecycle", category: "Sync")]
		public static UnitTestResult AuthorityCleanupAdvancesLifecycle()
		{
			NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry[] original =
				NetworkIdentityRegistry.GetLifecycleRevisionSnapshot().ToArray();
			try
			{
				const int netId = -1_987_654_321;
				ulong first = NetworkIdentityRegistry.BeginLifecycle(netId);
				ulong tombstone = NetworkIdentityRegistry.EndLifecycle(netId);
				ulong replacement = NetworkIdentityRegistry.BeginLifecycle(netId);
				if (first == 0 || tombstone <= first || replacement <= tombstone ||
				    NetworkIdentityRegistry.IsLifecycleTombstoned(netId))
					return UnitTestResult.Fail("Reused authority NetId retained its old lifecycle");
				if (!NetworkIdentity.ShouldEndLifecycleLocally(false, false, true) ||
				    NetworkIdentity.ShouldEndLifecycleLocally(true, true, true) ||
				    NetworkIdentity.ShouldEndLifecycleLocally(true, false, true) ||
				    NetworkIdentity.ShouldEndLifecycleLocally(false, false, false))
					return UnitTestResult.Fail("Authority cleanup role gate is incorrect");
				return UnitTestResult.Pass("Standalone cleanup tombstones reusable identity exactly once");
			}
			finally
			{
				NetworkIdentityRegistry.TryReplaceLifecycleRevisionBaseline(original);
			}
		}

		private static TelepadEntitySpawnPacket Roundtrip(TelepadEntitySpawnPacket input)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			var output = new TelepadEntitySpawnPacket();
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			return output;
		}

		private static ImmigrantOptionEntry Duplicant()
			=> new()
			{
				EntryType = 0,
				Name = "Ada",
				PersonalityId = "ADA",
				TraitIds = new List<string>(),
				StressTraitId = "StressVomiter",
				JoyTraitId = "BalloonArtist",
				StickerType = string.Empty,
				SkillAptitudes = new Dictionary<string, float>(),
				StartingLevels = new Dictionary<string, int>()
			};
	}
}
