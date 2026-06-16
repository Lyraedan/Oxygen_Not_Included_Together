using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches;
using System;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World
{
	public class SpeedChangePacket : IPacket, IHostRelayGate
	{
		[Flags]
		public enum SpeedState : int
		{
			Paused = -1,
			Normal = 0,
			Double = 1,
			Triple = 2
		}

		public SpeedState Speed { get; set; }

		public SpeedChangePacket() { }

		public SpeedChangePacket(SpeedState speed)
		{
			using var _ = Profiler.Scope();

			Speed = speed;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write((int)Speed);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Speed = (SpeedState)reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (SpeedControlScreen.Instance == null)
				return;

			// Authority: the host must never apply a remote resume while a player is
			// unready — even though this runs under IsSyncing. Without this, a client that
			// originated a resume (its own SetSpeed/TogglePause isn't gated) would resume
			// the host mid-load via this packet, bypassing the SpeedControlScreen gate.
			// Pause packets are always honoured; only resume is rejected.
			if (MultiplayerSession.IsHost
				&& Speed != SpeedState.Paused
				&& !ReadyManager.CanHostResume())
			{
				DebugConsole.Log("[SpeedChangePacket] Ignored remote resume: not all players are ready");
				return;
			}

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

			// Rebroadcast if Host
			//if (MultiplayerSession.IsHost)
			//{
			//	PacketSender.SendToAllClients(this);
			//}

			DebugConsole.Log($"[SpeedChnagePacket] SpeedChangePacket received: Speed set to {Speed}");
		}

		/// <summary>
		/// Relay gate (host side): a pause is always allowed, but a resume may only be
		/// applied AND relayed to the other clients once everyone is ready. This stops the
		/// host from fanning a client-originated resume out to other clients while it
		/// correctly refuses to resume its own sim. Mirrors the inline check in
		/// <see cref="OnDispatched"/>, which stays as defense-in-depth for the direct path.
		/// </summary>
		public bool HostShouldProcess()
			=> Speed == SpeedState.Paused || ReadyManager.CanHostResume();
	}
}
