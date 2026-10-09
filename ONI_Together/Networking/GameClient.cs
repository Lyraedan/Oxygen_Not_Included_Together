using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using ONI_Together.Networking.States;
using ONI_Together.Patches.ToolPatches;
using Shared;
using Shared.Helpers;
using Steamworks;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static class GameClient
	{

		private static ClientState _state = ClientState.Disconnected;
		public static ClientState State => _state;

		private static bool _cancellingConnectionAttempt = false;

		public static bool IsHardSyncInProgress => _state == ClientState.LoadingWorld;
		public static TransitionResult Handle(ClientEvent evt)
		{
			using var _ = Profiler.Scope();

			ClientState nextState;
			switch (evt)
			{
				case ClientEvent.BeginConnect:
					if (_state == ClientState.Disconnected)
						nextState = ClientState.Connecting;
					else
						return RejectTransition(evt, $"Cannot begin connecting while in {_state} state.");
					break;
				case ClientEvent.ConnectionFailed:
					if (_state == ClientState.Disconnected)
						return ApplyStateTransition(_state, evt);
					if (_state == ClientState.Connecting)
						nextState = ClientState.Disconnected;
					else
						return RejectTransition(evt, $"Cannot fail a connection attempt while in {_state} state.");
					break;
				case ClientEvent.CancelConnect:
					if (_state == ClientState.Disconnected)
						return ApplyStateTransition(_state, evt);
					if (_state == ClientState.Connecting)
						nextState = ClientState.Disconnected;
					else
						return RejectTransition(evt, $"Cannot cancel a connection attempt while in {_state} state.");
					break;
				case ClientEvent.TransportConnected:
					if (_state == ClientState.Connecting)
						nextState = ClientState.Connected;
					else
						return RejectTransition(evt, $"Cannot complete transport connection while in {_state} state.");
					break;
				case ClientEvent.WorldLoadStarted:
					if (_state == ClientState.LoadingWorld)
						return ApplyStateTransition(_state, evt);
					if (_state == ClientState.Connected || _state == ClientState.InGame)
						nextState = ClientState.LoadingWorld;
					else
						return RejectTransition(evt, $"Cannot start a world load while in {_state} state.");
					break;
				case ClientEvent.SynchronizationCompleted:
					if (_state == ClientState.LoadingWorld)
						nextState = ClientState.InGame;
					else
						return RejectTransition(evt, $"Cannot complete synchronization while in {_state} state.");
					break;
				case ClientEvent.TransportDisconnected:
					if (_state == ClientState.Disconnected)
						return ApplyStateTransition(_state, evt);
					if (_state == ClientState.Connecting || _state == ClientState.Connected ||
						_state == ClientState.LoadingWorld || _state == ClientState.InGame)
						nextState = ClientState.Disconnected;
					else
						return RejectTransition(evt, $"Cannot disconnect while in {_state} state.");
					break;
				default:
					return RejectTransition(evt, $"Unknown client event {evt}.");
			}

			return ApplyStateTransition(nextState, evt);
		}

		private static TransitionResult ApplyStateTransition(ClientState nextState, ClientEvent evt)
		{
			if (_state != nextState)
			{
				ClientState previousState = _state;
				_state = nextState;
				DebugConsole.Log($"[GameClientLifecycle] {previousState} -> {_state} via {evt}");
			}

			return TransitionResult.Accepted();
		}

		private static TransitionResult RejectTransition(ClientEvent evt, string reason)
		{
			DebugConsole.LogWarning($"[GameClientLifecycle] Rejected {evt} while in {_state}. Reason: {reason}");
			return TransitionResult.Rejected(reason);
		}

		public static void Init()
		{
			using var _ = Profiler.Scope();

			// I fucking hate this, maybe replace this with hashes?
			NetworkConfig.TransportClient.OnClientDisconnected = () =>
			{
				if (_cancellingConnectionAttempt)
					return;

				ClientState previousState = _state;
				Handle(previousState == ClientState.Connecting ? ClientEvent.ConnectionFailed : ClientEvent.TransportDisconnected);
			};
			NetworkConfig.TransportClient.OnConnectionFailed = () =>
			{
				if (!_cancellingConnectionAttempt)
					Handle(ClientEvent.ConnectionFailed);
			};
			NetworkConfig.TransportClient.OnClientConnected = () =>
			{
				if (!_cancellingConnectionAttempt)
					Handle(ClientEvent.TransportConnected);
			};
			NetworkConfig.TransportClient.OnContinueConnectionFlow = () => ContinueConnectionFlow();
			NetworkConfig.TransportClient.OnReturnToMenu = (reason, message) =>
			{
				CoroutineRunner.RunOne(ShowMessageAndReturnToTitle(reason, message));
			};
			NetworkConfig.TransportClient.OnRequestStateOrReturn = () =>
			{
                PacketSender.SendToHost(GameStateRequestPacket.CreateClientRequest(MultiplayerSession.LocalUserID));
                MP_Timer.Instance.StartDelayedAction(10, () => CoroutineRunner.RunOne(ShowMessageAndReturnToTitle()));
            };
            NetworkConfig.TransportClient.Prepare();
            CursorManager.Instance.AssignColor();
        }

		public static void ConnectToHost(bool showLoadingScreen = true, string ip = "", int port = 7777)
		{
			using var _ = Profiler.Scope();

            Init();

			if (showLoadingScreen)
			{
				string hostName = "uknown host";
				if (NetworkConfig.IsSteamConfig())
				{
					hostName = SteamFriends.GetFriendPersonaName(MultiplayerSession.HostUserID.AsCSteamID());
                }
				else if (NetworkConfig.IsLanConfig())
				{
					hostName = $"{ip}:{port}";
                }
					MultiplayerOverlay.Show(string.Format(STRINGS.UI.MP_OVERLAY.CLIENT.CONNECTING_TO_HOST, hostName));
			}

			TransitionResult transition = Handle(ClientEvent.BeginConnect);
			if (!transition.Success)
				return;

			try
			{
				NetworkConfig.TransportClient.ConnectToHost(ip, port);
			}
			catch
			{
				Handle(ClientEvent.ConnectionFailed);
				throw;
			}
		}

		public static void Disconnect()
		{
			using var _ = Profiler.Scope();

			NetworkConfig.TransportClient.Disconnect();
		}

		public static void CancelConnectionAttempt()
		{
			using var _ = Profiler.Scope();

			if (_state != ClientState.Connecting)
				return;

			_cancellingConnectionAttempt = true;
			try
			{
				NetworkConfig.TransportClient.Disconnect();
			}
			finally
			{
				_cancellingConnectionAttempt = false;

				TransitionResult transition = Handle(ClientEvent.CancelConnect);
				if (!transition.Success)
					DebugConsole.LogError($"[GameClient] Failed to complete connection cancellation: {transition.Reason}");

				MultiplayerOverlay.Close();
			}
		}

		public static void ReconnectToSession()
		{
			using var _ = Profiler.Scope();

			NetworkConfig.TransportClient.ReconnectToSession();
		}

		public static void Poll()
		{
			using var _ = Profiler.Scope();

			NetworkConfig.TransportClient.Update();

			switch (State)
			{
				case ClientState.Connected:
				case ClientState.LoadingWorld:
				case ClientState.InGame:
					NetworkConfig.TransportClient.OnMessageRecieved();
					break;
				case ClientState.Connecting:
				case ClientState.Disconnected:
				default:
					break;
			}
		}

		public static void OnHostResponseReceived(GameStateRequestPacket packet)
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log("Gamestate packet received");
			MP_Timer.Instance.Abort();
			if (!TryValidateHostProtocol(packet, out string protocolReason, out string protocolMessage))
			{
				DebugConsole.LogWarning($"[GameClient] Host protocol validation failed: {protocolReason} | {protocolMessage}");
				Disconnect();
				NetworkConfig.TransportClient.OnReturnToMenu.Invoke(protocolReason, protocolMessage);
				return;
			}

			if (MultiplayerSession.GetPlayer(MultiplayerSession.HostUserID) is MultiplayerPlayer host)
			{
				host.ProtocolVerified = true;
			}

			if (!SaveHelper.SavegameDlcListValid(packet.ActiveDlcIds, out var errorMsg))
			{
				DebugConsole.Log("invalid dlc config detected");
				SaveHelper.ShowMessageAndReturnToMainMenu(errorMsg);
				return;
			}

			if (!SaveHelper.SteamModListSynced(packet.ActiveModIds, out var notEnabled, out var notDisabled, out var missingMods))
			{
				string text = STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.TEXT + "\n\n";
				if (notEnabled.Any())
					text += string.Format(STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.TOENABLE, notEnabled.Count) +"\n";
				if (notDisabled.Any())
					text += string.Format(STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.TODISABLE, notDisabled.Count) + "\n";
				if (missingMods.Any())
					text += string.Format(STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.MISSING, missingMods.Count) + "\n";

				// Ignore this if we're in game already
				if (Utils.IsInMenu())
				{
					DialogUtil.CreateConfirmDialogFrontend(STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.TITLE, text,
		   STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.CONFIRM_SYNC,
					() => { SaveHelper.SyncModsAndRestart(notEnabled, notDisabled, missingMods); },
					STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.CANCEL,
					BackToMainMenu,
					STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.DENY_SYNC,
					ContinueConnectionFlow);
					DebugConsole.Log("mods not synced!");
				}
				return;
			}

			ContinueConnectionFlow();
		}

		private static bool TryValidateHostProtocol(GameStateRequestPacket packet, out string reason, out string message)
		{
			using var _ = Profiler.Scope();

			if(Configuration.Instance.BypassProtocolCompatibilityChecks)
			{
				reason = string.Empty;
				message = string.Empty;
				return true;
			}

			reason = STRINGS.UI.PROTOCOL.VALIDATION.TITLE;
			message = string.Empty;

			if (!packet.HasProtocolMetadata)
			{
				message = STRINGS.UI.PROTOCOL.VALIDATION.NO_METADATA;
				return false;
			}

			if (!packet.ProtocolAccepted)
			{
				message = string.IsNullOrEmpty(packet.ProtocolFailureReason)
					? STRINGS.UI.PROTOCOL.VALIDATION.REJECTED
					: packet.ProtocolFailureReason;
				return false;
			}

			if (packet.ProtocolVersion != ProtocolCompatibility.CurrentProtocolVersion)
			{
				message = string.Format(STRINGS.UI.PROTOCOL.VALIDATION.PROTOCOL_MISMATCH, packet.ProtocolVersion, ProtocolCompatibility.CurrentProtocolVersion);
				return false;
			}

			if (packet.PacketRegistryFingerprint != ProtocolCompatibility.PacketFingerprint)
			{
				message = string.Format(STRINGS.UI.PROTOCOL.VALIDATION.FINGERPRINT_MISMATCH, packet.PacketRegistryFingerprint, ProtocolCompatibility.PacketFingerprint);
				return false;
			}

			return true;
		}
		static void BackToMainMenu()
		{
			using var _ = Profiler.Scope();

			MultiplayerOverlay.Close();
			NetworkIdentityRegistry.Clear();
			NetworkConfig.Stop();
			App.LoadScene("frontend");
		}

		private static void ContinueConnectionFlow()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
			{
				DebugConsole.Log("[GameClient] ContinueConnectionFlow called on host - ignoring");
				return;
			}

			DebugConsole.Log($"[GameClient] ContinueConnectionFlow - IsInMenu: {Utils.IsInMenu()}, IsInGame: {Utils.IsInGame()}, HardSyncInProgress: {IsHardSyncInProgress}");
			ReadyManager.SendReadyStatusPacket(ClientReadyState.Unready);

			if (Utils.IsInMenu())
			{
				DebugConsole.Log("[GameClient] Client is in menu - requesting save file or sending ready status");
				MultiplayerOverlay.Show(string.Format(STRINGS.UI.MP_OVERLAY.CLIENT.WAITING_FOR_PLAYER, SteamFriends.GetFriendPersonaName(MultiplayerSession.HostUserID.AsCSteamID())));
				DebugConsole.Log("[GameClient] Requesting synchronized save file from host");
				PacketSender.SendToHost(new SaveFileRequestPacket { Requester = MultiplayerSession.LocalUserID });
			}
			else if (Utils.IsInGame())
			{
				DebugConsole.Log("[GameClient] Client reconnected while in game; requesting host synchronization");
				PacketSender.SendToHost(new SaveFileRequestPacket { Requester = MultiplayerSession.LocalUserID });
			}
			else
			{
				DebugConsole.LogWarning("[GameClient] Client is neither in menu nor in game - unexpected state");
			}
		}

		public static bool BeginSynchronization()
		{
			using var _ = Profiler.Scope();

			if (_state != ClientState.LoadingWorld)
			{
				TransitionResult transition = Handle(ClientEvent.WorldLoadStarted);
				if (!transition.Success)
					return false;
			}

			SpeedControlScreen.Instance?.Pause(false);
			return true;
		}

		public static void OnWorldSpawnComplete()
		{
			using var _ = Profiler.Scope();

			if (_state != ClientState.LoadingWorld)
				return;

			ReadyManager.SendReadyStatusPacket(ClientReadyState.Ready);
		}

		public static bool CompleteSynchronization()
		{
			using var _ = Profiler.Scope();

			if (_state != ClientState.LoadingWorld)
				return false;

			TransitionResult transition = Handle(ClientEvent.SynchronizationCompleted);
			if (!transition.Success)
			{
				DebugConsole.LogError($"[HardSync] could not enter gameplay after sync completion: {transition.Reason}");
				return false;
			}
			Game.Instance?.Trigger(MP_HASHES.GameClient_OnConnectedInGame);
			MultiplayerSession.CreateConnectedPlayerCursors();
			SelectToolPatch.UpdateColor();
			return true;
		}

		private static IEnumerator ShowMessageAndReturnToTitle(string reason = "", string message = "")
		{
			string displayText;
			if (string.IsNullOrEmpty(reason) && string.IsNullOrEmpty(message))
			{
				displayText = STRINGS.UI.MP_OVERLAY.CLIENT.MENU_LOST_CONNECTION;
			}
			else if (string.IsNullOrEmpty(reason))
			{
				displayText = message;
			}
			else if (string.IsNullOrEmpty(message))
			{
				displayText = reason;
			}
			else
			{
				displayText = $"{reason}\n\n{message}";
			}

			MultiplayerOverlay.Show(displayText);
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
	}
}
