using System.IO;
using System.Threading;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Patches;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class SpeedChangePacket : IPacket, IClientRelayable, IHostAuthoritativeRelay
	{
		public enum SpeedState : int
		{
			Paused = -1,
			Normal = 0,
			Double = 1,
			Triple = 2
		}

		private static long _hostRevision;
		private static long _lastAppliedRevision;
		private static int _clientBarrierPauseLocked;

		public SpeedState Speed { get; private set; }
		public long Revision { get; private set; }
		public bool BarrierPauseLocked { get; private set; }
		internal static bool IsBarrierPauseLocked => MultiplayerSession.IsHost
			? ReadyManager.HasActiveSyncBarrier
			: MultiplayerSession.IsClientWorldLoadRetained
			  || Volatile.Read(ref _clientBarrierPauseLocked) != 0;

		public SpeedChangePacket() { }

		public SpeedChangePacket(SpeedState speed)
			: this(speed, 0)
		{
		}

		private SpeedChangePacket(SpeedState speed, long revision, bool barrierPauseLocked = false)
		{
			Speed = speed;
			Revision = revision;
			BarrierPauseLocked = barrierPauseLocked;
		}

		public static void ResetSessionState()
		{
			Interlocked.Exchange(ref _hostRevision, 0);
			Interlocked.Exchange(ref _lastAppliedRevision, 0);
			Interlocked.Exchange(ref _clientBarrierPauseLocked, 0);
		}

		public static void SubmitLocalChange(SpeedState speed)
		{
			if (!MultiplayerSession.InSession || !IsValidSpeed(speed))
				return;

			if (MultiplayerSession.IsHost)
			{
				if (!CanApplyDuringSyncBarrier(ReadyManager.HasActiveSyncBarrier, speed))
				{
					EnforceBarrierPauseAndBroadcast();
					return;
				}
				BroadcastAuthoritative(speed);
			}
			else
				PacketSender.SendToAllOtherPeers(new SpeedChangePacket(speed));
		}

		internal static void EnforceClientWorldLoadPause()
		{
			if (!MultiplayerSession.IsClientWorldLoadRetained)
				return;
			Interlocked.Exchange(ref _clientBarrierPauseLocked, 1);
			new SpeedChangePacket(SpeedState.Paused).ApplySpeed();
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			writer.Write((int)Speed);
			writer.Write(Revision);
			writer.Write(BarrierPauseLocked);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			SpeedState speed = (SpeedState)reader.ReadInt32();
			long revision = reader.ReadInt64();
			if (!IsValidSpeed(speed) || revision < 0)
				throw new InvalidDataException("Invalid speed command");
			bool barrierPauseLocked = reader.ReadBoolean();
			if (barrierPauseLocked && speed != SpeedState.Paused)
				throw new InvalidDataException("Barrier speed command must remain paused");
			Speed = speed;
			Revision = revision;
			BarrierPauseLocked = barrierPauseLocked;
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			DispatchContext context = PacketHandler.CurrentContext;
			if (MultiplayerSession.IsHost)
			{
				HandleClientRequest(context);
				return;
			}

			if (SpeedControlScreen.Instance == null
			    || !context.SenderIsHost
			    || !TryAcceptAuthoritativeRevision(Revision))
				return;
#if DEBUG
			if (ShouldIgnoreAuthoritativeSpeed(SoakTickBarrier.IsControllingSpeed))
			{
				DebugConsole.Log($"[SoakTick][IGNORED_SPEED] speed={Speed} revision={Revision}");
				return;
			}
#endif
			Interlocked.Exchange(ref _clientBarrierPauseLocked, BarrierPauseLocked ? 1 : 0);
			ApplySpeed();
		}

		private void HandleClientRequest(DispatchContext context)
		{
			if (!context.IsVerifiedHostBroadcast || Revision != 0)
			{
				DebugConsole.LogWarning("[SpeedChangePacket] Rejected non-request client command");
				return;
			}
			if (!CanApplyDuringSyncBarrier(ReadyManager.HasActiveSyncBarrier, Speed))
			{
				DebugConsole.LogWarning("[SpeedChangePacket] Rejected speed change during sync barrier");
				EnforceBarrierPauseAndBroadcast();
				return;
			}
			if (ApplySpeed())
				BroadcastAuthoritative(Speed);
		}

		internal static bool CanApplyDuringSyncBarrier(bool barrierActive, SpeedState requested)
			=> !barrierActive || requested == SpeedState.Paused;

		internal static bool ShouldBlockLocalSpeedControl(
			bool inSession,
			bool isSyncing,
			bool barrierPauseLocked)
			=> inSession && !isSyncing && barrierPauseLocked;

		internal static bool ShouldIgnoreAuthoritativeSpeed(bool soakControlsSpeed)
			=> soakControlsSpeed;

		private static void EnforceBarrierPauseAndBroadcast()
		{
			new SpeedChangePacket(SpeedState.Paused).ApplySpeed();
			BroadcastAuthoritative(SpeedState.Paused);
		}

		private static void BroadcastAuthoritative(SpeedState speed)
		{
			long revision = Interlocked.Increment(ref _hostRevision);
			PacketSender.SendToAllClients(
				new SpeedChangePacket(speed, revision, ReadyManager.HasActiveSyncBarrier),
				PacketSendMode.ReliableImmediate);
		}

		internal static bool TryAcceptAuthoritativeRevision(long revision)
		{
			if (revision <= 0)
				return false;
			while (true)
			{
				long previous = Interlocked.Read(ref _lastAppliedRevision);
				if (revision <= previous)
					return false;
				if (Interlocked.CompareExchange(ref _lastAppliedRevision, revision, previous) == previous)
					return true;
			}
		}

		private bool ApplySpeed()
		{
			if (SpeedControlScreen.Instance == null)
				return false;
			SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = true;
			try
			{
				if (Speed == SpeedState.Paused)
				{
					if (!SpeedControlScreen.Instance.IsPaused)
						SpeedControlScreen.Instance.TogglePause();
				}
				else
				{
					if (SpeedControlScreen.Instance.IsPaused)
						SpeedControlScreen.Instance.TogglePause();
					SpeedControlScreen.Instance.SetSpeed((int)Speed);
				}
			}
			finally
			{
				SpeedControlScreen_SendSpeedPacketPatch.IsSyncing = false;
			}
			return true;
		}

		private static bool IsValidSpeed(SpeedState speed)
			=> speed is SpeedState.Paused or SpeedState.Normal or SpeedState.Double or SpeedState.Triple;
	}
}
