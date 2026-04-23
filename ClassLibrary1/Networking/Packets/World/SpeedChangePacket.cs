using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Patches;
using System;
using System.IO;
using Shared.Profiling;
using ONI_MP.Networking;
using ONI_MP.Patches.World;

namespace ONI_MP.Networking.Packets.World
{
	public class SpeedChangePacket : IPacket
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
	}
}
