using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.Misc;
using ONI_Together.Networking.Transport;
using ONI_Together.Networking.Transport.External;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.Networking.Transport.Steam;
using Steamworks;
using SteamServer = ONI_Together.Networking.Transport.Steam.SteamworksServer;
using SteamClient = ONI_Together.Networking.Transport.Steam.SteamworksClient;
using ONI_Together.Networking.Transport.Steamworks;
using ONI_Together.DebugTools;
using Shared.Profiling;
using ONI_Together.Patches.ToolPatches;
using UnityEngine;
using System.Collections;
using Shared;

namespace ONI_Together.Networking
{
    public static class NetworkConfig
    {
        public class PingRanges
        {
            // Anything less then degraded is considered good
            public static readonly int DEGRADED = 120;
            public static readonly int BAD = 150;
        }

        public enum NetworkTransport
        {
            STEAMWORKS = 0,
            LITENETLIB = 1,
            RIPTIDE = 2,
            CUSTOM = 3
        }
        public static NetworkTransport transport { get; private set; } = NetworkTransport.LITENETLIB;

        /// <summary>Stable id of the active transport ("steamworks", "litenetlib", "riptide", or a custom id).</summary>
        public static string TransportId { get; private set; } = "litenetlib";

        /// <summary>The registered custom provider when <see cref="transport"/> is <see cref="NetworkTransport.CUSTOM"/>.</summary>
        public static ExternalTransportProvider ActiveExternalProvider { get; private set; }

        public static TransportServer TransportServer { get; set; } = new LiteNetLibServer();
        public static TransportClient TransportClient { get; set; } = new LiteNetLibClient();
        public static TransportPacketSender TransportPacketSender { get; set; } = new LiteNetLibPacketSender();

        public static bool IsCustomConfig => transport == NetworkTransport.CUSTOM;

        public static bool IsDirectConfig
        {
            get
            {
                if (transport == NetworkTransport.LITENETLIB || transport == NetworkTransport.RIPTIDE)
                    return true;

                return transport == NetworkTransport.CUSTOM && ActiveExternalProvider?.IsDirect == true;
            }
        }

        public static readonly int LOBBY_SIZE_MIN = 2;
        public static readonly int LOBBY_SIZE_DEFAULT = 4;
        public static readonly int LOBBY_SIZE_MAX = 16;

        /// <summary>
        /// Starts a GameServer on the current transport
        /// </summary>
        public static void StartServer()
        {
            switch(transport)
            {
                case NetworkTransport.STEAMWORKS:
                    UpdateTransport(NetworkTransport.STEAMWORKS);
                    StartSteamServer();
                    break;
                case NetworkTransport.LITENETLIB:
                case NetworkTransport.RIPTIDE:
                    UpdateTransport(transport);
                    CoroutineRunner.RunOne(StartRawDelayed(0.5f));
                    break;
                case NetworkTransport.CUSTOM:
                    if (ActiveExternalProvider == null)
                    {
                        DebugConsole.LogError("[NetworkConfig] Cannot start: no custom transport is prepared.");
                        return;
                    }
                    CoroutineRunner.RunOne(StartRawDelayed(0.5f));
                    break;
            }
        }

        private static void StartSteamServer()
        {
            SteamLobby.CreateLobby(onSuccess: () =>
            {
                //SpeedControlScreen.Instance?.Unpause(false);
                SpeedControlScreen.Instance.Pause(true);
                Game.Instance.Trigger(MP_HASHES.OnMultiplayerGameSessionInitialized);
            });
        }

        private static IEnumerator StartRawDelayed(float delay = 1f)
        {
            yield return new WaitForSecondsRealtime(delay);
            StartRawServer();
        }

        private static void StartRawServer()
        {
            MultiplayerSession.Clear();
            try
            {
                GameServer.Start();
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"Failed to start LAN game server: {ex.Message}\n{ex.StackTrace}");
            }
            SelectToolPatch.UpdateColor();
            Game.Instance.Trigger(MP_HASHES.OnMultiplayerGameSessionInitialized);
            SpeedControlScreen.Instance.Pause(true);
        }

