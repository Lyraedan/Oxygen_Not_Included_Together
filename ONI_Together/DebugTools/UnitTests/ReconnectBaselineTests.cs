#if DEBUG
using System.Collections.Generic;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport;
using ONI_Together.Networking.Transport.Lan;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ReconnectBaselineTests
	{
		private sealed class ThrowingSnapshotPacket : IPacket
		{
			internal int SerializeCount { get; private set; }

			public void Serialize(System.IO.BinaryWriter writer)
			{
				SerializeCount++;
				throw new System.IO.InvalidDataException("Synthetic snapshot failure");
			}

			public void Deserialize(System.IO.BinaryReader reader) { }
			public void OnDispatched() { }
		}

		private sealed class CountingPacketSender : TransportPacketSender
		{
			internal int SendCount { get; private set; }
			internal readonly List<IPacket> Packets = new();

			public override bool SendPacket(
				object conn, IPacket packet,
				PacketSendMode sendType = PacketSendMode.ReliableImmediate)
			{
				SendCount++;
				Packets.Add(packet);
				return true;
			}
		}

		private sealed class BaselineCutFixture : System.IDisposable
		{
			private readonly TransportPacketSender _originalSender = NetworkConfig.TransportPacketSender;
			private readonly ulong _originalHostId = MultiplayerSession.HostUserID;
			private readonly bool _originalIsHost = MultiplayerSession.IsHost;
			private readonly Dictionary<ulong, MultiplayerPlayer> _originalPlayers =
				new(MultiplayerSession.ConnectedPlayers);

			internal BaselineCutFixture()
			{
				ReadyManager.ResetSessionState();
				PacketSender.ResetSessionState();
				MultiplayerSession.ConnectedPlayers.Clear();
				MultiplayerSession.HostUserID = 1;
				MultiplayerSession.IsHost = true;
				NetworkConfig.TransportPacketSender = Sender;
				Player = new MultiplayerPlayer(2);
				Player.BeginConnection(new object());
				Player.ProtocolVerified = true;
				MultiplayerSession.ConnectedPlayers.Add(2, Player);
			}

			internal CountingPacketSender Sender { get; } = new();
			internal MultiplayerPlayer Player { get; }

			internal bool TryStart(out long generation)
			{
				generation = 0;
				return ReadyManager.BeginSyncBarrier(2)
				       && ReadyManager.BeginSnapshotEpoch(2, out generation)
				       && ReadyManager.SetPlayerReadyState(
					       Player, ClientReadyState.Loading, 99, generation);
			}

			public void Dispose()
			{
				ReadyManager.ResetSessionState();
				PacketSender.ResetSessionState();
				MultiplayerSession.ConnectedPlayers.Clear();
				foreach (var pair in _originalPlayers)
					MultiplayerSession.ConnectedPlayers.Add(pair.Key, pair.Value);
				MultiplayerSession.HostUserID = _originalHostId;
				MultiplayerSession.IsHost = _originalIsHost;
				NetworkConfig.TransportPacketSender = _originalSender;
			}
		}

		[UnitTest(name: "Overflowed reliable journal is terminal", category: "Networking")]
		public static UnitTestResult OverflowedJournalCannotTransfer()
		{
			ReliableSyncBacklog.ClearAll();
			try
			{
				ReliableSyncBacklog.Begin(71);
				var invalid = new ThrowingSnapshotPacket();
				if (ReliableSyncBacklog.TryBuffer(71, invalid, PacketSendMode.Reliable)
				        != SyncBacklogResult.Overflow
				    || ReliableSyncBacklog.TryBuffer(71, invalid, PacketSendMode.Reliable)
				        != SyncBacklogResult.Terminated)
					return UnitTestResult.Fail("Overflowed journal did not enter a terminal state");

				if (invalid.SerializeCount != 1 || ReliableSyncBacklog.CanTransfer(71, 72)
				    || ReliableSyncBacklog.Transfer(71, 72))
					return UnitTestResult.Fail("Terminal journal was re-serialized, re-reported, or transferred");
				return UnitTestResult.Pass("Overflow is reported once and cannot poison reconnect");
			}
			finally
			{
				ReliableSyncBacklog.ClearAll();
			}
		}

		[UnitTest(name: "Fresh snapshot clears stale loading epoch", category: "Networking")]
		public static UnitTestResult FreshSnapshotClearsStaleLoadingEpoch()
		{
			TransportServer originalServer = NetworkConfig.TransportServer;
			ulong originalHostId = MultiplayerSession.HostUserID;
			bool originalIsHost = MultiplayerSession.IsHost;
			var originalPlayers = new Dictionary<ulong, MultiplayerPlayer>(MultiplayerSession.ConnectedPlayers);
			var server = new RiptideServer();
			try
			{
				ReadyManager.ResetSessionState();
				MultiplayerSession.ConnectedPlayers.Clear();
				MultiplayerSession.HostUserID = 1;
				MultiplayerSession.IsHost = true;
				NetworkConfig.TransportServer = server;
				var player = new MultiplayerPlayer(2);
				player.BeginConnection(new object());
				player.ProtocolVerified = true;
				MultiplayerSession.ConnectedPlayers.Add(2, player);
				if (!ReadyManager.BeginSyncBarrier(2)
				    || !ReadyManager.BeginSnapshotEpoch(2, out long oldGeneration)
				    || !ReadyManager.SetPlayerReadyState(player, ClientReadyState.Loading, 99, oldGeneration))
					return UnitTestResult.Fail("Could not arrange stale loading epoch");
				server.MarkClientLoading(2, 99);
				ReliableSyncBacklog.TryBuffer(
					2, new DeferredReliablePacket(System.Array.Empty<byte>()), PacketSendMode.Reliable);

				ReadyManager.PrepareFreshSnapshot(2);
				if (ReadyManager.IsClientInSyncBarrier(2) || ReadyManager.HasReconnectProof(2, 99)
				    || server.IsClientLoading(2) || ReliableSyncBacklog.CanTransfer(2, 2))
					return UnitTestResult.Fail("Fresh snapshot inherited stale barrier, token, or journal");
				if (!ReadyManager.BeginSyncBarrier(2)
				    || !ReadyManager.BeginSnapshotEpoch(2, out long freshGeneration)
				    || freshGeneration <= oldGeneration)
					return UnitTestResult.Fail("Token-free reconnect did not start a fresh snapshot epoch");
				return UnitTestResult.Pass("Fresh snapshot starts after complete stale-epoch cleanup");
			}
			finally
			{
				ReadyManager.ResetSessionState();
				MultiplayerSession.ConnectedPlayers.Clear();
				foreach (var pair in originalPlayers)
					MultiplayerSession.ConnectedPlayers.Add(pair.Key, pair.Value);
				MultiplayerSession.HostUserID = originalHostId;
				MultiplayerSession.IsHost = originalIsHost;
				NetworkConfig.TransportServer = originalServer;
			}
		}

		[UnitTest(name: "Reconnect Ready is gated by the current world baseline", category: "Networking")]
		public static UnitTestResult ReadyRequiresCurrentBaseline()
		{
			if (!GameClient.ShouldRequestWorldBaseline(true, 9, 4)
			    || GameClient.ShouldRequestWorldBaseline(true, 0, 4)
			    || GameClient.ShouldRequestWorldBaseline(true, 9, 0)
			    || GameClient.ShouldRequestWorldBaseline(false, 9, 4))
			{
				return UnitTestResult.Fail("World baseline request gate accepted an invalid reconnect state");
			}

			if (!GameClient.ShouldCompleteWorldBaseline(4, 4, 9)
			    || GameClient.ShouldCompleteWorldBaseline(3, 4, 9)
			    || GameClient.ShouldCompleteWorldBaseline(4, 4, 0))
			{
				return UnitTestResult.Fail("Reconnect could complete without the current generation and token");
			}
			if (!GameClient.ShouldCompleteReadyAcceptance(ClientState.AwaitingReadyAck, 4, 4)
			    || GameClient.ShouldCompleteReadyAcceptance(ClientState.Connected, 4, 4)
			    || GameClient.ShouldCompleteReadyAcceptance(ClientState.AwaitingReadyAck, 4, 3))
			{
				return UnitTestResult.Fail("Client entered InGame before the exact Ready acknowledgement");
			}
			if (!PacketHandler.CanDispatchClientPacket(
				    new WorldDataRequestPacket(), true, ClientReadyState.Loading)
			    || PacketHandler.CanDispatchClientPacket(
				    new WorldDataRequestPacket(), false, ClientReadyState.Loading))
			{
				return UnitTestResult.Fail("A verified loading client could not request its baseline safely");
			}

			var barrier = new SyncBarrier();
			barrier.Add(12, isPaused: true);
			barrier.MarkTransferStarted(12, 4);
			barrier.TryAcceptLoading(12, 9, 4);
			if (barrier.CanComplete(12, 9, 4))
				return UnitTestResult.Fail("Ready was accepted before a world baseline started");
			if (!barrier.TryBeginWorldBaseline(12, 4)
			    || barrier.TryBeginWorldBaseline(12, 4)
			    || barrier.TryBeginWorldBaseline(12, 3)
			    || !barrier.CanComplete(12, 9, 4))
			{
				return UnitTestResult.Fail("World baseline cut was not single-use and generation-bound");
			}

			ReliableSyncBacklog.ClearAll();
			try
			{
				ReliableSyncBacklog.Begin(12);
				ReliableSyncBacklog.TryBuffer(
					12, new WorldCyclePacket { Cycle = 1, CycleTime = 1f }, PacketSendMode.Reliable);
				ReliableSyncBacklog.Begin(12);
				if (ReliableSyncBacklog.CountForTests(12) != 0)
					return UnitTestResult.Fail("World baseline cut retained pre-baseline deltas");
				ReliableSyncBacklog.TryBuffer(
					12, new WorldCyclePacket { Cycle = 1, CycleTime = 2f }, PacketSendMode.Reliable);
				if (ReliableSyncBacklog.CountForTests(12) != 1)
					return UnitTestResult.Fail("World baseline cut did not journal post-baseline deltas");
			}
			finally
			{
				ReliableSyncBacklog.ClearAll();
			}

			return UnitTestResult.Pass("Reconnect remains Unready until the current baseline is applied");
		}

		[UnitTest(name: "World baseline transfer is ACK-windowed and progress-bound", category: "Networking")]
		public static UnitTestResult WorldBaselineTransferIsAckWindowed()
		{
			const int totalChunks = 252;
			var sent = new List<int>();
			var window = new WorldDataSendWindow(totalChunks);
			if (!window.TrySendAvailable(index =>
			    {
				    sent.Add(index);
				    return true;
			    })
			    || sent.Count != WorldDataSendWindow.MaxInFlightChunks)
				return UnitTestResult.Fail("Baseline sender injected the full transfer before progress ACKs");

			for (int applied = 0; applied < 121; applied++)
			{
				if (!window.AcceptProgress(applied)
				    || !window.TrySendAvailable(index =>
				    {
					    sent.Add(index);
					    return true;
				    })
				    || window.InFlightChunks > WorldDataSendWindow.MaxInFlightChunks)
					return UnitTestResult.Fail("Contiguous baseline progress broke its bounded send window");
			}
			if (window.IsComplete || sent.Contains(totalChunks - 1))
				return UnitTestResult.Fail("121 received chunks incorrectly completed or exposed the final baseline");

			for (int applied = 121; applied < totalChunks; applied++)
			{
				if (!window.AcceptProgress(applied)
				    || !window.TrySendAvailable(_ => true))
					return UnitTestResult.Fail("Complete contiguous progress could not drain the baseline window");
			}
			return window.IsComplete
			       && !GameClient.ShouldCompleteWorldBaseline(0, 4, 9)
				? UnitTestResult.Pass("Final baseline is exposed only after every bounded window is applied")
				: UnitTestResult.Fail("Transport completion bypassed application observation or did not complete");
		}

		[UnitTest(name: "Connection replacement invalidates an active world baseline", category: "Networking")]
		public static UnitTestResult ConnectionReplacementRequiresFreshSnapshot()
		{
			using var fixture = new BaselineCutFixture();
			if (!fixture.TryStart(out long generation)
			    || !ReadyManager.TryBeginWorldBaseline(2, generation))
				return UnitTestResult.Fail("Could not arrange an active connection-bound baseline");

			fixture.Player.BeginConnection(new object());
			if (ReadyManager.GetReconnectProofStatus(
				    2, 99, requireSameCompletedClient: true) != ReconnectProofStatus.Missing)
			{
				return UnitTestResult.Fail(
					"An unfinished baseline remained resumable on a replacement connection");
			}
			ReadyManager.PrepareFreshSnapshot(2);
			if (ReadyManager.IsClientInSyncBarrier(2)
			    || ReadyManager.HasPendingReadyCommitForTests(2)
			    || WorldDataRequestPacket.HasActiveTransferForTests(2, generation))
				return UnitTestResult.Fail("Fresh snapshot cleanup retained old reconnect state");

			return UnitTestResult.Pass(
				"Connection replacement invalidates the unfinished baseline atomically");
		}

		[UnitTest(name: "World baseline cut discards only pre-baseline reliable deltas", category: "Networking")]
		public static UnitTestResult WorldBaselineCutDoesNotFlushStaleDeltas()
		{
			using var fixture = new BaselineCutFixture();
			if (!fixture.TryStart(out long generation)
			    || ReliableSyncBacklog.TryBuffer(
				    2, new WorldCyclePacket { Cycle = 1, CycleTime = 1f },
				    PacketSendMode.Reliable) != SyncBacklogResult.Buffered)
				return UnitTestResult.Fail("Could not arrange a pre-baseline reliable delta");

			if (!ReadyManager.TryBeginWorldBaseline(2, generation)
			    || ReliableSyncBacklog.CountForTests(2) != 0
			    || fixture.Sender.SendCount != 0
			    || fixture.Sender.PendingCountForTests(fixture.Player.Connection) != 0)
				return UnitTestResult.Fail("Baseline cut flushed instead of discarding superseded deltas");

			if (ReliableSyncBacklog.TryBuffer(
				    2, new WorldCyclePacket { Cycle = 1, CycleTime = 2f },
				    PacketSendMode.Reliable) != SyncBacklogResult.Buffered
			    || ReadyManager.TryBeginWorldBaseline(2, generation)
			    || ReliableSyncBacklog.CountForTests(2) != 1
			    || fixture.Sender.SendCount != 0
			    || fixture.Sender.PendingCountForTests(fixture.Player.Connection) != 0)
				return UnitTestResult.Fail("A rejected second cut erased or sent post-baseline deltas");

			return UnitTestResult.Pass("Baseline cut discards old deltas and journals only newer state");
		}

		[UnitTest(name: "World baseline progress renews only its exact generation lease", category: "Networking")]
		public static UnitTestResult WorldBaselineProgressRenewsIdleLease()
		{
			var lease = new WorldBaselineProgressLease(
				generation: 7, startedAt: 0f, idleTimeoutSeconds: 10f,
				absoluteTimeoutSeconds: 60f);
			if (!lease.TryAdvance(7, 0, 252, 9f) || lease.IsTimedOut(15f))
				return UnitTestResult.Fail("Contiguous progress did not renew the no-progress deadline");
			if (lease.TryAdvance(6, 1, 252, 18f)
			    || lease.TryAdvance(7, 2, 252, 18f)
			    || !lease.IsTimedOut(19f))
				return UnitTestResult.Fail("Stale or non-contiguous progress renewed the baseline lease");

			var capped = new WorldBaselineProgressLease(
				generation: 7, startedAt: 0f, idleTimeoutSeconds: 10f,
				absoluteTimeoutSeconds: 60f);
			for (int chunk = 0; chunk < 6; chunk++)
			{
				if (!capped.TryAdvance(7, chunk, 252, 9f * (chunk + 1)))
					return UnitTestResult.Fail("Valid baseline progress was rejected before its absolute cap");
			}
			if (!capped.IsTimedOut(60f))
				return UnitTestResult.Fail("Baseline progress escaped its absolute deadline");

			return UnitTestResult.Pass("Only exact contiguous progress renews idle time within an absolute cap");
		}

		[UnitTest(name: "Loading client may send only generation-bound baseline progress", category: "Networking")]
		public static UnitTestResult LoadingClientAllowsBaselineProgressControl()
		{
			if (!PacketHandler.CanDispatchClientPacket(
				    new WorldDataProgressAckPacket(), true, ClientReadyState.Loading)
			    || PacketHandler.CanDispatchClientPacket(
				    new WorldDataProgressAckPacket(), false, ClientReadyState.Loading)
			    || PacketHandler.CanDispatchClientPacket(
				    new WorldUpdatePacket(), true, ClientReadyState.Loading))
			{
				return UnitTestResult.Fail("Baseline progress widened the loading-client packet surface");
			}
			return UnitTestResult.Pass("Loading permits only the verified baseline progress control");
		}

		[UnitTest(name: "Reconnect Ready requires exact lifecycle membership", category: "Networking")]
		public static UnitTestResult ReadyRequiresExactMembership()
		{
			var baseline = new List<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry>
			{
				new(41, 1, false),
				new(42, 2, true)
			};
			var valid = NetworkIdentityRegistry.ValidateLifecycleMembership(baseline, new[] { 41 });
			var missing = NetworkIdentityRegistry.ValidateLifecycleMembership(baseline, new int[0]);
			var unexpected = NetworkIdentityRegistry.ValidateLifecycleMembership(baseline, new[] { 41, 43 });
			var tombstoned = NetworkIdentityRegistry.ValidateLifecycleMembership(baseline, new[] { 41, 42 });
			var duplicate = NetworkIdentityRegistry.ValidateLifecycleMembership(
				new[] { new NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry(41, 3, false), baseline[0] },
				new[] { 41 });

			if (!valid.IsValid || missing.MissingLiveCount != 1 || missing.IsValid ||
			    unexpected.UnexpectedLiveCount != 1 || unexpected.IsValid ||
			    tombstoned.TombstonedLiveCount != 1 || tombstoned.IsValid ||
			    duplicate.BaselineValid || duplicate.IsValid)
			{
				return UnitTestResult.Fail("Lifecycle membership accepted missing, extra, or tombstoned live identities");
			}
			return UnitTestResult.Pass("Ready requires an exact live registry cut");
		}

	}
}
#endif
