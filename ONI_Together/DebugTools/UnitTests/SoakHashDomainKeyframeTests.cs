#if DEBUG
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SoakHashDomainKeyframeTests
	{
		[UnitTest(name: "Soak keyframe stream is windowed and progress-driven", category: "Networking")]
		public static UnitTestResult KeyframeStreamIsWindowedAndProgressDriven()
		{
			SoakHashDomainKeyframeTracker.Begin(9, 2, 130);
			for (int index = 0; index < 128; index++)
				SoakHashDomainKeyframeTracker.Record(new SoakHashDomainKeyframeRecord
				{
					RunId = 9, SampleId = 2, EntryIndex = index, Applied = true,
				});
			if (!SoakHashDomainKeyframeTracker.TryGetProgress(out var first)
			    || first.ReceivedEntries != 128 || first.ApplyFinished)
				return UnitTestResult.Fail("The first bounded keyframe window did not report progress");
			SoakHashDomainKeyframeTracker.CommitProgress(first);
			SoakHashDomainKeyframeTracker.Record(new SoakHashDomainKeyframeRecord
			{
				RunId = 9, SampleId = 2, EntryIndex = 128, Applied = true,
			});
			SoakHashDomainKeyframeTracker.Record(new SoakHashDomainKeyframeRecord
			{
				RunId = 9, SampleId = 2, EntryIndex = 129, Applied = true,
			});
			bool complete = SoakHashDomainKeyframeTracker.TryGetProgress(out var final)
			                && final.ReceivedEntries == 130 && final.ApplyFinished
			                && final.ApplySucceeded;
			SoakHashDomainKeyframeTracker.Reset();
			return complete
				? UnitTestResult.Pass("Keyframes advance only after a bounded contiguous window")
				: UnitTestResult.Fail("The final keyframe progress did not include apply outcome");
		}

		[UnitTest(name: "Soak keyframe window is bounded by wire credit", category: "Networking")]
		public static UnitTestResult LargeStorageWindowIsWireCreditBounded()
		{
			byte[] storagePayload = new byte[4 * 1024 * 1024];
			byte[] body = LargeStorageKeyframe(0, storagePayload).SerializeBody();
			SoakHashDomainKeyframePagePacket page =
				SoakHashDomainKeyframePagePacket.Create(12, 1, 0, 0, body);
			int fragments = (page.GetOrderedReliableWireBytes()
			                 + SoakHashDomainKeyframePagePacket.RiptideFragmentPayloadBytes - 1)
			                / SoakHashDomainKeyframePagePacket.RiptideFragmentPayloadBytes;
			SoakHashDomainKeyframeTracker.Begin(new SoakHashDomainKeyframeContext
			{
				RunId = 12,
				SampleId = 1,
				ExpectedEntries = 1,
				PagedTransport = true,
				LifecycleBaseline = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>(),
			});
			SoakKeyframePageReceiver.Begin(12, 1, 1);
			bool firstAccepted = SoakKeyframePageReceiver.Accept(page)
			                     == SoakKeyframePageReceiveResult.Advanced;
			bool noLegacyProgress = !SoakHashDomainKeyframeTracker.TryGetProgress(out _);
			SoakHashDomainKeyframeTracker.Reset();

			if (SoakHashDomainKeyframePagePacket.PageCountFor(body.Length) < 300
			    || fragments > SoakHashDomainKeyframePagePacket.MaxRiptideFragmentsPerPage
			    || fragments >= 16 || !firstAccepted || !noLegacyProgress)
			{
				return UnitTestResult.Fail(
					$"Large page was not credit-bounded: body={body.Length}, " +
					$"wire={page.GetOrderedReliableWireBytes()}, fragments={fragments}, " +
					$"accepted={firstAccepted}, legacy={noLegacyProgress}");
			}
			return UnitTestResult.Pass(
				"Large storage keyframes advance through sub-ACK-history pages");
		}

		[UnitTest(name: "Soak keyframe progress rejects invalid advances", category: "Networking")]
		public static UnitTestResult KeyframeProgressRejectsInvalidAdvances()
		{
			var window = new SoakKeyframeProgressWindow
			{
				RunId = 4, SampleId = 2, ExpectedEntries = 300,
				SentEntries = 128, AcknowledgedEntries = -1,
			};
			var first = new SoakKeyframeProgressAckPacket
			{
				RunId = 4, SampleId = 2, ReceivedEntries = 128,
			};
			if (SoakKeyframeProgressAckPacket.Evaluate(window, first)
			    != SoakKeyframeProgressResult.Advanced)
				return UnitTestResult.Fail("A valid full window was rejected");
			window.AcknowledgedEntries = 128;
			var stale = new SoakKeyframeProgressAckPacket
			{
				RunId = 4, SampleId = 2, ReceivedEntries = 128,
			};
			var beyondSent = new SoakKeyframeProgressAckPacket
			{
				RunId = 4, SampleId = 2, ReceivedEntries = 129,
			};
			return SoakKeyframeProgressAckPacket.Evaluate(window, stale)
			       == SoakKeyframeProgressResult.Ignore
			       && SoakKeyframeProgressAckPacket.Evaluate(window, beyondSent)
			       == SoakKeyframeProgressResult.Invalid
			       && SoakStateHashProbe.KeyframeProgressTimedOut(29f, 299f) == false
			       && SoakStateHashProbe.KeyframeProgressTimedOut(30f, 1f)
				? UnitTestResult.Pass("Progress is monotonic, bounded, and finitely timed")
				: UnitTestResult.Fail("Stale, out-of-window, or unbounded progress was accepted");
		}

		[UnitTest(name: "Soak keyframe apply exception reports terminal failure", category: "Networking")]
		public static UnitTestResult KeyframeApplyExceptionReportsFailure()
		{
			SoakHashDomainKeyframeTracker.Begin(7, 3, 0);
			SoakHashDomainKeyframeTracker.ApplyForTests(
				() => throw new InvalidDataException("synthetic keyframe apply failure"));
			bool reported = SoakHashDomainKeyframeTracker.TryGetProgress(out var progress)
			                && progress.ReceivedEntries == 0 && progress.ApplyFinished
			                && !progress.ApplySucceeded;
			SoakHashDomainKeyframeTracker.Reset();
			return reported
				? UnitTestResult.Pass("Apply exceptions become an explicit failed progress ACK")
				: UnitTestResult.Fail("Apply exception escaped without a terminal failed progress ACK");
		}

		[UnitTest(name: "Soak keyframe tracker requires every exact entry", category: "Networking")]
		public static UnitTestResult TrackerRequiresEveryExactEntry()
		{
			SoakHashDomainKeyframeTracker.Begin(5, 3, 2);
			if (!SoakHashDomainKeyframeTracker.Record(new SoakHashDomainKeyframeRecord
			    {
				    RunId = 5, SampleId = 3, EntryIndex = 1, Applied = true,
			    })
			    || SoakHashDomainKeyframeTracker.IsComplete(5, 3)
			    || SoakHashDomainKeyframeTracker.Record(new SoakHashDomainKeyframeRecord
			    {
				    RunId = 5, SampleId = 3, EntryIndex = 1, Applied = true,
			    })
			    || !SoakHashDomainKeyframeTracker.Record(new SoakHashDomainKeyframeRecord
			    {
				    RunId = 5, SampleId = 3, EntryIndex = 0, Applied = true,
			    })
			    || !SoakHashDomainKeyframeTracker.IsComplete(5, 3))
				return UnitTestResult.Fail("Keyframe fence accepted a missing, stale, or duplicate entry");
			SoakHashDomainKeyframeTracker.Begin(5, 4, 1);
			SoakHashDomainKeyframeTracker.Record(new SoakHashDomainKeyframeRecord
			{
				RunId = 5, SampleId = 4, EntryIndex = 0, Applied = false,
			});
			bool rejectedFailure = !SoakHashDomainKeyframeTracker.IsComplete(5, 4);
			SoakHashDomainKeyframeTracker.Reset();
			return rejectedFailure
				? UnitTestResult.Pass("Fence requires every keyframe to apply successfully")
				: UnitTestResult.Fail("A failed keyframe released the fence");
		}

		[UnitTest(name: "Soak position keyframe is host-only and ordered", category: "Networking")]
		public static UnitTestResult PositionKeyframeIsHostOnlyAndOrdered()
		{
			var packet = RoundTrip(new SoakHashDomainKeyframePacket
			{
				RunId = 8,
				SampleId = 2,
				EntryIndex = 4,
				NetId = 19,
				LifecycleSnapshot = Lifecycle(19, new Vector3(2f, 3f, 4f)),
				HasPosition = true,
				Position = new Vector3(2f, 3f, 4f),
				FlipX = true,
				NavType = NavType.Floor,
				PositionSequence = 27,
			});
			if (packet.RunId != 8 || packet.SampleId != 2 || packet.EntryIndex != 4
			    || packet.NetId != 19 || !packet.HasPosition || packet.Position.x != 2f
			    || !packet.FlipX || packet.PositionSequence != 27
			    || packet is not IHostOnlyPacket
			    || !OrderedReliableChannel.ShouldWrap(packet, PacketSendMode.ReliableImmediate))
				return UnitTestResult.Fail("Position keyframe lost authority or ordered delivery metadata");
				return UnitTestResult.Pass("Position keyframes precede the reliable checkpoint fence");
			}

			[UnitTest(name: "Soak keyframe carries rocket control-station state", category: "Networking")]
		public static UnitTestResult RocketControlStationStateRoundTrips()
			{
				var packet = RoundTrip(new SoakHashDomainKeyframePacket
				{
					RunId = 8,
					SampleId = 2,
					EntryIndex = 5,
					NetId = 20,
					LifecycleSnapshot = Lifecycle(20, Vector3.zero),
					HasRocketSettings = true,
					RocketSettings = new RocketSettingsPacketData
					{
						TargetKind = RocketSettingsTarget.ControlStation,
						TargetNetId = 20,
						RestrictWhenGrounded = true,
					},
				});
				if (!packet.HasRocketSettings || packet.RocketSettings == null
				    || packet.RocketSettings.TargetKind != RocketSettingsTarget.ControlStation
				    || packet.RocketSettings.TargetNetId != 20
				    || !packet.RocketSettings.RestrictWhenGrounded)
					return UnitTestResult.Fail("Rocket control-station state was lost from the keyframe");
				return UnitTestResult.Pass("Rocket control-station state is carried by the ordered keyframe");
			}

		[UnitTest(name: "Soak keyframe applies cluster state after world transform", category: "Networking")]
		public static UnitTestResult ClusterStateFollowsWorldTransform()
		{
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static |
			                           BindingFlags.Instance;
			MethodInfo pipeline = typeof(SoakHashDomainKeyframePacket).GetMethod(
				"TryApplyPreparedKeyframes", flags);
			MethodInfo transform = typeof(SoakHashDomainKeyframePacket).GetMethod(
				"TryApplyWorldTransform", flags);
			MethodInfo clusterRocket = typeof(SoakHashDomainKeyframePacket).GetMethod(
				"TryApplyNonPosition", flags);
			int transformIndex = IndexOfCall(pipeline, transform);
			int clusterIndex = IndexOfCall(pipeline, clusterRocket);
			return transformIndex >= 0 && clusterIndex > transformIndex
				? UnitTestResult.Pass("Transform side effects precede final cluster and rocket state")
				: UnitTestResult.Fail("World transform can overwrite an already-applied cluster keyframe");
		}

		[UnitTest(
			name: "Authority snapshots discard pending storage deltas",
			category: "Networking")]
		public static UnitTestResult AuthoritySnapshotsDiscardPendingStorageDeltas()
		{
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
			MethodInfo clear = typeof(NetworkIdentityRegistry).GetMethod(
				"ClearPendingSnapshotDeltas", flags);
			MethodInfo baseline = typeof(NetworkIdentityRegistry).GetMethod(
				"TryReconcileLifecycleBaseline", flags);
			MethodInfo keyframe = typeof(SoakHashDomainKeyframePacket).GetMethod(
				"TryReconcileLifecycle", flags);
			MethodInfo baselineApply = typeof(NetworkIdentityRegistry).GetMethod(
				"TryApplyExpectedLifecycle", flags);
			MethodInfo keyframeApply = typeof(SpawnPrefabPacket).GetMethod(
				nameof(SpawnPrefabPacket.TryApplySnapshot),
				BindingFlags.NonPublic | BindingFlags.Instance);
			int baselineClear = IndexOfCall(baseline, clear);
			int keyframeClear = IndexOfCall(keyframe, clear);
			return baselineClear > IndexOfCall(baseline, baselineApply)
			       && keyframeClear > IndexOfCall(keyframe, keyframeApply)
				? UnitTestResult.Pass(
					"Pending deltas clear only after every authority lifecycle applies")
				: UnitTestResult.Fail(
					"A failed authority snapshot can discard pending deltas");
		}

		[UnitTest(
			name: "Lifecycle snapshot does not consume pre-cut pending deltas",
			category: "Networking")]
		public static UnitTestResult SnapshotMaterializationDoesNotConsumePending()
		{
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
			MethodInfo complete = typeof(SpawnPrefabPacket).GetMethod(
				"CompleteMaterialization", flags);
			MethodInfo finish = typeof(SpawnPrefabPacket).GetMethod(
				"FinishRuntimeMaterialization", flags);
			MethodInfo consume = typeof(SpawnPrefabPacket).GetMethod(
				"ConsumePendingPickup", flags);
			return IndexOfCall(complete, consume) < 0 && IndexOfCall(finish, consume) >= 0
				? UnitTestResult.Pass("Only runtime spawn completion replays pending item events")
				: UnitTestResult.Fail("Lifecycle baseline can consume a pre-cut pending item event");
		}

		[UnitTest(
			name: "Authority snapshots retire zero-mass element lifecycles",
			category: "Networking")]
		public static UnitTestResult AuthoritySnapshotsRetireZeroMassElements()
		{
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
			MethodInfo retire = typeof(NetworkIdentityRegistry).GetMethod(
				"RetireUnstableElementLifecyclesForSnapshot", flags);
			MethodInfo keyframe = typeof(SoakHashDomainKeyframePacket).GetMethod(
				"PrepareHashDomainIdentityMembership", flags);
			MethodInfo world = typeof(WorldDataRequestPacket).GetMethod(
				"SendWorldData", BindingFlags.Public | BindingFlags.Static);
			bool stablePredicate =
				SpawnPrefabPacket.ShouldSynchronizeElementLifecycle(0.001f)
				&& !SpawnPrefabPacket.ShouldSynchronizeElementLifecycle(0f);
			return stablePredicate && IndexOfCall(keyframe, retire) >= 0
			       && IndexOfCall(world, retire) >= 0
				? UnitTestResult.Pass(
					"World and keyframe baselines tombstone non-material resources")
				: UnitTestResult.Fail(
					"A zero-mass resource can enter an authoritative live baseline");
		}

		[UnitTest(name: "Soak world hash uses authoritative client positions", category: "Networking")]
		public static UnitTestResult WorldHashUsesAuthoritativeClientPosition()
		{
			var rendered = new Vector3(1f, 2f, 0f);
			var authoritative = new Vector3(8f, 9f, 0f);
			if (EntityPositionHandler.SelectHashPosition(
				    true, rendered, 3, authoritative) != authoritative
			    || EntityPositionHandler.SelectHashPosition(
					    false, rendered, 3, authoritative) != rendered
			    || EntityPositionHandler.SelectHashPosition(
					    true, rendered, 0, authoritative) != rendered)
				return UnitTestResult.Fail("World hash still depends on client render interpolation");
			return UnitTestResult.Pass("World hash compares authoritative movement state");
		}

		[UnitTest(name: "Soak keyframe covers identities without position handlers", category: "Networking")]
		public static UnitTestResult GenericWorldTransformRoundTrips()
		{
			SoakHashDomainKeyframePacket packet = RoundTrip(
				new SoakHashDomainKeyframePacket
				{
					RunId = 8,
					SampleId = 2,
					EntryIndex = 6,
					NetId = 21,
					WorldId = 3,
					Position = new Vector3(4.25f, 7.5f, -1f),
					LifecycleSnapshot = Lifecycle(
						21, new Vector3(4.25f, 7.5f, -1f), 3),
				});
			return !packet.HasPosition && packet.WorldId == 3
			       && packet.Position == new Vector3(4.25f, 7.5f, -1f)
				? UnitTestResult.Pass("Generic identities carry exact world transforms")
				: UnitTestResult.Fail("Generic identity transform was omitted from the keyframe");
		}

		private static SpawnPrefabPacket Lifecycle(
			int netId, Vector3 position, int worldId = 0)
		{
			return new SpawnPrefabPacket
			{
				NetId = netId,
				Revision = 1,
				Hash = 1,
				Position = position,
				WorldId = worldId,
				IsActive = true,
			};
		}

		private static SoakHashDomainKeyframePacket LargeStorageKeyframe(
			int entryIndex, params byte[][] storagePayloads)
		{
			int netId = 1000 + entryIndex;
			return new SoakHashDomainKeyframePacket
			{
				RunId = 12,
				SampleId = 1,
				EntryIndex = entryIndex,
				NetId = netId,
				LifecycleSnapshot = Lifecycle(netId, Vector3.zero),
				WorldId = 0,
				Position = Vector3.zero,
				StorageRevision = 1,
				StorageSnapshots = new List<byte[]>(storagePayloads),
			};
		}

		private static T RoundTrip<T>(T source) where T : IPacket, new()
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(
				       stream, System.Text.Encoding.UTF8, leaveOpen: true))
				source.Serialize(writer);
			stream.Position = 0;
			var copy = new T();
			using var reader = new BinaryReader(stream);
			copy.Deserialize(reader);
			return copy;
		}

		private static int IndexOfCall(MethodInfo caller, MethodInfo callee)
		{
			byte[] il = caller?.GetMethodBody()?.GetILAsByteArray();
			if (il == null || callee == null)
				return -1;
			byte[] token = BitConverter.GetBytes(callee.MetadataToken);
			for (int index = 0; index <= il.Length - token.Length; index++)
				if (il.Skip(index).Take(token.Length).SequenceEqual(token))
					return index;
			return -1;
		}
	}
}
#endif
