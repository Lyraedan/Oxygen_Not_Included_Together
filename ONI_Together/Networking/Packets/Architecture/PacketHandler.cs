using System;
using System.IO;
using System.Threading;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Architecture
{
	internal enum RelayProvenance
	{
		DirectTransport,
		VerifiedHostBroadcast
	}

	internal enum EnvelopeProvenance
	{
		DirectTransport,
		ChunkReassembled,
		OrderedInner,
		ReliablePageInner,
	}

	public readonly struct DispatchContext
	{
		public ulong SenderId { get; }
		public bool SenderIsHost { get; }
		public long ConnectionGeneration { get; }
		public long SessionEpoch { get; }
		internal bool IsVerifiedHostBroadcast => Provenance == RelayProvenance.VerifiedHostBroadcast;
		internal RelayProvenance Provenance { get; }
		internal EnvelopeProvenance Envelope { get; }

		public DispatchContext(ulong senderId, bool senderIsHost)
			: this(senderId, senderIsHost, 0, 0, RelayProvenance.DirectTransport)
		{
		}

		public DispatchContext(ulong senderId, bool senderIsHost, long connectionGeneration)
			: this(senderId, senderIsHost, connectionGeneration, 0, RelayProvenance.DirectTransport)
		{
		}

		public DispatchContext(
			ulong senderId, bool senderIsHost, long connectionGeneration, long sessionEpoch)
			: this(
				senderId, senderIsHost, connectionGeneration, sessionEpoch,
				RelayProvenance.DirectTransport)
		{
		}

		private DispatchContext(
			ulong senderId,
			bool senderIsHost,
			long connectionGeneration,
			long sessionEpoch,
			RelayProvenance provenance,
			EnvelopeProvenance envelope = EnvelopeProvenance.DirectTransport)
		{
			SenderId = senderId;
			SenderIsHost = senderIsHost;
			ConnectionGeneration = connectionGeneration;
			SessionEpoch = sessionEpoch;
			Provenance = provenance;
			Envelope = envelope;
		}

		internal DispatchContext AsVerifiedHostBroadcast()
			=> new DispatchContext(
				SenderId, SenderIsHost, ConnectionGeneration, SessionEpoch,
				RelayProvenance.VerifiedHostBroadcast, Envelope);

		internal DispatchContext AsChunkReassembled()
			=> WithEnvelope(EnvelopeProvenance.ChunkReassembled);

		internal DispatchContext AsOrderedInner()
			=> WithEnvelope(EnvelopeProvenance.OrderedInner);

		internal DispatchContext AsReliablePageInner()
			=> WithEnvelope(EnvelopeProvenance.ReliablePageInner);

		private DispatchContext WithEnvelope(EnvelopeProvenance envelope)
			=> new DispatchContext(
				SenderId, SenderIsHost, ConnectionGeneration, SessionEpoch,
				Provenance, envelope);
	}

	public static class PacketHandler
	{
		public const int MaxPacketSize = 16 * 1024 * 1024;

		private static bool _readyToProcess = true;
		private static float _notReadySince = float.MaxValue;
		private const float NOT_READY_TIMEOUT = 60f;
		private static long _clientSessionEpoch;
#if DEBUG
		internal static bool BypassReadyGateForTests;
		internal static bool BypassTrackingForTests;
#endif
		public static DispatchContext CurrentContext { get; private set; }

		internal static void ResetSessionState()
		{
			_readyToProcess = true;
			_notReadySince = float.MaxValue;
			Interlocked.Exchange(ref _clientSessionEpoch, 0);
			CurrentContext = default;
			SpeedChangePacket.ResetSessionState();
			RedAlertStatePacket.ResetSessionState();
		}

		internal static void SetClientSessionEpoch(long epoch)
			=> Interlocked.Exchange(ref _clientSessionEpoch, epoch);

		public static bool readyToProcess
		{
			get => _readyToProcess;
			set
			{
				if (!value)
					_notReadySince = Time.unscaledTime;
				_readyToProcess = value;
			}
		}

		public static void HandleIncoming(byte[] data, DispatchContext context)
			=> TryHandleIncoming(data, context);

		internal static bool TryHandleIncoming(byte[] data, DispatchContext context)
			=> TryHandleIncoming(data, context, discardStaleClientRuntime: false);

		internal static bool TryHandleIncomingReliableFrame(byte[] data, DispatchContext context)
			=> TryHandleIncoming(data, context, discardStaleClientRuntime: true);

		private static bool TryHandleIncoming(
			byte[] data,
			DispatchContext context,
			bool discardStaleClientRuntime)
		{
			using var _ = Profiler.Scope();
			if (data == null || data.Length < sizeof(int) || data.Length > MaxPacketSize)
			{
				DebugConsole.LogWarning($"[PacketHandler] Rejected packet with invalid size {data?.Length ?? 0}");
				return false;
			}

			var previousContext = CurrentContext;
			CurrentContext = context;

			try
			{
				if (
#if DEBUG
				    !BypassReadyGateForTests &&
#endif
				    !CanProcessNow())
					return false;
				return ProcessIncoming(data, context, discardStaleClientRuntime);
			}
			catch (Exception ex)
			{
				DebugConsole.LogWarning($"[PacketHandler] Rejected malformed packet from {context.SenderId}: {ex}");
				if (IsOrderedEnvelopeData(data))
					OrderedReliableChannel.RejectMalformed(context);
				return false;
			}
			finally
			{
				CurrentContext = previousContext;
			}
		}

		private static bool CanProcessNow()
		{
#if DEBUG
			if (BypassReadyGateForTests)
				return true;
#endif
			if (_readyToProcess)
				return true;
			if (Time.unscaledTime - _notReadySince <= NOT_READY_TIMEOUT)
				return false;
			DebugConsole.LogWarning(
				$"[PacketHandler] readyToProcess was false for >{NOT_READY_TIMEOUT}s — force-recovering");
			_readyToProcess = true;
			return true;
		}

		private static bool ProcessIncoming(
			byte[] data,
			DispatchContext context,
			bool discardStaleClientRuntime)
		{
			using var ms = new MemoryStream(data);
			using var reader = new BinaryReader(ms);
			int type = reader.ReadInt32();
			if (!PacketRegistry.HasRegisteredPacket(type))
			{
				DebugConsole.LogError($"Invalid PacketType received: {type}", false);
				return false;
			}

			using var scope = Profiler.Scope();
			IPacket packet = PacketRegistry.Create(type);
			if (!CanDispatchEnvelope(packet, context.Envelope))
			{
				if (IsTransportEnvelope(packet))
					PacketSender.TerminateIncomingReliableStream(context);
				return false;
			}
			bool canDispatch = CanDispatchPacket(packet, context, MultiplayerSession.IsHost);
			bool discard = !canDispatch && discardStaleClientRuntime
				&& ShouldDiscardStaleClientRuntime(packet, context, MultiplayerSession.IsHost);
			if (!canDispatch && !discard)
			{
				DebugConsole.LogWarning(
					$"[PacketHandler] Rejected packet origin for {packet.GetType().Name} from {context.SenderId}");
				return false;
			}
			packet.Deserialize(reader);
			if (reader.BaseStream.Position != reader.BaseStream.Length)
			{
				DebugConsole.LogWarning(
					$"[PacketHandler] Rejected trailing payload for {packet.GetType().Name} from {context.SenderId}");
				if (packet is OrderedReliablePacket)
					OrderedReliableChannel.RejectMalformed(context);
				return false;
			}
			if (discard)
				return true;
			Dispatch(packet);
			scope.End(packet.GetType().Name, data.Length);
			if (ShouldTrackIncoming(packet)
#if DEBUG
			    && !BypassTrackingForTests
#endif
			   )
				PacketTracker.TrackIncoming(new PacketTracker.PacketTrackData { packet = packet, size = data.Length });
			return true;
		}

		internal static bool ShouldTrackIncoming(IPacket packet)
			=> packet is not (OrderedReliablePacket or ReliablePagePacket
				or ReliablePageAckPacket or ChunkedPacket);

		private static bool IsOrderedEnvelopeData(byte[] data)
			=> data != null && data.Length >= sizeof(int)
			   && BitConverter.ToInt32(data, 0)
			   == API_Helper.GetHashCode(typeof(OrderedReliablePacket));

		internal static bool DispatchNested(IPacket packet, DispatchContext context)
		{
			if (!CanDispatchPacket(packet, context, MultiplayerSession.IsHost))
			{
				DebugConsole.LogWarning($"[PacketHandler] Rejected nested packet origin for {packet.GetType().Name} from {context.SenderId}");
				return false;
			}

			var previousContext = CurrentContext;
			CurrentContext = context;
			try
			{
				Dispatch(packet);
				return true;
			}
			catch (Exception ex)
			{
				DebugConsole.LogWarning($"[PacketHandler] Nested {packet.GetType().Name} failed for {context.SenderId}: {ex}");
				return false;
			}
			finally
			{
				CurrentContext = previousContext;
			}
		}

		internal static bool CanDispatchPacket(IPacket packet, DispatchContext context, bool localIsHost)
		{
			if (!CanDispatchEnvelope(packet, context.Envelope))
				return false;
			if (packet is IHostOnlyPacket && !context.SenderIsHost)
				return false;

			if (!IsCurrentConnectionContext(context, localIsHost, out MultiplayerPlayer player))
				return false;

			if (!localIsHost || context.SenderIsHost)
				return true;

			player ??= MultiplayerSession.GetPlayer(context.SenderId);
			if (!CanDispatchClientPacket(packet, player?.ProtocolVerified == true,
				    player?.readyState ?? ClientReadyState.Unready))
				return false;
			if (packet is IModApiPacket)
				return PacketRegistry.CanClientDispatchModApi(packet, context.IsVerifiedHostBroadcast);
			if (packet is not IClientRelayable)
				return true;

			return IsVerifiedClientRelay(context, protocolVerified: true);
		}

		private static bool ShouldDiscardStaleClientRuntime(
			IPacket packet,
			DispatchContext context,
			bool localIsHost)
		{
			if (!localIsHost || context.SenderIsHost || packet is IHostOnlyPacket)
				return false;
			if (!IsCurrentConnectionContext(context, localIsHost, out MultiplayerPlayer player))
				return false;
			player ??= MultiplayerSession.GetPlayer(context.SenderId);
			if (player?.ProtocolVerified != true
			    || SyncBarrier.IsExactReady(player.readyState)
			    || CanDispatchClientPacket(packet, true, player.readyState)
			    || !CanDispatchClientPacket(packet, true, ClientReadyState.Ready))
				return false;
			if (packet is IModApiPacket
			    && !PacketRegistry.CanClientDispatchModApi(packet, context.IsVerifiedHostBroadcast))
				return false;
			return packet is not IClientRelayable
			       || IsVerifiedClientRelay(context, protocolVerified: true);
		}

		internal static bool IsCurrentDispatchContext(DispatchContext context)
			=> IsCurrentConnectionContext(
				context, MultiplayerSession.IsHost, out _);

		private static bool IsCurrentConnectionContext(
			DispatchContext context,
			bool localIsHost,
			out MultiplayerPlayer player)
		{
			player = null;
			if (context.ConnectionGeneration != 0
			    && ((player = MultiplayerSession.GetPlayer(context.SenderId)) == null
			        || player.ConnectionGeneration != context.ConnectionGeneration))
				return false;

			long clientSessionEpoch = Interlocked.Read(ref _clientSessionEpoch);
			if (!localIsHost && context.SenderIsHost
			    && context.SessionEpoch > 0 && clientSessionEpoch == 0)
				return false;
			return localIsHost || !context.SenderIsHost || clientSessionEpoch == 0
			       || context.SenderId == MultiplayerSession.HostUserID
			       && context.ConnectionGeneration > 0
			       && context.SessionEpoch == clientSessionEpoch;
		}

		private static bool CanDispatchEnvelope(IPacket packet, EnvelopeProvenance envelope)
		{
			if (packet is ChunkedPacket)
				return envelope == EnvelopeProvenance.DirectTransport;
			if (packet is OrderedReliablePacket)
				return envelope is EnvelopeProvenance.DirectTransport
					or EnvelopeProvenance.ChunkReassembled;
			if (packet is ReliablePagePacket or ReliablePageAckPacket)
				return envelope == EnvelopeProvenance.OrderedInner;
			return true;
		}

		private static bool IsTransportEnvelope(IPacket packet)
			=> packet is ChunkedPacket or OrderedReliablePacket
				or ReliablePagePacket or ReliablePageAckPacket;

		internal static bool CanDispatchClientPacket(
			IPacket packet,
			bool protocolVerified,
			ClientReadyState readyState)
		{
			if (packet is GameStateRequestPacket or OrderedReliablePacket or ChunkedPacket
			    or ReliablePagePacket or ReliablePageAckPacket)
				return true;
			if (!protocolVerified)
				return false;
			if (IsPreReadyControl(packet))
				return true;
			return SyncBarrier.IsExactReady(readyState);
		}

		internal static bool CanSendClientPacket(IPacket packet, ClientState state)
			=> packet != null && (GameClient.CanSendRuntimeRequests(state)
			   || packet is GameStateRequestPacket or ReadyAcceptedAckPacket
			   || IsPreReadyControl(packet));

		private static bool IsPreReadyControl(IPacket packet)
		{
				return packet is SaveFileRequestPacket
				    or WorldDataRequestPacket
				    or WorldDataProgressAckPacket
				    or TcpFallbackRequestPacket
			    or ClientReadyStatusPacket
			    or SyncProgressPacket
			    or ChunkAckPacket
			    or OrderedReliablePacket;
		}

		internal static bool IsVerifiedClientRelay(DispatchContext context, bool protocolVerified)
			=> context.IsVerifiedHostBroadcast && protocolVerified;

		private static void Dispatch(IPacket packet)
		{
			using var _ = Profiler.Scope();

			packet.OnDispatched();
		}
	}

}