        /// <summary>
        /// Stops the server based off the current transport
        /// </summary>
        public static void Stop()
        {
            GameClient.IsHardSyncInProgress = false;

            switch(transport)
            {
                case NetworkTransport.STEAMWORKS:
                    StopSteamworks();
                    break;
                case NetworkTransport.LITENETLIB:
                case NetworkTransport.RIPTIDE:
                case NetworkTransport.CUSTOM:
                    StopRaw();
                    break;
            }
            Game.Instance?.Trigger(MP_HASHES.OnDisconnected);
        }

        private static void StopSteamworks()
        {
            SteamLobby.LeaveLobby();
        }

        private static void StopRaw()
        {
            if (MultiplayerSession.IsHost)
                GameServer.Shutdown();

            if (MultiplayerSession.IsClient)
                GameClient.Disconnect();

            NetworkIdentityRegistry.Clear();
            MultiplayerSession.Clear();

            SelectToolPatch.UpdateColor();
        }

        public static void UpdateTransport(NetworkTransport newTransport)
        {
            if (newTransport == NetworkTransport.CUSTOM)
            {
                DebugConsole.LogWarning("[NetworkConfig] Use UpdateCustomTransport(id) for custom transports.");
                return;
            }

            if (newTransport == NetworkTransport.RIPTIDE)
            {
                // Legacy enum/config value. Riptide is now a separate transport mod.
                if (TransportRegistry.GetExternalProvider("riptide") != null && UpdateCustomTransport("riptide"))
                    return;

                DebugConsole.LogWarning(
                    "[NetworkConfig] Riptide is now a separate transport mod. Falling back to LiteNetLib.");
                newTransport = NetworkTransport.LITENETLIB;
            }

            if (transport.Equals(newTransport) && ActiveExternalProvider == null)
                return;

            transport = newTransport;
            TransportId = GetTransportId(newTransport);
            ActiveExternalProvider = null;
            TransportServer = GetTransportServer();
            TransportClient = GetTransportClient();
            TransportPacketSender = GetTransportPacketSender();
            DebugConsole.Log($"Updated network transport to: {newTransport.ToString()}");
        }

        public static bool UpdateCustomTransport(string id)
        {
            var provider = TransportRegistry.GetExternalProvider(id);
            if (provider == null)
            {
                DebugConsole.LogError($"[NetworkConfig] Custom transport '{id}' is not registered.");
                return false;
            }

            var server = provider.CreateServer();
            var client = provider.CreateClient();
            if (server == null || client == null)
            {
                DebugConsole.LogError($"[NetworkConfig] Custom transport '{id}' failed to create its endpoints.");
                return false;
            }

            transport = NetworkTransport.CUSTOM;
            TransportId = provider.Id;
            ActiveExternalProvider = provider;
            TransportServer = server;
            TransportClient = client;
            TransportPacketSender = new ExternalTransportPacketSender(server, client);

            DebugConsole.Log($"Updated network transport to custom: {provider.DisplayName} ({provider.Id})");
            return true;
        }

        public static bool PrepareCustomServer(string id, Dictionary<string, string> settings)
        {
            if (!UpdateCustomTransport(id))
                return false;

            if (TransportServer is ExternalTransportServer server)
                server.Settings = settings ?? new Dictionary<string, string>();

            return true;
        }

        public static bool StartCustomServer(string id, Dictionary<string, string> settings)
        {
            if (!PrepareCustomServer(id, settings))
                return false;

            StartServer();
            return true;
        }

        public static bool ConnectCustom(string id, Dictionary<string, string> settings)
        {
            if (!UpdateCustomTransport(id))
                return false;

            if (TransportClient is ExternalTransportClient client)
                client.Settings = settings ?? new Dictionary<string, string>();

            GameClient.ConnectToHost();
            return true;
        }

        public static void UpdateLanTransport()
        {
            var lanTransport = Configuration.Instance.Host.LanSettings.Transport;
            if (lanTransport == LanTransportType.Riptide)
            {
                DebugConsole.LogWarning(
                    "[NetworkConfig] Riptide is now a separate transport mod; select it from the Custom tab.");
                lanTransport = LanTransportType.LiteNetLib;
                Configuration.Instance.Host.LanSettings.Transport = lanTransport;
            }

            UpdateTransport((NetworkTransport)lanTransport);
        }

