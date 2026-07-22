using System.IO;
using System.Text;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SyncTests
	{
		[UnitTest(name: "Duplicant positions in sync with host", category: "Sync")]
		public static UnitTestResult DuplicantPositionsInSync()
		{
			if (!MultiplayerSession.InActiveSession)
				return UnitTestResult.Fail("Not in a multiplayer session");

			const float MaxCellDelta = 2f;

			int minionsChecked = 0;
			foreach (var identity in NetworkIdentityRegistry.AllIdentities)
			{
				if (identity == null || identity.gameObject == null)
					continue;

				var prefabId = identity.gameObject.GetComponent<KPrefabID>();
				if (prefabId == null || !prefabId.HasTag(GameTags.BaseMinion))
					continue;

				if (!identity.gameObject.TryGetComponent<EntityPositionHandler>(out var handler))
					return UnitTestResult.Fail($"Minion '{identity.gameObject.name}' has no EntityPositionHandler");

				minionsChecked++;

				if (MultiplayerSession.IsHost)
					continue;

				if (handler.serverTimestamp == 0)
					return UnitTestResult.Fail($"Minion '{identity.gameObject.name}' has not received a position packet yet");

				float delta = Vector3.Distance(identity.gameObject.transform.position, handler.serverPosition);
				if (delta > MaxCellDelta)
					return UnitTestResult.Fail($"Minion '{identity.gameObject.name}' is {delta:F2} cells off server position");
			}

			if (minionsChecked == 0)
				return UnitTestResult.Fail("No minions found in registry");

			string mode = MultiplayerSession.IsHost ? "host" : "client";
			return UnitTestResult.Pass($"Checked {minionsChecked} minions ({mode})");
		}

		[UnitTest(name: "Build progress bar pipeline intact", category: "Sync")]
		public static UnitTestResult BuildProgressBarVisible()
		{
			var packet = new WorkableProgressPacket();

			using var ms = new MemoryStream();
			using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
				packet.Serialize(writer);
			ms.Position = 0;

			var copy = new WorkableProgressPacket();
			using (var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
				copy.Deserialize(reader);

			const int testNetId = -987654;
			const float testPercent = 0.42f;
			RemoteProgressRegistry.SetProgress(testNetId, RemoteProgressKind.WorkablePercent, testPercent, true, 2f, 5f);

			if (!RemoteProgressRegistry.TryGetPercent(testNetId, RemoteProgressKind.WorkablePercent, out float percent))
			{
				RemoteProgressRegistry.Clear(testNetId, RemoteProgressKind.WorkablePercent, hideTarget: false);
				return UnitTestResult.Fail("RemoteProgressRegistry did not return the stored entry");
			}

			RemoteProgressRegistry.Clear(testNetId, RemoteProgressKind.WorkablePercent, hideTarget: false);

			if (Mathf.Abs(percent - testPercent) > 0.001f)
				return UnitTestResult.Fail($"Stored percent {percent} differs from written {testPercent}");

			return UnitTestResult.Pass("WorkableProgressPacket serialize/deserialize pipeline intact and RemoteProgressRegistry stores progress");
		}

		[UnitTest(name: "Hard sync not stuck in progress", category: "Sync")]
		public static UnitTestResult HardSyncCompletes()
		{
			if (!PacketRegistry.HasRegisteredPacket(typeof(HardSyncPacket)))
				return UnitTestResult.Fail("HardSyncPacket is not registered");
			if (!PacketRegistry.HasRegisteredPacket(typeof(HardSyncCompletePacket)))
				return UnitTestResult.Fail("HardSyncCompletePacket is not registered");

			if (GameServerHardSync.IsHardSyncInProgress)
				return UnitTestResult.Fail("Hard sync is currently in progress, rerun the test once it completes");

			if (GameServerHardSync.hardSyncDoneThisCycle && !MultiplayerSession.InActiveSession)
				return UnitTestResult.Fail("hardSyncDoneThisCycle is set but session is not active");

			string state = GameServerHardSync.hardSyncDoneThisCycle ? "completed this cycle" : "idle";
			return UnitTestResult.Pass($"Hard sync machinery is {state}");
		}
	}
}
