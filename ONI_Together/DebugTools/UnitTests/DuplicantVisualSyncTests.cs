using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.DuplicantActions;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class DuplicantVisualSyncTests
	{
		[UnitTest(name: "Duplicant visual batch: compact round-trip", category: "Duplicant")]
		public static UnitTestResult SnapshotBatchRoundTrip()
		{
			var input = new DuplicantVisualSnapshotBatchPacket
			{
				ServerTimestamp = 123456789,
				Snapshots = new List<DuplicantVisualSnapshot>
				{
					new DuplicantVisualSnapshot
					{
						NetId = 42,
						Sequence = 7,
						Position = new Vector3(132.503f, 188.497f, 1.9f),
						NavType = NavType.Ladder,
						Flags = DuplicantVisualSnapshotFlags.FlipX | DuplicantVisualSnapshotFlags.Moving,
					},
					new DuplicantVisualSnapshot
					{
						NetId = 99,
						Sequence = uint.MaxValue,
						Position = new Vector3(2.25f, 3.75f, -1.25f),
						NavType = NavType.Floor,
						Flags = DuplicantVisualSnapshotFlags.Teleport,
					},
				},
			};

			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);

			const int expectedPayloadBytes = 8 + 1 + 20 * 2;
			if (stream.Length != expectedPayloadBytes)
				return UnitTestResult.Fail($"Expected {expectedPayloadBytes} bytes, got {stream.Length}");

			stream.Position = 0;
			var output = new DuplicantVisualSnapshotBatchPacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				output.Deserialize(reader);

			if (output.ServerTimestamp != input.ServerTimestamp || output.Snapshots.Count != 2)
				return UnitTestResult.Fail("Batch header did not round-trip");
			for (int i = 0; i < input.Snapshots.Count; i++)
			{
				var expected = input.Snapshots[i];
				var actual = output.Snapshots[i];
				if (actual.NetId != expected.NetId || actual.Sequence != expected.Sequence
					|| actual.NavType != expected.NavType || actual.Flags != expected.Flags)
				{
					return UnitTestResult.Fail($"Snapshot metadata {i} did not round-trip");
				}
				if (Vector3.Distance(actual.Position, expected.Position) > 0.007f)
					return UnitTestResult.Fail($"Snapshot {i} quantization error was too large");
			}

			return UnitTestResult.Pass($"Two snapshots round-tripped in {stream.Length} bytes");
		}

		[UnitTest(name: "Duplicant navigation event: round-trip", category: "Duplicant")]
		public static UnitTestResult NavigationEventRoundTrip()
		{
			var input = new NavigatorTransitionPacket
			{
				NetId = 17,
				Sequence = 88,
				ServerTimestamp = 987654321,
				SourcePosition = new Vector3(10.5f, 11.5f, 2f),
				TransitionId = 13,
				Speed = 2.75f,
			};

			var output = RoundTrip(input);
			if (output.NetId != input.NetId || output.Sequence != input.Sequence
				|| output.ServerTimestamp != input.ServerTimestamp
				|| output.SourcePosition != input.SourcePosition
				|| output.TransitionId != input.TransitionId || output.Speed != input.Speed
				|| output.IsStop)
			{
				return UnitTestResult.Fail("Navigation transition fields did not round-trip");
			}

			input.IsStop = true;
			input.StopNavType = NavType.Pole;
			input.PlayIdle = false;
			output = RoundTrip(input);
			if (!output.IsStop || output.StopNavType != NavType.Pole || output.PlayIdle)
				return UnitTestResult.Fail("Navigation stop fields did not round-trip");

			return UnitTestResult.Pass("Begin and stop navigation events round-tripped");
		}

		[UnitTest(name: "Duplicant animation keyframe: compact round-trip", category: "Duplicant")]
		public static UnitTestResult StatePacketRoundTrip()
		{
			var input = new DuplicantStatePacket
			{
				NetId = 51,
				Sequence = 9,
				ServerTimestamp = 5000,
				ActionState = DuplicantActionState.Building,
				TargetCell = 1234,
				CurrentAnimHash = new HashedString("working_loop").hash,
				AnimElapsedTime = 0.75f,
				IsWorking = true,
				HeldItemSymbolHash = new HashedString("gun").hash,
				AnimPlayMode = (byte)KAnim.PlayMode.Loop,
				AnimSpeed = 1.25f,
			};

			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			if (stream.Length != 39)
				return UnitTestResult.Fail($"Expected 39-byte state keyframe, got {stream.Length}");

			stream.Position = 0;
			var output = new DuplicantStatePacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				output.Deserialize(reader);

			if (output.NetId != input.NetId || output.Sequence != input.Sequence
				|| output.ServerTimestamp != input.ServerTimestamp || output.ActionState != input.ActionState
				|| output.TargetCell != input.TargetCell || output.CurrentAnimHash != input.CurrentAnimHash
				|| output.AnimElapsedTime != input.AnimElapsedTime || output.IsWorking != input.IsWorking
				|| output.HeldItemSymbolHash != input.HeldItemSymbolHash
				|| output.AnimPlayMode != input.AnimPlayMode || output.AnimSpeed != input.AnimSpeed)
			{
				return UnitTestResult.Fail("Animation keyframe fields did not round-trip");
			}

			return UnitTestResult.Pass("Compact animation keyframe round-tripped in 39 bytes");
		}

		[UnitTest(name: "Duplicant visual sequence: wrap-safe ordering", category: "Duplicant")]
		public static UnitTestResult SequenceOrderingWrapSafe()
		{
			if (!DuplicantClientController.IsNewerSequence(11, 10))
				return UnitTestResult.Fail("Normal newer sequence rejected");
			if (DuplicantClientController.IsNewerSequence(10, 10))
				return UnitTestResult.Fail("Duplicate sequence accepted");
			if (!DuplicantClientController.IsNewerSequence(1, uint.MaxValue))
				return UnitTestResult.Fail("Wrapped sequence rejected");
			if (DuplicantClientController.IsNewerSequence(uint.MaxValue, 1))
				return UnitTestResult.Fail("Old pre-wrap sequence accepted");
			return UnitTestResult.Pass("Sequence comparison handles duplicates and uint wrap");
		}

		private static NavigatorTransitionPacket RoundTrip(NavigatorTransitionPacket input)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			var output = new NavigatorTransitionPacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				output.Deserialize(reader);
			return output;
		}
	}
}
