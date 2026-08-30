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
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together
{
    public class DiscordRichPresence : MonoBehaviour
    {
        private const string APP_ID = "1511834433542688870";
        private const string LARGE_IMAGE_KEY = "oni_together_logo";
        private const string LARGE_IMAGE_TEXT = "ONI Together";
        private const float PRESENCE_UPDATE_INTERVAL = 5f;

        private DiscordRpcClient _client;
        private System.DateTime _sessionStartTime = System.DateTime.UtcNow;
        private bool _hasRecordedStartTime;
        private float _presenceUpdateTimer;
        
        private ClusterIcons.Entry? _cachedAstroidData;
        private bool _hasCachedAstroidData;

        private void Start()
        {
            try
            {
                _client = new DiscordRpcClient(APP_ID);
                _client.OnReady += OnDiscordReady;
                _client.OnError += OnDiscordError;
                _client.OnJoin += OnDiscordJoin;

                try
                {
                    _client.RegisterUriScheme("457140");
                }
                catch (Exception ex)
                {
                    DebugConsole.LogWarning($"[DiscordRichPresence] URI scheme registration failed: {ex.Message}");
                }

                _client.Initialize();

                DebugConsole.Log("[DiscordRichPresence] Initialized");
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[DiscordRichPresence] Failed to initialize: {ex.Message}");
                enabled = false;
                return;
            }

            if (Game.Instance != null)
            {
                _hasRecordedStartTime = true;
                Game.Instance.OnSpawnComplete += RefreshAstroid;
                Game.Instance.Subscribe((int) GameHashes.ActiveWorldChanged, OnActiveWorldChanged);
            }

            App.OnPostLoadScene += OnSceneLoaded;
        }

        private void OnSceneLoaded()
        {
            if (Game.Instance != null && !_hasRecordedStartTime)
            {
                _sessionStartTime = System.DateTime.UtcNow;
                _hasRecordedStartTime = true;
            }

            _hasCachedAstroidData = false;
        }

        private void RefreshAstroid()
        {
            _hasCachedAstroidData = false;
        }

        private void OnActiveWorldChanged(object data)
        {
            RefreshAstroid();
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
            using var _ = Profiler.Scope();
            
            if (_client == null || !_client.IsInitialized)
                return;

            if (!Configuration.Instance.UseDiscordRichPresence)
            {
                _client.ClearPresence();
                return;
            }

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

            if (Utils.IsInGame())
            {
                var entry = GetAstroidData();
                if (entry.HasValue)
                {
                    presence.Assets.SmallImageKey = entry.Value.IconUrl;
                    presence.Assets.SmallImageText = entry.Value.Name;
                }
            }

            if (MultiplayerSession.InActiveSession)
            {
                int cycle = GameClock.Instance != null ? (GameClock.Instance.GetCycle() + 1) : 1;
                int dupeCount = global::Components.LiveMinionIdentities?.Count ?? 0;
                string baseName = SaveGame.Instance?.BaseName ?? "";
                presence.Details = string.IsNullOrEmpty(baseName)
                    ? $"Cycle {cycle} with {dupeCount} dupes"
                    : $"{baseName} — Cycle {cycle} with {dupeCount} dupes";

                string role = MultiplayerSession.IsHost ? "Hosting" : "Playing";
                string transport = NetworkConfig.IsSteamConfig() ? "Steam" : "LAN";
                presence.State = $"{role} over {transport}";

                string party_id = "oni_together_session";
                bool isPublic = false;
                
                if (_client.HasRegisteredUriScheme && NetworkConfig.IsSteamConfig() && SteamLobby.InLobby)
                {
                    party_id = $"{SteamLobby.CurrentLobby.m_SteamID}";
                    string visibility = SteamMatchmaking.GetLobbyData(SteamLobby.CurrentLobby, "visibility");
                    isPublic = visibility != "private";
                    if (isPublic)
                    {
                        presence.Secrets = new DiscordRPC.Secrets
                        {
                            JoinSecret = $"{SteamLobby.CurrentLobbyCode}"
                        };
                    }
                }

                presence.Party = new DiscordRPC.Party
                {
                    ID = party_id,
                    Size = NetworkConfig.GetConnectedClients().Count,
                    Max = NetworkConfig.GetMaxServerCapacity(),
                    Privacy = isPublic ? Party.PrivacySetting.Public : Party.PrivacySetting.Private,
                };
            }
            else
            {
                if (Utils.IsInMenu())
                {
                    presence.Details = "At main menu";
                }
                else if(Utils.IsInGame())
                {
                    int cycle = GameClock.Instance != null ? (GameClock.Instance.GetCycle() + 1) : 1;
                    int dupeCount = global::Components.LiveMinionIdentities?.Count ?? 0;
                    string baseName = SaveGame.Instance?.BaseName ?? "";
                    presence.Details = string.IsNullOrEmpty(baseName)
                        ? $"Cycle {cycle} with {dupeCount} dupes"
                        : $"{baseName} — Cycle {cycle} with {dupeCount} dupes";
                    presence.State = "Playing singleplayer";
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

            if (msg.Secret != null)
            {
                if (LobbyCodeHelper.TryParseCode(msg.Secret, out ulong lobbyId))
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
                if (_client.IsInitialized)
                    _client.ClearPresence();
                _client.Dispose();
                _client = null;
            }

            if (Game.Instance != null)
            {
                Game.Instance.OnSpawnComplete -= RefreshAstroid;
                Game.Instance.Unsubscribe((int) GameHashes.ActiveWorldChanged, OnActiveWorldChanged);
            }

            App.OnPostLoadScene -= OnSceneLoaded;
        }

        public ClusterIcons.Entry? GetAstroidData()
        {
            using var _ = Profiler.Scope();

            if (!_hasCachedAstroidData)
            {
                _cachedAstroidData = ClusterIcons.ResolveCurrent();
                _hasCachedAstroidData = true;
            }

            return _cachedAstroidData;
        }
    }
}
