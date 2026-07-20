using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.Misc;
using ONI_Together.Networking.Transport;
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
            RIPTIDE = 1,
        }
        public static NetworkTransport transport { get; private set; } = NetworkTransport.RIPTIDE;

        public static TransportServer TransportServer { get; set; } = new RiptideServer();
        public static TransportClient TransportClient { get; set; } = new RiptideClient();
        public static TransportPacketSender TransportPacketSender { get; set; } = new RiptidePacketSender();

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
                case NetworkTransport.RIPTIDE:
                    UpdateTransport(NetworkTransport.RIPTIDE);
                    CoroutineRunner.RunOne(StartRawDelayed(1f)); // Wait 1 second (prevents timeouts when hosting after loading... doesn't work on crap hardware)
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
                DebugConsole.LogError($"Failed to start LAN game server: {ex.Message}");
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
            switch(transport)
            {
                case NetworkTransport.STEAMWORKS:
                    StopSteamworks();
                    break;
                case NetworkTransport.RIPTIDE:
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
            if (transport.Equals(newTransport))
                return;
            
            transport = newTransport;
            TransportServer = GetTransportServer();
            TransportClient = GetTransportClient();
            TransportPacketSender = GetTransportPacketSender();
            DebugConsole.Log($"Updated network transport to: {newTransport.ToString()}");
        }

        public static TransportServer GetTransportServer()
        {
            using var _ = Profiler.Scope();

            switch (transport)
            {
                case NetworkTransport.STEAMWORKS:
                    return new SteamServer();
                case NetworkTransport.RIPTIDE:
                    return new RiptideServer();
                default:
                    return new RiptideServer(); // Use riptide by default now
            }
        }

        public static TransportClient GetTransportClient()
        {
            using var _ = Profiler.Scope();

            switch (transport)
            {
                case NetworkTransport.STEAMWORKS:
                    return new SteamClient();
                case NetworkTransport.RIPTIDE:
                    return new RiptideClient();
                default:
                    return new RiptideClient(); // Use riptide by default now
            }
        }

        public static TransportPacketSender GetTransportPacketSender()
        {
            using var _ = Profiler.Scope();

            switch (transport)
            {
                case NetworkTransport.STEAMWORKS:
                    return new SteamworksPacketSender();
                case NetworkTransport.RIPTIDE:
                    return new RiptidePacketSender();
                default:
                    return new RiptidePacketSender(); // Use riptide by default now
            }
        }

        public static ulong GetLocalID()
        {
            using var _ = Profiler.Scope();

            switch (transport)
            {
                case NetworkTransport.STEAMWORKS:
                    return SteamUser.GetSteamID().m_SteamID;
                case NetworkTransport.RIPTIDE:
                    if (MultiplayerSession.IsClient)
                    {
                        return RiptideClient.CLIENT_ID;
                    }
                    else
                    {
                        return RiptideServer.CLIENT_ID;
                    }
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

            return transport.Equals(NetworkTransport.RIPTIDE);
        }

        public static int GetMaxServerCapacity()
        {
            switch (transport)
            {
                case NetworkTransport.STEAMWORKS:
                    if (SteamLobby.InLobby)
                        return SteamMatchmaking.GetLobbyMemberLimit(SteamLobby.CurrentLobby);
                    break;
                case NetworkTransport.RIPTIDE:
                    if (RiptideServer.ServerInstance != null)
                        return RiptideServer.ServerInstance.MaxClientCount;
                    if (RiptideClient.MaxServerCapacity > 0)
                        return RiptideClient.MaxServerCapacity;
                    break;
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
                case NetworkTransport.RIPTIDE:
                    if (MultiplayerSession.IsClient)
                    {
                        RiptideClient client = TransportClient as RiptideClient;
                        return client.ClientList;
                    }
                    else
                    {
                        RiptideServer server = TransportServer as RiptideServer;
                        return server.ClientList;
                    }
            }
            return clients;
        }
    }
}

