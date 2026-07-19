using System.IO;
using System.Reflection;
using System.Text;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Animation;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SyncTests
	{
		[UnitTest(name: "Position staleness uses the local receive clock", category: "Sync")]
		public static UnitTestResult PositionStalenessUsesLocalClock()
		{
			if (!EntityPositionHandler.IsServerStateStale(0, 10f, 10f))
				return UnitTestResult.Fail("Missing server state must be stale");
			if (EntityPositionHandler.IsServerStateStale(1, 9f, 10f))
				return UnitTestResult.Fail("A position received one second ago must be fresh");
			if (!EntityPositionHandler.IsServerStateStale(1, 7f, 10f))
				return UnitTestResult.Fail("A position received three seconds ago must be stale");
			if (!EntityPositionHandler.ShouldRequestServerState(0, false, 10f, 10f))
				return UnitTestResult.Fail("Initial position must be requested outside the viewport");
			if (EntityPositionHandler.ShouldRequestServerState(1, false, 7f, 10f))
				return UnitTestResult.Fail("Received off-screen positions must remain viewport-culled");
			if (!EntityPositionHandler.ShouldRequestServerState(1, true, 7f, 10f))
				return UnitTestResult.Fail("Stale visible positions must be repaired");

			return UnitTestResult.Pass("Initial position repair bypasses viewport culling once");
		}

		[UnitTest(name: "Duplicant positions in sync with host", category: "Sync", liveSafe: true)]
		public static UnitTestResult DuplicantPositionsInSync()
		{
			if (!MultiplayerSession.InSession)
				return UnitTestResult.Skip("Requires an active multiplayer session");

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
			var packet = ProgressPacket(show: false, percent: 0f, remaining: 0f, total: 0f);

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

		[UnitTest(name: "Workable progress normalizes non-finite host state", category: "Sync")]
		public static UnitTestResult WorkableProgressNormalizesNonFiniteHostState()
		{
			WorkableProgressPacket hidden = ProgressPacket(
				show: false, percent: float.NaN,
				remaining: float.NaN, total: float.PositiveInfinity);
			WorkableProgressPacket hiddenCopy = Roundtrip(hidden, new WorkableProgressPacket());
			if (!HasProgressState(hiddenCopy, show: false, percent: 0f, remaining: 0f, total: 0f))
				return UnitTestResult.Fail("Hidden progress preserved non-finite host values");

			WorkableProgressPacket invalidVisible = ProgressPacket(
				show: true, percent: float.NaN,
				remaining: float.PositiveInfinity, total: 0f);
			WorkableProgressPacket invalidCopy = Roundtrip(
				invalidVisible, new WorkableProgressPacket());
			if (!HasProgressState(invalidCopy, show: false, percent: 0f, remaining: 0f, total: 0f))
				return UnitTestResult.Fail("Invalid visible progress was not converted to hidden state");

			WorkableProgressPacket bounded = ProgressPacket(
				show: true, percent: 2f, remaining: -1f, total: 10f);
			WorkableProgressPacket boundedCopy = Roundtrip(bounded, new WorkableProgressPacket());
			return HasProgressState(boundedCopy, show: true, percent: 1f, remaining: 0f, total: 10f)
				? UnitTestResult.Pass("Progress packets emit finite canonical wire state")
				: UnitTestResult.Fail("Finite progress values were not bounded canonically");
		}

		[UnitTest(name: "Workable progress rejects invalid wire targets", category: "Sync")]
		public static UnitTestResult WorkableProgressRejectsInvalidWireTargets()
		{
			if (!RejectsProgressWire(0, typeof(Workable).AssemblyQualifiedName))
				return UnitTestResult.Fail("Zero target NetId was accepted");
			if (!RejectsProgressWire(1, string.Empty))
				return UnitTestResult.Fail("Empty target type was accepted");
			if (!RejectsProgressWire(1, new string('A', 4097)))
				return UnitTestResult.Fail("Oversized target type was accepted");
			if (RejectsProgressWire(1, new string('A', 4096)))
				return UnitTestResult.Fail("Maximum-length target type was rejected");

			WorkableProgressPacket invalidOutbound = ProgressPacket(
				show: false, percent: 0f, remaining: 0f, total: 0f);
			SetProgressField(invalidOutbound, "TargetNetId", 0);
			if (!RejectsSerialize(invalidOutbound))
				return UnitTestResult.Fail("Zero target NetId was serialized outbound");

			return UnitTestResult.Pass("Invalid workable progress targets are rejected inbound and outbound");
		}

		[UnitTest(name: "Worker state rejects invalid outbound identities", category: "Sync")]
		public static UnitTestResult WorkerStateRejectsInvalidOutboundIdentities()
		{
			var invalidWorker = WorkerPacket(0, starting: false, 0, null);
			if (!RejectsSerialize(invalidWorker))
				return UnitTestResult.Fail("Zero worker NetId was serialized outbound");

			var invalidTarget = WorkerPacket(
				1, starting: true, 0, typeof(Workable).AssemblyQualifiedName);
			if (!RejectsSerialize(invalidTarget))
				return UnitTestResult.Fail("Zero workable NetId was serialized outbound");

			var validStop = WorkerPacket(1, starting: false, 0, null);
			Roundtrip(validStop, new StandardWorker_WorkingState_Packet());
			var validStart = WorkerPacket(
				1, starting: true, 2, typeof(Workable).AssemblyQualifiedName);
			Roundtrip(validStart, new StandardWorker_WorkingState_Packet());

			return UnitTestResult.Pass("Worker state requires tracked worker and workable identities");
		}

		[UnitTest(name: "Authoritative state revisions roundtrip and reject stale packets", category: "Sync")]
		public static UnitTestResult AuthoritativeStateRevisionsRejectStalePackets()
		{
			if (!SnapshotWireBoundsTests.TryGetValidCell(out int cell))
				return UnitTestResult.Skip("Structure revision roundtrip requires an initialized world grid");

			var cycle = Roundtrip(new WorldCyclePacket { Cycle = 3, CycleTime = 4f, Revision = 5 }, new WorldCyclePacket());
			if (cycle.Revision != 5 || !WorldCyclePacket.ShouldApplyRevision(4, 5)
			    || WorldCyclePacket.ShouldApplyRevision(5, 5))
				return UnitTestResult.Fail("World cycle revision ordering failed");

			var structure = Roundtrip(new StructureStatePacket
			{
				NetId = 7,
				Cell = cell,
				Revision = 9,
				SyncerTypeName = "StorageStateSyncer",
				Value = 1f
			}, new StructureStatePacket());
			if (structure.Revision != 9 || structure.SyncerTypeName != "StorageStateSyncer"
			    || !StructureStatePacket.ShouldApplyRevision(8, 9)
			    || StructureStatePacket.ShouldApplyRevision(9, 8))
				return UnitTestResult.Fail("Structure state revision ordering failed");

			var workableSource = ProgressPacket(show: false, percent: 0f, remaining: 0f, total: 0f);
			workableSource.Revision = 12;
			var workable = Roundtrip(workableSource, new WorkableProgressPacket());
			if (workable.Revision != 12 || !WorkableProgressPacket.ShouldApplyRevision(11, 12)
			    || WorkableProgressPacket.ShouldApplyRevision(12, 12))
				return UnitTestResult.Fail("Workable progress revision ordering failed");

			return UnitTestResult.Pass("World, structure, and workable state reject stale revisions");
		}

		[UnitTest(name: "Hard sync not stuck in progress", category: "Sync", liveSafe: true)]
		public static UnitTestResult HardSyncCompletes()
		{
			if (!PacketRegistry.HasRegisteredPacket(typeof(HardSyncPacket)))
				return UnitTestResult.Fail("HardSyncPacket is not registered");
			if (!PacketRegistry.HasRegisteredPacket(typeof(HardSyncCompletePacket)))
				return UnitTestResult.Fail("HardSyncCompletePacket is not registered");

			if (GameServerHardSync.IsHardSyncInProgress)
				return UnitTestResult.Fail("Hard sync is currently in progress, rerun the test once it completes");

			if (GameServerHardSync.hardSyncDoneThisCycle && !MultiplayerSession.InSession)
				return UnitTestResult.Fail("hardSyncDoneThisCycle is set but session is not active");

			string state = GameServerHardSync.hardSyncDoneThisCycle ? "completed this cycle" : "idle";
			return UnitTestResult.Pass($"Hard sync machinery is {state}");
		}

		private static T Roundtrip<T>(T source, T target) where T : IPacket
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
				source.Serialize(writer);
			stream.Position = 0;
			using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
				target.Deserialize(reader);
			return target;
		}

		private static WorkableProgressPacket ProgressPacket(
			bool show, float percent, float remaining, float total)
		{
			var packet = new WorkableProgressPacket { Revision = 1 };
			SetProgressField(packet, "TargetNetId", 1);
			SetProgressField(packet, "TargetTypeName", typeof(Workable).AssemblyQualifiedName);
			SetProgressField(packet, "ProgressKind", RemoteProgressKind.WorkablePercent);
			SetProgressField(packet, "ShowProgressBar", show);
			SetProgressField(packet, "PercentComplete", percent);
			SetProgressField(packet, "WorkTimeRemaining", remaining);
			SetProgressField(packet, "WorkTimeTotal", total);
			return packet;
		}

		private static bool RejectsProgressWire(int targetNetId, string targetTypeName)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
			{
				writer.Write(targetNetId);
				writer.Write(targetTypeName);
				writer.Write((int)RemoteProgressKind.WorkablePercent);
				writer.Write(0f);
				writer.Write(false);
				writer.Write(0f);
				writer.Write(0f);
				writer.Write(1UL);
			}
			stream.Position = 0;
			try
			{
				using var reader = new BinaryReader(stream, Encoding.UTF8, true);
				new WorkableProgressPacket().Deserialize(reader);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}

		private static bool RejectsSerialize(IPacket packet)
		{
			try
			{
				using var stream = new MemoryStream();
				using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
				packet.Serialize(writer);
				return false;
			}
			catch (InvalidDataException)
			{
				return true;
			}
		}

		private static StandardWorker_WorkingState_Packet WorkerPacket(
			int workerNetId, bool starting, int workableNetId, string workableType)
		{
			var packet = new StandardWorker_WorkingState_Packet();
			SetWorkerField(packet, "WorkerNetId", workerNetId);
			SetWorkerField(packet, "StartingToWork", starting);
			SetWorkerField(packet, "WorkableNetId", workableNetId);
			SetWorkerField(packet, "WorkableType", workableType);
			return packet;
		}

		private static bool HasProgressState(
			WorkableProgressPacket packet,
			bool show, float percent, float remaining, float total)
			=> GetProgressField<bool>(packet, "ShowProgressBar") == show
			   && GetProgressField<float>(packet, "PercentComplete") == percent
			   && GetProgressField<float>(packet, "WorkTimeRemaining") == remaining
			   && GetProgressField<float>(packet, "WorkTimeTotal") == total;

		private static void SetProgressField<T>(WorkableProgressPacket packet, string name, T value)
			=> typeof(WorkableProgressPacket).GetField(
				name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(packet, value);

		private static T GetProgressField<T>(WorkableProgressPacket packet, string name)
			=> (T)typeof(WorkableProgressPacket).GetField(
				name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(packet);

		private static void SetWorkerField<T>(
			StandardWorker_WorkingState_Packet packet, string name, T value)
			=> typeof(StandardWorker_WorkingState_Packet).GetField(
				name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(packet, value);
	}
}
