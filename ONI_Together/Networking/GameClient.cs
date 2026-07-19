using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Handshake;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Steamworks;
using ONI_Together.Patches.ToolPatches;
using Shared;
using Shared.Helpers;
using Steamworks;
using System;
using System.Collections;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static partial class GameClient
	{

		private static ClientState _state = ClientState.Disconnected;
		public static ClientState State => _state;
		internal static bool CanSendRuntimeRequests(ClientState state)
			=> state == ClientState.InGame;

		private static bool _pollingPaused = false;

		private static CachedConnectionInfo? _cachedConnectionInfo = null;

		public static bool IsHardSyncInProgress = false;
		private static bool _modVerificationSent = false;
		private static long _awaitingReadyGeneration;

		// Auto-reconnect state
		private static bool _autoReconnecting = false;
		private static int _reconnectAttempt = 0;
		private const int MAX_RECONNECT_ATTEMPTS = 5;
		private const float RECONNECT_BASE_DELAY = 1f;

		internal static bool ShouldRetryConnection(bool isInGame, ClientState state, int attempt)
			=> isInGame
			   && state != ClientState.LoadingWorld
			   && state != ClientState.Error
			   && attempt >= 0
			   && attempt < MAX_RECONNECT_ATTEMPTS;

		internal static bool ShouldRequestSnapshotAfterHandshake(bool isInGame, ulong reconnectToken)
			=> isInGame && reconnectToken == 0;

		internal static bool ShouldRequestWorldBaseline(
			bool isInGame,
			ulong reconnectToken,
			long snapshotGeneration)
			=> isInGame && reconnectToken != 0 && snapshotGeneration > 0;

		internal static bool ShouldCompleteWorldBaseline(
			long appliedGeneration,
			long currentGeneration,
			ulong reconnectToken)
			=> appliedGeneration > 0 && appliedGeneration == currentGeneration && reconnectToken != 0;

		internal static bool ShouldCompleteReadyAcceptance(
			ClientState state,
			long awaitingGeneration,
			long acknowledgedGeneration)
			=> state == ClientState.AwaitingReadyAck
			   && awaitingGeneration > 0
			   && acknowledgedGeneration == awaitingGeneration;

		internal static bool ShouldAcceptHostReconnectDecision(
			ulong localReconnectToken,
			ulong acceptedReconnectToken)
			=> acceptedReconnectToken == 0
			   || localReconnectToken != 0 && acceptedReconnectToken == localReconnectToken;


		private struct CachedConnectionInfo
		{
			public ulong HostSteamID;
			public string ServerIp;
			public int ServerPort;

			public CachedConnectionInfo(ulong id)
			{
				HostSteamID = id;
			}

			public CachedConnectionInfo(string ip, int port)
            {
                ServerIp = ip;
                ServerPort = port;
            }

        }

		/// <summary>
		/// Returns true if we have cached connection info from a previous session
		/// (used to determine if we need to reconnect after world load)
		/// </summary>
		public static bool HasCachedConnection()
		{
			using var _ = Profiler.Scope();

			return _cachedConnectionInfo.HasValue;
		}

		/// <summary>
		/// Clears the cached connection info after successful reconnection or on error
		/// </summary>
		public static void ClearCachedConnection()
		{
			using var _ = Profiler.Scope();

			_cachedConnectionInfo = null;
			MultiplayerSession.ReleaseClientWorldLoad();
		}

		internal static bool BeginWorldLoadReconnect()
		{
			if (!MultiplayerSession.TryRetainClientWorldLoad())
				return false;
			SetState(ClientState.LoadingWorld);
			return true;
		}

		internal static void EndWorldLoadReconnect()
		{
			MultiplayerSession.ReleaseClientWorldLoad();
		}

		internal static bool ShouldTransitionToDisconnected(ClientState state)
			=> state != ClientState.LoadingWorld && state != ClientState.Error;

		public static void SetState(ClientState newState)
		{
			using var _ = Profiler.Scope();

			if (_state != newState)
			{
				_state = newState;
				DebugConsole.Log($"[GameClient] State changed to: {_state}");
			}
		}

		public static void Init()
		{
			using var _ = Profiler.Scope();
			WorldDataPacket.SnapshotApplied -= OnWorldBaselineApplied;
			WorldDataPacket.SnapshotApplied += OnWorldBaselineApplied;

			// I fucking hate this, maybe replace this with hashes?
			NetworkConfig.TransportClient.OnClientDisconnected = () =>
			{
				TcpTransferStartPacket.CancelActiveDownload();
				_awaitingReadyGeneration = 0;
				if (ShouldTransitionToDisconnected(State))
					SetState(ClientState.Disconnected);
			};
			NetworkConfig.TransportClient.OnClientConnected = () => SetState(ClientState.Connected);
			NetworkConfig.TransportClient.OnContinueConnectionFlow = () => ContinueConnectionFlow();
			NetworkConfig.TransportClient.OnReturnToMenu = (reason, message) => CoroutineRunner.RunOne(ShowMessageAndReturnToTitle(reason, message));
			NetworkConfig.TransportClient.OnRequestStateOrReturn = () =>
			{
				bool sent = PacketSender.SendToHost(
					GameStateRequestPacket.CreateClientRequest(MultiplayerSession.LocalUserID));
				DebugConsole.Log(
					$"[GameClient] Initial GameStateRequest sent={sent} " +
					$"sender={NetworkConfig.TransportPacketSender?.GetType().Name ?? "null"}");
                MP_Timer.Instance.StartDelayedAction(10, () => CoroutineRunner.RunOne(ShowMessageAndReturnToTitle()));
            };
            NetworkConfig.TransportClient.Prepare();
            CursorManager.Instance.AssignColor();
        }

		public static void ConnectToHost(bool showLoadingScreen = true, string ip = "", int port = 7777)
		{
			using var _ = Profiler.Scope();

			if (NetworkConfig.IsLanConfig() && !NetworkConfig.IsValidLanPort(port))
			{
				const string reason = "Invalid LAN port";
				const string message = "LAN port must be between 1 and 65534.";
				DebugConsole.LogError($"[GameClient] {message}");
				SetState(ClientState.Error);
				CoroutineRunner.RunOne(ShowMessageAndReturnToTitle(reason, message));
				return;
			}

			if (showLoadingScreen)
			{
				EndWorldLoadReconnect();
				SessionStateReset.Reset();
			}

            Init();

            // Reset mod verification for new connection attempts
            _modVerificationSent = false;

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

			SetState(ClientState.Connecting);
			NetworkConfig.TransportClient.ConnectToHost(ip, port);
		}

		public static void Disconnect()
		{
			using var _ = Profiler.Scope();

			TcpTransferStartPacket.CancelActiveDownload();
			NetworkConfig.TransportClient.Disconnect();
		}

		public static bool TryReconnectToSession()
		{
			using var _ = Profiler.Scope();

			return NetworkConfig.TransportClient.TryReconnectToSession();
		}

		public static void Poll()
		{
			using var _ = Profiler.Scope();

			if (_pollingPaused)
				return;

			NetworkConfig.TransportClient.Update();

			switch (State)
			{
				case ClientState.Connected:
				case ClientState.AwaitingReadyAck:
				case ClientState.InGame:
					NetworkConfig.TransportClient.OnMessageRecieved();
					break;
				case ClientState.Connecting:
				case ClientState.Disconnected:
				case ClientState.Error:
					default:
						break;
				}
			UpdateWorldLoadPhase();
		}

		public static void OnHostResponseReceived(GameStateRequestPacket packet)
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log("Gamestate packet received");
			MP_Timer.Instance.Abort();
			if (!TryValidateHostProtocol(packet, out string protocolReason, out string protocolMessage))
			{
				DebugConsole.LogWarning($"[GameClient] Host protocol validation failed: {protocolReason} | {protocolMessage}");
				FailConnectionValidation(protocolReason, protocolMessage);
				return;
			}
			ulong localReconnectToken = ReadyManager.ReconnectToken;
			if (!ShouldAcceptHostReconnectDecision(localReconnectToken, packet.ReconnectToken))
			{
				const string message = "Host returned an invalid reconnect decision.";
				DebugConsole.LogWarning($"[GameClient] {message}");
				FailConnectionValidation(
					STRINGS.UI.PROTOCOL.VALIDATION.TITLE,
					message);
				return;
			}
			if (packet.ReconnectToken == 0 && localReconnectToken != 0)
				ReadyManager.ClearReconnectProof();

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
					BackToMainMenu);
					DebugConsole.Log("mods not synced!");
				}
				else if (ShouldTerminateConnectionValidation(inMenu: false))
					FailConnectionValidation(STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.TITLE, text);
				return;
			}

			if (!SaveHelper.ActiveModFingerprintsMatch(packet.ActiveModFingerprints, out var missingLocalVersions, out var extraLocalVersions))
			{
				string text = STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.VERSION_TEXT + "\n\n"
					+ string.Format(STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.HOST_ONLY, missingLocalVersions.Count) + "\n"
					+ string.Format(STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.CLIENT_ONLY, extraLocalVersions.Count);
				DebugConsole.LogWarning($"[GameClient] Exact active-mod fingerprint mismatch: host-only={missingLocalVersions.Count}, client-only={extraLocalVersions.Count}");
				if (Utils.IsInMenu())
				{
					DialogUtil.CreateConfirmDialogFrontend(
						STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.TITLE,
						text,
						STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.CANCEL,
						BackToMainMenu);
				}
				else if (ShouldTerminateConnectionValidation(inMenu: false))
					FailConnectionValidation(STRINGS.UI.MP_OVERLAY.SYNC.MODSYNC.TITLE, text);
				return;
			}

			ContinueConnectionFlow();
		}

		private static bool TryValidateHostProtocol(GameStateRequestPacket packet, out string reason, out string message)
		{
			using var _ = Profiler.Scope();

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

			if (!string.Equals(packet.ModVersion, ProtocolCompatibility.ModVersion, StringComparison.Ordinal))
			{
				message = string.Format(STRINGS.UI.PROTOCOL.MOD_VERSION_MISMATCH, packet.ModVersion, ProtocolCompatibility.ModVersion);
				return false;
			}

			if (packet.GameBuild != ProtocolCompatibility.GameBuild)
			{
				message = string.Format(
					STRINGS.UI.PROTOCOL.VALIDATION.GAME_BUILD_MISMATCH,
					packet.GameBuild, ProtocolCompatibility.GameBuild);
				return false;
			}

			if (ProtocolCompatibility.ModBuildFingerprint.Length != 64
			    || !string.Equals(
				    packet.ModBuildFingerprint,
				    ProtocolCompatibility.ModBuildFingerprint,
				    StringComparison.Ordinal))
			{
				message = STRINGS.UI.PROTOCOL.VALIDATION.MOD_BUILD_MISMATCH;
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

			// CRITICAL: Only execute on client, never on server
			if (MultiplayerSession.IsHost)
			{
				DebugConsole.Log("[GameClient] ContinueConnectionFlow called on host - ignoring");
				return;
			}

			DebugConsole.Log($"[GameClient] ContinueConnectionFlow - IsInMenu: {Utils.IsInMenu()}, IsInGame: {Utils.IsInGame()}, HardSyncInProgress: {IsHardSyncInProgress}");

			if (!ReadyManager.SendReadyStatusPacket(ClientReadyState.Unready))
			{
				FailConnectionValidation(
					"Connection failed", "Could not send the initial Ready state to the host.");
				return;
			}

			if (Utils.IsInMenu())
			{
				DebugConsole.Log("[GameClient] Client is in menu - requesting save file or sending ready status");

				// CRITICAL: Enable packet processing BEFORE requesting save file
				// Otherwise, host packets will be discarded!
				PacketHandler.readyToProcess = true;
				DebugConsole.Log("[GameClient] PacketHandler.readyToProcess = true (menu)");

				// Show overlay with localized message
				MultiplayerOverlay.Show(string.Format(STRINGS.UI.MP_OVERLAY.CLIENT.WAITING_FOR_PLAYER, SteamFriends.GetFriendPersonaName(MultiplayerSession.HostUserID.AsCSteamID())));
				if (!IsHardSyncInProgress)
				{
					DebugConsole.Log("[GameClient] Requesting save file from host");
					var packet = new SaveFileRequestPacket
					{
						Requester = MultiplayerSession.LocalUserID
					};
					if (!PacketSender.SendToHost(packet))
						FailConnectionValidation(
							"Connection failed", "Could not request the host save file.");
				}
				else
				{
					DebugConsole.Log("[GameClient] Hard sync in progress, sending ready status");
					// Tell the host we're ready
					if (!ReadyManager.SendReadyStatusPacket(ClientReadyState.Ready))
						FailConnectionValidation(
							"Connection failed", "Could not send the Ready state to the host.");
				}
			}
			else if (Utils.IsInGame())
			{
				DebugConsole.Log("[GameClient] Client is in game - treating as reconnection");

				// CRÍTICO: Habilitar processamento de pacotes
				PacketHandler.readyToProcess = true;
				DebugConsole.Log("[GameClient] PacketHandler.readyToProcess = true");

				if (ShouldRequestSnapshotAfterHandshake(true, ReadyManager.ReconnectToken))
				{
					DebugConsole.Log("[GameClient] Requesting a fresh snapshot for connection recovery");
					if (!PacketSender.SendToHost(new SaveFileRequestPacket
					    {
						    Requester = MultiplayerSession.LocalUserID
					    }))
						FailWorldBaseline("Could not request a fresh host snapshot.");
					return;
				}

				if (!RequestWorldBaseline())
					FailWorldBaseline("Could not request the current world baseline.");
			}
			else
			{
				DebugConsole.LogWarning("[GameClient] Client is neither in menu nor in game - unexpected state");
			}
		}

	}
}
