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
using System.Collections.Generic;
using System.Linq;
using ProcGen;
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
        
        private AstroidData _cachedAstroidData;
        private bool _hasCachedAstroidData;

        public static Dictionary<string, string> clusterWorldNames = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> SpacedOutWorldLinks = new Dictionary<string, string>()
        {
            ["Terrania"] = "Terrania_Asteroid.png",
            ["Folia"] = "Folia_Asteroid.png",
            ["Quagmiris"] = "Quagmiris_Asteroid.png",
            ["Terra"] = "Terra_Asteroid_(Spaced_Out).png",
            ["Verdante"] = "Verdante_Asteroid_(Spaced_Out).png",
            ["Squelchy"] = "Squelchy_Asteroid.png",
            ["Rime"] = "Rime_Asteroid_(Spaced_Out).png",
            ["Oceania"] = "Oceania_Asteroid_(Spaced_Out).png",
            ["Oasisse"] = "Oasisse_Asteroid_(Spaced_Out).png",
            ["The Badlands"] = "The_Badlands_Asteroid_(Spaced_Out).png",
            ["Arboria"] = "Arboria_Asteroid_(Spaced_Out).png",
            ["Aridio"] = "Aridio_Asteroid_(Spaced_Out).png",
            ["Volcanea"] = "Volcanea_Asteroid_(Spaced_Out).png",
            ["Ceres"] = "Ceres_Asteroid_(Spaced_Out).png",
            ["Blasted Ceres"] = "Blasted_Ceres_Asteroid_(Spaced_Out).png",
            ["Ceres Mantle"] = "Ceres_Mantle_Asteroid.png",
            ["Ceres Minor Cluster"] = "Ceres_Minor_Asteroid.png",
            ["Relica"] = "Relica_Asteroid_(Spaced_Out).png",
            ["Relica Minor"] = "Relica_Minor_Asteroid.png",
            ["Marinea"] = "Marinea_Asteroid_(Spaced_Out).png",
            ["Marinea Minor"] = "Marinea_Minor_Asteroid.png",
            ["RelicAAAA"] = "RelicAAAAAAAGHH_Asteroid.png",
        };

        private static readonly Dictionary<string, string> SpacedOutContainsLinks = new Dictionary<string, string>()
        {
            ["Metallic Swampy"] = "Metallic_Swampy_Asteroid.png",
            ["Frozen Forest"] = "Frozen_Forest_Asteroid.png",
            ["The Desolands"] = "The_Desolands_Asteroid.png",
            ["Moonlet"] = "The_Desolands_Asteroid.png",
            ["Flipped"] = "Flipped_Asteroid.png",
            ["Radioactive Ocean"] = "Radioactive_Ocean_Asteroid.png",
        };

        public struct AstroidData
        {
            public string worldNameFriendly;
            public string worldNameLink;
            public string imgUrl;
        }

        private void Start()
        {
            try
            {
                _client = new DiscordRpcClient(APP_ID);
                _client.OnReady += OnDiscordReady;
                _client.OnError += OnDiscordError;
                _client.OnJoin += OnDiscordJoin;
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
                Game.Instance.Subscribe(1983128072, OnActiveWorldChanged);
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
                AstroidData astroid_data = GetAstroidData();
                presence.Assets.SmallImageKey = astroid_data.imgUrl;
                presence.Assets.SmallImageText = astroid_data.worldNameFriendly;
                DebugConsole.Log("[DiscordRichPresence] Astroid url: " + astroid_data.imgUrl);
                DebugConsole.Log("[DiscordRichPresence] Astroid friendly name: " + astroid_data.worldNameFriendly);
            }

            if (MultiplayerSession.InSession)
            {
                int cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() : 0;
                int dupeCount = global::Components.LiveMinionIdentities?.Count ?? 0;
                string baseName = SaveGame.Instance?.BaseName ?? "";
                presence.Details = string.IsNullOrEmpty(baseName)
                    ? $"Cycle {cycle} with {dupeCount} dupes"
                    : $"{baseName} — Cycle {cycle} with {dupeCount} dupes";

                string role = MultiplayerSession.IsHost ? "Hosting" : "Playing";
                string transport = NetworkConfig.IsSteamConfig() ? "Steam" : "LAN";
                presence.State = $"{role} over {transport}";

                presence.Party = new DiscordRPC.Party
                {
                    ID = "oni_together_session",
                    Size = NetworkConfig.GetConnectedClients().Count,
                    Max = NetworkConfig.GetMaxServerCapacity()
                };

                if (MultiplayerSession.IsHost && NetworkConfig.IsSteamConfig() && SteamLobby.InLobby)
                {
                    string visibility = SteamMatchmaking.GetLobbyData(SteamLobby.CurrentLobby, "visibility");
                    if (visibility != "private")
                    {
                        presence.Secrets = new DiscordRPC.Secrets
                        {
                            JoinSecret = $"{SteamLobby.CurrentLobby.m_SteamID}"
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
                    int cycle = GameClock.Instance != null ? GameClock.Instance.GetCycle() : 0;
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
                if (ulong.TryParse(msg.Secret, out ulong lobbyId))
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

            if (Game.Instance != null)
            {
                Game.Instance.OnSpawnComplete -= RefreshAstroid;
                Game.Instance.Unsubscribe(1983128072, OnActiveWorldChanged);
            }

            App.OnPostLoadScene -= OnSceneLoaded;
        }

        private static string GetSpacedOutLink(string worldName = "Astroid.png")
        {
            DebugConsole.Log($"Detected world name: {worldName} (SpacedOut)");

            foreach (var (prefix, fileName) in SpacedOutWorldLinks.OrderByDescending(x => x.Key.Length))
            {
                if (worldName.StartsWith(prefix, StringComparison.Ordinal))
                    return $"https://oxygennotincluded.wiki.gg/images/{fileName}";
            }

            foreach (var (text, fileName) in SpacedOutContainsLinks)
            {
                if (worldName.Contains(text, StringComparison.Ordinal))
                    return $"https://oxygennotincluded.wiki.gg/images/{fileName}";
            }

            return $"https://oxygennotincluded.wiki.gg/images/{worldName}";
        }

        public AstroidData GetAstroidData()
        {
            if (_hasCachedAstroidData)
                return _cachedAstroidData;

            Klei.CustomSettings.SettingLevel currentQualitySetting = CustomGameSettings.Instance.GetCurrentQualitySetting(Klei.CustomSettings.CustomGameSettingConfigs.ClusterLayout);
            ClusterLayout clusterLayout;
            SettingsCache.clusterLayouts.clusterCache.TryGetValue(currentQualitySetting.id, out clusterLayout);

            var world = ClusterManager.Instance?.activeWorld;
            string worldNameFriendly = Strings.Get(clusterLayout.name);
            string worldNameLink = clusterLayout.name;

            if (clusterWorldNames.TryGetValue(worldNameLink, out string resolvedName))
            {
                worldNameLink = resolvedName;
            }
            else
            {
                worldNameLink = "Asteroid.png";
            }

            worldNameLink = worldNameLink.Replace("<sup>", "").Replace("</sup>", "").Replace(" ", "_");
            string url = "https://oxygennotincluded.wiki.gg/images/Asteroid.png";

            if (!DlcManager.FeatureClusterSpaceEnabled())
            {
                url = $"https://oxygennotincluded.wiki.gg/images/{worldNameLink}_Asteroid.png";
            }
            else
            {
                url = GetSpacedOutLink(worldNameLink);
            }

            _cachedAstroidData = new AstroidData
            {
                worldNameFriendly = worldNameFriendly,
                worldNameLink = worldNameLink,
                imgUrl = url
            };
            _hasCachedAstroidData = true;

            return _cachedAstroidData;
        }
    }
}