        private static string GetTransportId(NetworkTransport value)
        {
            return value switch
            {
                NetworkTransport.STEAMWORKS => "steamworks",
                NetworkTransport.LITENETLIB => "litenetlib",
                NetworkTransport.RIPTIDE => "riptide",
                _ => "custom"
            };
        }

        public static TransportServer GetTransportServer()
        {
            using var _ = Profiler.Scope();
            if (transport == NetworkTransport.CUSTOM)
                return TransportServer;
            return TransportRegistry.GetTransport((TransportProtocol)transport).CreateServer();
        }

        public static TransportClient GetTransportClient()
        {
            using var _ = Profiler.Scope();
            if (transport == NetworkTransport.CUSTOM)
                return TransportClient;
            return TransportRegistry.GetTransport((TransportProtocol)transport).CreateClient();
        }

        public static TransportPacketSender GetTransportPacketSender()
        {
            using var _ = Profiler.Scope();
            if (transport == NetworkTransport.CUSTOM)
                return TransportPacketSender;
            return TransportRegistry.GetTransport((TransportProtocol)transport).CreatePacketSender();
        }

        public static ulong GetLocalID()
        {
            using var _ = Profiler.Scope();

            switch (transport)
            {
                case NetworkTransport.STEAMWORKS:
                    return SteamUser.GetSteamID().m_SteamID;
                case NetworkTransport.LITENETLIB:
                    return MultiplayerSession.IsClient ? LiteNetLibClient.CLIENT_ID : LiteNetLibServer.CLIENT_ID;
                case NetworkTransport.CUSTOM:
                    return MultiplayerSession.IsClient
                        ? TransportClient?.LocalPlayerId ?? 0UL
                        : TransportServer?.LocalPlayerId ?? 0UL;
                default:
                    return Utils.NilUlong();
            }
        }

        public static bool IsSteamConfig()
        {
            using var _ = Profiler.Scope();

            return transport.Equals(NetworkTransport.STEAMWORKS);
        }

        public static bool IsLanConfig()
        {
            using var _ = Profiler.Scope();

            // Custom direct transports get the same chunk/name behaviour as LAN.
            return IsDirectConfig;
        }

        public static int GetMaxServerCapacity()
        {
            switch (transport)
            {
                case NetworkTransport.STEAMWORKS:
                    if (SteamLobby.InLobby)
                        return SteamMatchmaking.GetLobbyMemberLimit(SteamLobby.CurrentLobby);
                    break;
                case NetworkTransport.LITENETLIB:
                case NetworkTransport.RIPTIDE:
                case NetworkTransport.CUSTOM:
                    return Configuration.Instance.Host.MaxLobbySize;
            }
            return Configuration.Instance.Host.MaxLobbySize;
        }

        public static List<ulong> GetConnectedClients()
        {
            using var _ = Profiler.Scope();

            List<ulong> clients = new List<ulong>();
            switch(transport)
            {
                case NetworkTransport.STEAMWORKS:
                    List<CSteamID> members = SteamLobby.GetAllLobbyMembers();
                    foreach(CSteamID member in members)
                    {
                        clients.Add(member.m_SteamID);
                    }
                    break;
                case NetworkTransport.LITENETLIB:
                    if (MultiplayerSession.IsClient)
                    {
                        return new List<ulong>(MultiplayerSession.ConnectedPlayers.Keys);
                    }
                    else
                    {
                        LiteNetLibServer server = TransportServer as LiteNetLibServer;
                        return server?.ClientList ?? clients;
                    }
                case NetworkTransport.CUSTOM:
                    if (MultiplayerSession.IsClient)
                        return new List<ulong>(MultiplayerSession.ConnectedPlayers.Keys);

                    return new List<ulong>(TransportServer?.GetConnectedPlayerIds() ?? Array.Empty<ulong>());
            }
            return clients;
        }
    }
}

