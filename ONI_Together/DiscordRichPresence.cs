using DiscordRPC;
using DiscordRPC.Message;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Transport.Steamworks;
using ONI_Together.UI;
using Steamworks;
using System;
using System.Collections;
using UnityEngine;

namespace ONI_Together
{
    public class DiscordRichPresence : MonoBehaviour
    {
        private const string APP_ID = "1511834433542688870";
        private const string LARGE_IMAGE_KEY = "oni_together_logo";
        private const string LARGE_IMAGE_TEXT = "ONI Together";
        private const string SMALL_IMAGE_KEY = "network_icon";
        private const float PRESENCE_UPDATE_INTERVAL = 5f;

        private DiscordRpcClient _client;
        private System.DateTime _sessionStartTime = System.DateTime.UtcNow;
        private bool _hasRecordedStartTime;
        private float _presenceUpdateTimer;

        private void Start()
        {
            try
            {
                _client = new DiscordRpcClient(APP_ID);
                _client.OnReady += OnDiscordReady;
                _client.OnError += OnDiscordError;
                _client.OnJoin += OnDiscordJoin;
                _client.Initialize();

                try
                {
                    _client.RegisterUriScheme("457140", null);
                }
                catch { }

                DebugConsole.Log("[DiscordRichPresence] Initialized");
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[DiscordRichPresence] Failed to initialize: {ex.Message}");
                enabled = false;
                return;
            }

            if (Game.Instance != null)
                _hasRecordedStartTime = true;

            App.OnPostLoadScene += OnSceneLoaded;
        }

        private void OnSceneLoaded()
        {
            if (Game.Instance != null && !_hasRecordedStartTime)
            {
                _sessionStartTime = System.DateTime.UtcNow;
                _hasRecordedStartTime = true;
            }
        }

        private void Update()
        {
            if (_client != null)
                _client.Invoke();

            _presenceUpdateTimer += Time.unscaledDeltaTime;
            if (_presenceUpdateTimer >= PRESENCE_UPDATE_INTERVAL)
            {
                _presenceUpdateTimer = 0f;
                UpdatePresence();
            }
        }

        private void UpdatePresence()
        {
            if (_client == null || !_client.IsInitialized)
                return;

            var presence = new RichPresence
            {
                Assets = new DiscordRPC.Assets
                {
                    LargeImageKey = LARGE_IMAGE_KEY,
                    LargeImageText = LARGE_IMAGE_TEXT
                },
                Timestamps = new DiscordRPC.Timestamps
                {
                    Start = (System.DateTime?)_sessionStartTime
                }
            };

            if (MultiplayerSession.InSession)
            {
                presence.Assets.SmallImageKey = SMALL_IMAGE_KEY;

                int cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() : 0;
                int dupeCount = global::Components.LiveMinionIdentities?.Count ?? 0;
                presence.Details = $"Cycle {cycle} with {dupeCount} dupes";

                string role = MultiplayerSession.IsHost ? "Hosting" : "Playing";
                string transport = NetworkConfig.IsSteamConfig() ? "Steam" : "LAN";
                presence.State = $"{role} over {transport}";

                presence.Party = new DiscordRPC.Party
                {
                    ID = "oni_together_session",
                    Size = NetworkConfig.GetConnectedClients().Count,
                    Max = NetworkConfig.GetMaxServerCapacity()
                };

                if (_client.HasRegisteredUriScheme && MultiplayerSession.IsHost && NetworkConfig.IsSteamConfig() && SteamLobby.InLobby)
                {
                    string visibility = SteamMatchmaking.GetLobbyData(SteamLobby.CurrentLobby, "visibility");
                    if (visibility != "private")
                    {
                        presence.Secrets = new DiscordRPC.Secrets
                        {
                            JoinSecret = $"steam_lobby:{SteamLobby.CurrentLobby.m_SteamID}"
                        };
                    }
                }
            }
            else
            {
                if (Utils.IsInMenu())
                {
                    presence.Details = "At main menu";
                }
                else if(Utils.IsInGame())
                {
                    presence.Details = "Playing singleplayer";
                }
            }

            _client.SetPresence(presence);
        }

        private void OnDiscordReady(object sender, ReadyMessage msg)
        {
            DebugConsole.Log($"[DiscordRichPresence] Discord RPC ready for user {msg.User.Username}");
            UpdatePresence();
        }

        private void OnDiscordError(object sender, ErrorMessage msg)
        {
            DebugConsole.LogError($"[DiscordRichPresence] Error: {msg.Message}");
        }

        private void OnDiscordJoin(object sender, JoinMessage msg)
        {
            DebugConsole.Log($"[DiscordRichPresence] Join with secret: {msg.Secret}");

            if (msg.Secret != null && msg.Secret.StartsWith("steam_lobby:"))
            {
                if (ulong.TryParse(msg.Secret.Substring("steam_lobby:".Length), out ulong lobbyId))
                {
                    DebugConsole.Log($"[DiscordRichPresence] Joining Steam lobby: {lobbyId}");
                    NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.STEAMWORKS);
                    StartCoroutine(JoinViaDiscord(lobbyId.AsCSteamID()));
                }
            }
        }

        private System.Collections.IEnumerator JoinViaDiscord(CSteamID lobbyId)
        {
            SteamMatchmaking.RequestLobbyData(lobbyId);
            yield return new WaitForSeconds(0.5f);

            string hasPassword = SteamMatchmaking.GetLobbyData(lobbyId, "has_password");
            if (hasPassword == "1")
            {
                UnityPasswordInputDialogueUI.ShowPasswordDialogueFor(lobbyId.m_SteamID);
            }
            else
            {
                SteamLobby.JoinLobby(lobbyId);
            }
        }

        private void OnDestroy()
        {
            if (_client != null)
            {
                _client.ClearPresence();
                _client.Dispose();
                _client = null;
            }

            App.OnPostLoadScene -= OnSceneLoaded;
        }
    }
}
