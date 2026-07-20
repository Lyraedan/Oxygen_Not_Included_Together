using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using ONI_Together.Patches.ToolPatches;
using Shared;
using Shared.Profiling;
using System;
using System.Collections;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static partial class GameClient
	{
		private static bool RequestWorldBaseline()
		{
			long generation = ReadyManager.ClientSnapshotGeneration;
			if (!ShouldRequestWorldBaseline(Utils.IsInGame(), ReadyManager.ReconnectToken, generation))
				return false;
			DebugConsole.Log($"[GameClient] Requesting world baseline generation {generation}");
			bool sent = PacketSender.SendToPlayer(MultiplayerSession.HostUserID, new WorldDataRequestPacket
			{
				SenderId = MultiplayerSession.LocalUserID,
				SnapshotGeneration = generation,
			}, PacketSendMode.ReliableImmediate);
			if (sent)
				BeginWorldBaselineWait(ReadyManager.ReconnectToken, generation);
			return sent;
		}

		private static void OnWorldBaselineApplied(long snapshotGeneration)
		{
			if (MultiplayerSession.IsHost || State != ClientState.Connected || !Utils.IsInGame()
			    || !ShouldCompleteWorldBaseline(
				    snapshotGeneration,
				    ReadyManager.ClientSnapshotGeneration,
				    ReadyManager.ReconnectToken))
			{
				return;
			}

			_awaitingReadyGeneration = snapshotGeneration;
			EndWorldBaselineWait(ReadyManager.ReconnectToken, snapshotGeneration);
			SetState(ClientState.AwaitingReadyAck);
			if (!ReadyManager.SendReadyStatusPacket(ClientReadyState.Ready))
			{
				FailWorldBaseline("Could not send Ready after applying the world baseline.");
				return;
			}
			BeginReadyAcceptanceWait(ReadyManager.ReconnectToken, snapshotGeneration);
			DebugConsole.Log($"[GameClient] World baseline {snapshotGeneration} applied; awaiting host Ready acknowledgement");
		}

		internal static void OnReadyAccepted(long snapshotGeneration)
		{
			if (!ShouldCompleteReadyAcceptance(State, _awaitingReadyGeneration, snapshotGeneration)
			    || !Utils.IsInGame())
				return;

			_awaitingReadyGeneration = 0;
			CancelWorldLoadPhase();
			SetState(ClientState.InGame);
			EndWorldLoadReconnect();
			if (IsHardSyncInProgress)
			{
				IsHardSyncInProgress = false;
				DebugConsole.Log("[GameClient] Cleared HardSyncInProgress flag");
			}
			Game.Instance?.Trigger(MP_HASHES.GameClient_OnConnectedInGame);
			MultiplayerSession.CreateConnectedPlayerCursors();
			SelectToolPatch.UpdateColor();
			MultiplayerOverlay.Close();
			ResetReconnectState();
			DebugConsole.Log($"[GameClient] Reconnection setup complete after Ready acknowledgement {snapshotGeneration}");
		}

		private static void FailWorldBaseline(string message)
		{
			DebugConsole.LogError($"[GameClient] {message}");
			_awaitingReadyGeneration = 0;
			FailConnectionValidation(STRINGS.UI.PROTOCOL.VALIDATION.TITLE, message);
		}

		internal static void RejectWorldBaseline(long snapshotGeneration, string message)
		{
			if (MultiplayerSession.IsHost ||
			    !ReadyManager.IsCurrentClientSnapshot(snapshotGeneration))
			{
				return;
			}
			FailWorldBaseline(message);
		}

		private static IEnumerator AutoReconnectCoroutine()
		{
			if (_autoReconnecting) yield break;
			_autoReconnecting = true;
			_reconnectAttempt++;

			float delay = Mathf.Min(RECONNECT_BASE_DELAY * Mathf.Pow(2, _reconnectAttempt - 1), 30f);
			DebugConsole.Log($"[GameClient] Auto-reconnect attempt {_reconnectAttempt}/{MAX_RECONNECT_ATTEMPTS} in {delay}s");
			MultiplayerOverlay.Show($"Reconnecting... attempt {_reconnectAttempt}/{MAX_RECONNECT_ATTEMPTS}");

			yield return new WaitForSecondsRealtime(delay);

			if (!Utils.IsInGame())
			{
				DebugConsole.Log("[GameClient] No longer in game, aborting reconnect");
				_autoReconnecting = false;
				_reconnectAttempt = 0;
				yield break;
			}

			bool started = false;
			try
			{
				started = TryReconnectToSession();
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[GameClient] Reconnect attempt {_reconnectAttempt} failed: {ex}");
			}

			_autoReconnecting = false;
			if (started)
				yield break;
			if (ShouldRetryReconnectStartFailure(Utils.IsInGame(), _reconnectAttempt))
			{
				CoroutineRunner.RunOne(AutoReconnectCoroutine());
				yield break;
			}
			CoroutineRunner.RunOne(ShowMessageAndReturnToTitle(
				"Reconnect failed", "The transport could not start a new connection."));
		}

		public static void ResetReconnectState()
		{
			_autoReconnecting = false;
			_reconnectAttempt = 0;
		}

		private static IEnumerator ShowMessageAndReturnToTitle(string reason = "", string message = "")
		{
			// Auto-reconnect if still in game and under max attempts
			if (ShouldRetryConnection(Utils.IsInGame(), State, _reconnectAttempt))
			{
				CoroutineRunner.RunOne(AutoReconnectCoroutine());
				yield break;
			}

			// Reset on final failure
			EndWorldLoadReconnect();
			_reconnectAttempt = 0;
			_autoReconnecting = false;

			MultiplayerOverlay.Show(string.IsNullOrEmpty(message) ? reason : reason + "\n" + message);
			//SaveHelper.CaptureWorldSnapshot();
			yield return new WaitForSecondsRealtime(3f);
			//PauseScreen.TriggerQuitGame(); // Force exit to frontend, getting a crash here
			if (Utils.IsInGame())
			{
				Utils.ForceQuitGame();
			}
			App.LoadScene("frontend");

			MultiplayerOverlay.Close();
			NetworkIdentityRegistry.Clear();
			NetworkConfig.Stop();
		}

		public static void CacheCurrentServer()
		{
			using var _ = Profiler.Scope();

			if(NetworkConfig.IsSteamConfig())
			{
                if (MultiplayerSession.HostUserID != Utils.NilUlong())
                {
                    _cachedConnectionInfo = new CachedConnectionInfo(
                            MultiplayerSession.HostUserID
                    );
                }
            }
			else if(NetworkConfig.IsLanConfig())
			{
				_cachedConnectionInfo = new CachedConnectionInfo(
                    MultiplayerSession.ServerIp,
                    MultiplayerSession.ServerPort
                );
            }
		}

		public static void ReconnectFromCache()
		{
			using var _ = Profiler.Scope();

			if (_cachedConnectionInfo.HasValue)
			{
				if(NetworkConfig.IsSteamConfig())
				{
                    DebugConsole.Log($"[GameClient] Reconnecting to cached server: {_cachedConnectionInfo.Value.HostSteamID}");
                    var hostId = _cachedConnectionInfo.Value.HostSteamID;
                    _cachedConnectionInfo = null; // Clear cache to prevent re-triggering
                    MultiplayerSession.HostUserID = hostId;
                    ConnectToHost(false);
                }
				else if(NetworkConfig.IsLanConfig())
				{
					DebugConsole.Log($"[GameClient] Reconnecting to cached server: {_cachedConnectionInfo.Value.ServerIp}:{_cachedConnectionInfo.Value.ServerPort}");
                    var ip = _cachedConnectionInfo.Value.ServerIp;
                    var port = _cachedConnectionInfo.Value.ServerPort;
                    _cachedConnectionInfo = null; // Clear cache to prevent re-triggering
                    ConnectToHost(false, ip, port);
                }
			}
		}
	}
}
