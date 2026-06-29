using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Handshake;
using Shared.Profiling;
using ONI_Together.Networking.States;
using Shared;
using Steamworks;
using System;
using System.Runtime.InteropServices;
using ONI_Together.Networking.Components;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static class GameServer
	{
		private static ServerState _state = ServerState.Stopped;
		public static ServerState State => _state;

		private static float _tickAccumulator;
		private static float _tickInterval = 1f / 60f;

		private static void SetState(ServerState newState)
		{
			using var _ = Profiler.Scope();

			if (_state != newState)
			{
				_state = newState;
				DebugConsole.Log($"[GameServer] State changed to: {_state}");
				Game.Instance?.Trigger(MP_HASHES.GameServer_OnStateChanged);
			}
		}

		public static void RefreshTickRate()
		{
			if (Configuration.Instance?.Host?.Server != null)
				_tickInterval = 1f / TickRateToTps(Configuration.Instance.Host.Server.TickRate);
		}

		private static int TickRateToTps(ServerTickRate rate) => rate switch
		{
			ServerTickRate.TPS_20 => 20,
			ServerTickRate.TPS_30 => 30,
			ServerTickRate.TPS_60 => 60,
			ServerTickRate.TPS_90 => 90,
			ServerTickRate.TPS_120 => 120,
			ServerTickRate.TPS_128 => 128,
			_ => 60,
		};

		public static void Start()
		{
			using var _ = Profiler.Scope();

			SetState(ServerState.Preparing);

			NetworkConfig.TransportServer.OnError = () => SetState(ServerState.Error);
			NetworkConfig.TransportServer.Prepare();
			CursorManager.Instance.AssignColor();

			RefreshTickRate();
			_tickAccumulator = 0f;

			SetState(ServerState.Starting);

			MultiplayerSession.IsHost = true;
			NetworkConfig.TransportServer.Start();

			DebugConsole.Log("[GameServer] Game Server started!");
			//MultiplayerSession.InSession = true;
			Game.Instance?.Trigger(MP_HASHES.OnConnected);
			Game.Instance?.Trigger(MP_HASHES.GameServer_OnServerStarted);
			//MultiplayerOverlay.Close();

			SetState(ServerState.Started);
		}

		public static void Shutdown()
		{
			using var _ = Profiler.Scope();

			SetState(ServerState.Stopped);

			NetworkConfig.TransportServer.CloseConnections();
			NetworkConfig.TransportServer.Stop();
			MultiplayerSession.IsHost = false;

			//MultiplayerSession.InSession = false;
			
			DebugConsole.Log("[GameServer] Shutdown complete.");
		}

		public static void Update()
		{
			using var _ = Profiler.Scope();

			_tickAccumulator += Time.unscaledDeltaTime;
			_tickAccumulator = Mathf.Min(_tickAccumulator, _tickInterval * 3f);
			if (_tickAccumulator < _tickInterval)
				return;
			_tickAccumulator -= _tickInterval;

			switch (State)
			{
				case ServerState.Started:
					try
					{
						NetworkConfig.TransportServer.Update();
						NetworkConfig.TransportServer.OnMessageRecieved();

						// Check for lost chunks and retransmit specific missing chunks
						SaveFileTransferManager.CheckForLostChunks();
					}
					catch (Exception ex)
					{
						DebugConsole.LogError($"[GameServer] Error in server update: {ex}");
					}
					break;

				case ServerState.Preparing:
				case ServerState.Starting:
				case ServerState.Stopped:
				case ServerState.Error:
				default:
					// No server activity in these states.
					break;
			}
		}
	}
}
