using Klei.AI;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DuplicantActions;
using Shared.Profiling;
using System;
using System.Diagnostics;
using UnityEngine;
using static STRINGS.DUPLICANTS.STATS;

namespace ONI_Together.Networking.Synchronization
{
	// Attached to minions on the Host side.
	// Periodically sends vitals to clients. One packet per dupe per second,
	// sent Unreliable — steady-state drift, self-heals on next tick if dropped.
	// (Invariant #3: steady-state drift → Unreliable.)
	public class VitalStatsSyncer : KMonoBehaviour, ISim1000ms
	{
		[MyCmpReq]
		private NetworkIdentity _identity;
		[MyCmpReq]
		private PrimaryElement _element;
		private Amounts _amounts;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();
			_amounts = gameObject.GetAmounts();
		}

		public void Sim1000ms(float dt)
		{
			using var _ = Profiler.Scope();

			try
			{
				if (!MultiplayerSession.IsHostInSession) return;

				// Skip if no clients connected
				if (!MultiplayerSession.SessionHasPlayers) return;

				// Previously: foreach(var amountInstance in _amounts) ... — loop variable unused,
				// sent the same full packet N times (N = 12 Amounts) per dupe per second, Reliable.
				// That storm (12 * num_dupes Reliable packets/s) triggered Riptide pendingMessages
				// key collisions and the ~20-cycle session crash. One Unreliable send is sufficient:
				// VitalStatsPacket is idempotent set-last-value, next tick resyncs if dropped.
				var sw = Stopwatch.StartNew();
				var packet = new VitalStatsPacket(_identity.NetId, _amounts, _element);
				var bytes = packet.SerializeToByteArray();
				PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
				sw.Stop();
				SyncStats.RecordSync(SyncStats.VitalStats, 1, bytes.Length, (float)sw.Elapsed.TotalMilliseconds);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[VitalStatsSyncer] Exception: {ex}");
			}
		}
	}
}
