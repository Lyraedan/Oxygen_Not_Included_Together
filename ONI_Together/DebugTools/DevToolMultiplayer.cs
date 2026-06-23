// Keep this to only windows, Mac is not built with the Devtool framework so it doesn't have access to the DevTool class and just crashes
#if DEBUG //OS_WINDOWS || DEBUG

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ImGuiNET;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Components;
using UnityEngine;
using static STRINGS.UI;
using ONI_Together.Menus;
using ONI_Together.Misc;
using Shared.Profiling;
using System.Text;
using ONI_Together.Patches.ToolPatches;
using ONI_Together.Tests;
using ONI_Together.Networking.Transport.Lan;
using static STRINGS.BUILDINGS.PREFABS;
using Riptide;
using Steamworks;
using ONI_Together.Networking.Transport.Steamworks;
using ONI_Together.Networking.OxySync;
using ONI_Together.Networking.OxySync.Components;
using Shared.OxySync;
using System.Linq;

namespace ONI_Together.DebugTools
{
    public class DevToolMultiplayer : DevTool
    {
        private Vector2 scrollPos = Vector2.zero;
        DebugConsole console = null;
        PacketTracker packetTracker = null;

        // Player color
        private bool useRandomColor = false;
        private Vector3 playerColor = new Vector3(1f, 1f, 1f);

        // Alert popup
        private bool showRestartPrompt = false;

        // Open player profile
        private ulong? selectedPlayer = null;

        // Network transport
        private int selectedTransportType = 0; // 0 = Steam, 1 = LAN
        private int selectedLanType = 0; // 0 = Riptide, 1 = LiteNetLib
        private string hostIP = "";
        private int hostPort = 7777;
        private string clientIP = "";
        private int clientPort = 7777;
        LanSettings settings_host = new LanSettings();
        LanSettings settings_client = new LanSettings();

        // Unit testing
        private string unitTestSelectedCategory = "All";
        private bool unitTestAutoRun = false;
        private float unitTestAutoRunInterval = 2.0f;
        private float unitTestAutoRunTimer = 0f;

        // Independent popout windows
        private struct PopoutWindow
        {
            public int Id;
            public string Title;
            public System.Action Render;
            public bool Open;
        }
        private List<PopoutWindow> _popoutWindows = new();
        private int _nextPopoutId;

        private static readonly string ModDirectory = Path.Combine(
            Path.GetDirectoryName(typeof(DevToolMultiplayer).Assembly.Location),
            "oni_mp.dll"
        );

        public DevToolMultiplayer()
        {
            using var _ = Profiler.Scope();

            Name = "Multiplayer";
            RequiresGameRunning = false;
            console = DebugConsole.Init();
            packetTracker = PacketTracker.Init();

            ColorRGB loadedColor = Configuration.GetClientProperty<ColorRGB>("PlayerColor");
            playerColor = new Vector3(loadedColor.R / 255, loadedColor.G / 255, loadedColor.B / 255);
            useRandomColor = Configuration.GetClientProperty<bool>("UseRandomPlayerColor");

            OnInit += () => Init();
            OnUpdate += () => Update();
            OnUninit += () => UnInit();

            selectedTransportType = Configuration.Instance.Host.NetworkTransport;
            hostIP = Configuration.Instance.Host.LanSettings.Ip;
            hostPort = Configuration.Instance.Host.LanSettings.Port;
            settings_host.Ip = hostIP;
            settings_host.Port = hostPort;

            clientIP = Configuration.Instance.Client.LanSettings.Ip;
            clientPort = Configuration.Instance.Client.LanSettings.Port;
            settings_client.Ip = clientIP;
            settings_client.Port = clientPort;
        }

        void Init()
        {

        }

        void Update()
        {

        }

        void UnInit()
        {

        }

        public override void RenderTo(DevPanel panel)
        {
            using var _ = Profiler.Scope();

            ImGui.BeginChild("ScrollRegion", new Vector2(0, 0), true);

            if (ImGui.BeginTabBar("MultiplayerTabs"))
            {
                if (ImGui.BeginTabItem("General"))
                {
                    DrawGeneralTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Session"))
                {
                    DrawSessionTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Network"))
                {
                    DrawNetworkTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Debug"))
                {
                    DrawDebugTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Unit Tests"))
                {
                    DrawTestsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Console"))
                {
                    DrawConsoleTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Profiler"))
                {
                    if (ImGui.Button("Open Popout"))
                        OpenPopout("Profiler", () => Profiler.DrawImGuiInline());
                    ImGui.SameLine();
                    DisplayProfilers();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("OxySync"))
                {
                    DrawOxySyncTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.EndChild();

            DrawPopoutWindows();
        }

        private void OpenPopout(string title, System.Action render)
        {
            _popoutWindows.RemoveAll(p => !p.Open);
            _popoutWindows.Add(new PopoutWindow
            {
                Id = _nextPopoutId++,
                Title = title,
                Render = render,
                Open = true
            });
        }

        private void DrawPopoutWindows()
        {
            _popoutWindows.RemoveAll(p => !p.Open);
            for (int i = 0; i < _popoutWindows.Count; i++)
            {
                var pw = _popoutWindows[i];
                if (!pw.Open) continue;
                string id = $"{pw.Title}##popout_{pw.Id}";
                if (ImGui.Begin(id, ref pw.Open))
                {
                    pw.Render();
                }
                ImGui.End();
                _popoutWindows[i] = pw;
            }
        }

        private void DrawGeneralTab()
        {
            using var _ = Profiler.Scope();

            if (ImGui.Button("Open Mod Directory"))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(ModDirectory),
                    UseShellExecute = true
                });
            }

            ImGui.Separator();

            if (ImGui.CollapsingHeader("Player Color", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.Checkbox("Use Random Color", ref useRandomColor))
                    Configuration.SetClientProperty("UseRandomPlayerColor", useRandomColor);

                if (ImGui.ColorPicker3("Color", ref playerColor))
                {
                    Configuration.SetClientProperty("PlayerColor", new ColorRGB
                    {
                        R = (byte)(playerColor.x * 255),
                        G = (byte)(playerColor.y * 255),
                        B = (byte)(playerColor.z * 255),
                    });
                }
            }
        }

        private void DrawSessionTab()
        {
            using var _ = Profiler.Scope();

            if(MultiplayerSession.InSession)
                ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "Multiplayer Active");
            else
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "Multiplayer Not Active");
            ImGui.Separator();

            switch (NetworkConfig.transport)
            {
                case NetworkConfig.NetworkTransport.STEAMWORKS:
                    if (ImGui.Button("Create Lobby"))
                    {
                        SteamLobby.CreateLobby(onSuccess: () =>
                        {
                            //SpeedControlScreen.Instance?.Unpause(false);
                            Game.Instance.Trigger(MP_HASHES.OnMultiplayerGameSessionInitialized);
                        });
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Leave Lobby"))
                        SteamLobby.LeaveLobby();
                    break;
                case NetworkConfig.NetworkTransport.RIPTIDE:
                    if (ImGui.Button("Start Lan"))
                    {
                        MultiplayerSession.Clear();
                        try
                        {
                            DebugConsole.Log("Starting GameServer...");
                            Networking.GameServer.Start();
                            DebugConsole.Log("GameServer started successfully.");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.LogError($"GameServer.Start() failed: {ex}");
                        }
                        SelectToolPatch.UpdateColor();
                        Game.Instance.Trigger(MP_HASHES.OnMultiplayerGameSessionInitialized);
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Stop Lan"))
                    {
                        if (MultiplayerSession.IsHost)
                            Networking.GameServer.Shutdown();

                        if (MultiplayerSession.IsClient)
                            GameClient.Disconnect();

                        NetworkIdentityRegistry.Clear();
                        MultiplayerSession.Clear();

                        SelectToolPatch.UpdateColor();
                    }
                    break;
                default:
                    break;
            }

            ImGui.SameLine();
            if (ImGui.Button("Client Disconnect"))
            {
                GameClient.CacheCurrentServer();
                GameClient.Disconnect();
            }

            ImGui.SameLine();
            if (ImGui.Button("Reconnect"))
                GameClient.ReconnectFromCache();

            ImGui.Separator();
            DisplaySessionDetails();

            if (MultiplayerSession.InSession)
                DrawPlayerList();
            else
                ImGui.TextDisabled("Not in a multiplayer session.");
        }

        private void DrawNetworkTab()
        {
            using var _ = Profiler.Scope();

            DrawNetworkTransportDetails();
            if (!MultiplayerSession.InSession)
            {
                ImGui.TextDisabled("Not connected.");
                return;
            }

            DisplayNetworkStatistics();

            if (ImGui.CollapsingHeader("Packet Tracker"))
            {
                ImGui.Indent();
                if (ImGui.Button("Open Popout"))
                    OpenPopout("Packet Tracker", () => packetTracker?.ShowInTab());
                ImGui.SameLine();
                if (ImGui.Button("Open Bandwidth Popout"))
                    OpenPopout("Bandwidth", () => packetTracker?.DrawBandwidth());
                packetTracker?.ShowInTab();
                ImGui.Unindent();
            }

            if (MultiplayerSession.IsHost)
            {
                ImGui.Separator();
                if (ImGui.Button("Test Hard Sync"))
                    GameServerHardSync.PerformHardSync();
            }
        }

        private void DrawDebugTab()
        {
            using var _ = Profiler.Scope();

            DisplayProfilers();
            ImGui.Separator();
            DisplayNetIdHolders();
        }

        private void DrawTestsTab()
        {
            using var _ = Profiler.Scope();

            if (ImGui.Button("Riptide Smoke Test"))
            {
                RiptideSmokeTest.Run();
            }
            ImGui.SameLine();
            if (ImGui.Button("Start Current Config Server"))
            {
                NetworkConfig.TransportServer.Start();
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop Current Config Server"))
            {
                NetworkConfig.TransportServer.Stop();
            }
            ImGui.Separator();
            ImGui.Text("Dedicated Server Tests");
            DediTest.Update();
            if (ImGui.Button("Connect to dedi"))
            {
                DediTest.Connect();
            }

            ImGui.SameLine();
            if(ImGui.Button("Disconnect from dedi"))
            {
                DediTest.Disconnect();
            }

            if(ImGui.Button("Send test packet"))
            {
                DediTest.SendTestPacket();
            }

            ImGui.Separator();
            DisplayUnitTests();
        }

        private void DisplayUnitTests()
        {
            ImGui.Text("Unit Tests");
            if (ImGui.Button("Run All"))
                UnitTestRegistry.RunAll();

            ImGui.SameLine();

            if (ImGui.Button("Run Failed"))
                UnitTestRegistry.RunFailed();

            ImGui.SameLine();

            ImGui.Checkbox("Auto Run", ref unitTestAutoRun);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.InputFloat("Interval (s)", ref unitTestAutoRunInterval);

            if (ImGui.BeginCombo("Category", unitTestSelectedCategory))
            {
                if (ImGui.Selectable("All", unitTestSelectedCategory == "All"))
                    unitTestSelectedCategory = "All";

                foreach (var category in UnitTestRegistry.GetCategories())
                {
                    if (ImGui.Selectable(category, unitTestSelectedCategory == category))
                        unitTestSelectedCategory = category;
                }

                ImGui.EndCombo();
            }

            ImGui.Separator();

            if (unitTestAutoRun)
            {
                unitTestAutoRunTimer += ImGui.GetIO().DeltaTime;

                if (unitTestAutoRunTimer >= unitTestAutoRunInterval)
                {
                    UnitTestRegistry.RunAll();
                    unitTestAutoRunTimer = 0f;
                }
            }

            if (ImGui.BeginTable("UnitTestsTable", 4,
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.ScrollY))
            {
                // Setup columns: Category | Test (name + button) | Status | Message
                ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Test", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 140);
                ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableHeadersRow();

                foreach (var test in UnitTestRegistry.Tests)
                {
                    if (unitTestSelectedCategory != "All" && test.Category != unitTestSelectedCategory)
                        continue;

                    ImGui.PushID(test.Name);

                    ImGui.TableNextRow();

                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text(test.Category);

                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text(test.Name);
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Run"))
                        test.Run();

                    ImGui.TableSetColumnIndex(2);
                    Vector4 color;
                    string label;

                    if (!test.HasRun)
                    {
                        color = new Vector4(1f, 1f, 0f, 1f);
                        label = "NOT RUN";
                    }
                    else
                    {
                        switch (test.State)
                        {
                            case TestState.Passed:
                                color = new Vector4(0f, 1f, 0f, 1f);
                                label = $"PASS ({test.DurationMs:F2} ms)";
                                break;
                            case TestState.Failed:
                                color = new Vector4(1f, 0f, 0f, 1f);
                                label = $"FAIL ({test.DurationMs:F2} ms)";
                                break;
                            case TestState.InProgress:
                                color = new Vector4(0f, 1f, 1f, 1f);
                                label = $"IN PROGRESS";
                                break;
                            default:
                                color = new Vector4(1f, 1f, 1f, 1f);
                                label = "UNKNOWN";
                                break;
                        }
                    }

                    ImGui.TextColored(color, label);

                    ImGui.TableSetColumnIndex(3);
                    if (!string.IsNullOrEmpty(test.Message))
                        ImGui.TextWrapped(test.Message);

                    ImGui.PopID();
                }

                ImGui.EndTable();
            }
        }

        private void DrawConsoleTab()
        {
            using var _ = Profiler.Scope();

            if (ImGui.Button("Open Popout"))
                OpenPopout("Console", () => console?.ShowInTab());
            ImGui.SameLine();
            console?.ShowInTab();
        }

        public void DisplaySessionDetails()
        {
            using var _ = Profiler.Scope();

            ImGui.Text("Session details:");
            ImGui.Text($"Connected clients: {(MultiplayerSession.InSession ? (MultiplayerSession.PlayerCursors.Count + 1) : 0)}");
            ImGui.Text($"Is Host: {MultiplayerSession.IsHost}");
            ImGui.Text($"Is Client: {MultiplayerSession.IsClient}");
            ImGui.Text($"In Session: {MultiplayerSession.InSession}");
            ImGui.Text($"Local ID: {MultiplayerSession.LocalUserID}");
            ImGui.Text($"Host ID: {MultiplayerSession.HostUserID}");
        }

        private void DrawPlayerList()
        {
            using var _ = Profiler.Scope();

            if(!MultiplayerSession.SessionHasPlayers)
            {
                ImGui.Text("No other players connected.");
                return;
            }

            ImGui.Separator();
            ImGui.Text("Players in Lobby:");

            switch (NetworkConfig.transport)
            {
                case NetworkConfig.NetworkTransport.STEAMWORKS:
                    SteamworksPlayerList();
                    break;
                case NetworkConfig.NetworkTransport.RIPTIDE:
                    RiptidePlayerList();
                    break;
            }
        }

        void SteamworksPlayerList()
        {
            using var _ = Profiler.Scope();

            var players = SteamLobby.GetAllLobbyMembers();
            string self = $"[You] {SteamFriends.GetPersonaName()} | {MultiplayerSession.LocalUserID}";

            RiptideServer server = null;

            foreach (CSteamID player in players)
            {
                bool isTheHost = player.m_SteamID == MultiplayerSession.HostUserID;

                string displayName;
                Vector4 color = new Vector4(1f, 1f, 1f, 1f); // default white
                if (MultiplayerSession.IsHost && isTheHost)
                {
                    displayName = $"[Host/You] {SteamFriends.GetPersonaName()}";
                    color = new Vector4(0.3f, 1f, 0.3f, 1f);
                }
                else if (MultiplayerSession.IsClient && isTheHost)
                {
                    displayName = $"[Host] {SteamFriends.GetFriendPersonaName(player)}";
                    color = new Vector4(1f, 1f, 0f, 1f);
                }
                else if (player.m_SteamID == MultiplayerSession.LocalUserID)
                {
                    displayName = $"[You] {SteamFriends.GetPersonaName()}";
                }
                else
                {
                    displayName = SteamFriends.GetFriendPersonaName(player);
                }

                if (ImGui.Selectable(displayName))
                {
                    SteamFriends.ActivateGameOverlayToUser("steamid", player);
                }

                if (MultiplayerSession.IsHost && !isTheHost)
                {
                    ImGui.SameLine();
                    if (ImGui.Button($"Kick##{player.m_SteamID}")) // ensure unique ID
                    {
                        server = NetworkConfig.GetTransportServer() as RiptideServer;
                        server?.KickClient(player.m_SteamID);
                    }
                }
            }
        }

        void RiptidePlayerList()
        {
            using var _ = Profiler.Scope();

            if(MultiplayerSession.IsHost)
            {
                var players = MultiplayerSession.ConnectedPlayers;
                var server = NetworkConfig.GetTransportServer() as RiptideServer;

                foreach (var player in players)
                {
                    if (player.Value.PlayerId != MultiplayerSession.HostUserID)
                    {
                        if (ImGui.Button("Kick"))
                        {
                            server.KickClient(player.Value.PlayerId);
                        }
                        ImGui.SameLine();
                        ImGui.Text($"{player.Value.PlayerName}");
                    } else
                    {
                        ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), $"[Host/You] {player.Value.PlayerName}");
                    }
                }
            }
            else if(MultiplayerSession.IsClient)
            {
                var client = NetworkConfig.GetTransportClient() as RiptideClient;
                var players = client.ClientList;
                foreach(ulong player in players)
                {
                    if(player == MultiplayerSession.LocalUserID)
                    {
                        ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), $"[You] Player {player}");
                    }
                    else
                    {
                        if (player == MultiplayerSession.HostUserID)
                        {
                            ImGui.TextColored(new Vector4(1f, 1f, 0f, 1f), $"[Host] Player {player}");
                        }
                        else
                        {
                            ImGui.Text($"{player}");
                        }
                    }
                }
            }
        }

        public void DisplayNetworkStatistics()
        {
            using var _ = Profiler.Scope();

            if(!MultiplayerSession.InSession)
                return;

            ImGui.Separator();
            ImGui.Text("Network Statistics");
            // TODO Update:
            //ImGui.Text($"Ping: {GameClient.GetPingToHost()}");
            //ImGui.Text($"Quality(L/R): {GameClient.GetLocalPacketQuality():0.00} / {GameClient.GetRemotePacketQuality():0.00}");
            //ImGui.Text($"Unacked Reliable: {GameClient.GetUnackedReliable()}");
            //ImGui.Text($"Pending Unreliable: {GameClient.GetPendingUnreliable()}");
            //ImGui.Text($"Queue Time: {GameClient.GetUsecQueueTime() / 1000}ms");
            ImGui.Spacing();
            int ping = 0;
            if (MultiplayerSession.IsClient)
            {
                ping = NetworkConfig.GetTransportClient().GetPing();
            }
            ImGui.Text($"Ping: {ping}");
            ImGui.Text($"Latency: {Utils.NetworkStateToString(NetworkIndicatorsScreen.latencyState)}");
            ImGui.Text($"Jitter: {Utils.NetworkStateToString(NetworkIndicatorsScreen.jitterState)}");
            ImGui.Text($"Packet Loss: {Utils.NetworkStateToString(NetworkIndicatorsScreen.packetlossState)}");
            ImGui.Text($"Server Performance: {Utils.NetworkStateToString(NetworkIndicatorsScreen.serverPerformanceState)}");

            // Sync Statistics (Host only)
            if (MultiplayerSession.IsHost)
            {
                ImGui.Separator();
                if (ImGui.CollapsingHeader("Sync Statistics"))
                {
                    float fps = 1f / Time.unscaledDeltaTime;
                    ImGui.Text($"FPS: {fps:F0} | Clients: {MultiplayerSession.ConnectedPlayers.Count}");
                    ImGui.Spacing();

                    foreach (var m in SyncStats.AllMetrics)
                    {
                        if (m.LastSyncTime > 0)
                        {
                            ImGui.Text($"{m.Name}: {m.TimeRemaining:F1}s | {m.LastItemCount} items, {m.LastPacketBytes}B, {m.LastDurationMs:F1}ms");
                        }
                        else
                        {
                            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"{m.Name}: waiting...");
                        }
                    }
                }
            }
        }

        private string netIdFilter = string.Empty;
		public void DisplayNetIdHolders()
		{
            using var _ = Profiler.Scope();

			if (ImGui.CollapsingHeader("Net Id Holders"))
			{
				var all_identities = NetworkIdentityRegistry.AllIdentities;

				ImGui.InputText("Filter", ref netIdFilter, 64);
				ImGui.Separator();

				if (ImGui.BeginTable("net_identity_table", 2,
						ImGuiTableFlags.Borders |
						ImGuiTableFlags.RowBg |
						ImGuiTableFlags.ScrollY, new UnityEngine.Vector2(0, 400)))
				{
					ImGui.TableSetupColumn("Name");
					ImGui.TableSetupColumn("Network ID");

					ImGui.TableHeadersRow();

					foreach (var identity in all_identities)
					{
						string identityName = identity.gameObject.name;
						string identityNetId = identity.NetId.ToString();

						if (!string.IsNullOrEmpty(netIdFilter))
						{
							bool matchesType =
								identityName.IndexOf(netIdFilter, StringComparison.OrdinalIgnoreCase) >= 0;

							bool matchesId =
								identityNetId.IndexOf(netIdFilter, StringComparison.OrdinalIgnoreCase) >= 0;

							if (!matchesType && !matchesId)
								continue;
						}

						ImGui.TableNextRow();

						ImGui.TableSetColumnIndex(0);
						ImGui.Text(identityName);

						ImGui.TableSetColumnIndex(1);
						ImGui.Text(identityNetId);
					}

					ImGui.EndTable();
				}
			}
		}

        public void DisplayProfilers()
        {
            using var _ = Profiler.Scope();

            Profiler.DrawImGuiInline();
        }

        public void DrawNetworkTransportDetails()
        {
            using var _ = Profiler.Scope();

            ImGui.Text("Network Transport Settings");

            string[] display_options = new string[] { "Steam", "LAN/Riptide" };
            ImGui.Text($"Currently used transport: {display_options[(int)NetworkConfig.transport]}");

            string[] options = new string[] { "Steam", "LAN" };
            // Dropdown for Steam/LAN
            ImGui.Combo("Transport Type", ref selectedTransportType, options, options.Length);

            // Only show LAN-specific fields if LAN is selected
            if (selectedTransportType == 1)
            {
                ImGui.Indent();
                ImGui.Separator();

                string[] lan_options = new string[] { "Riptide" };
                ImGui.Combo("Lan Type", ref selectedLanType, lan_options, lan_options.Length);
                ImGui.Separator();

                // Host section
                ImGui.Text("Host Settings (Used for hosting a server)");
                ImGui.InputText("Host IP", ref hostIP, 64);
                ImGui.InputInt("Host Port", ref hostPort);
                settings_host.Ip = hostIP;
                settings_host.Port = hostPort;

                ImGui.Separator();

                // Client section
                ImGui.Text("Client Settings (The server you are connecting too)");
                ImGui.InputText("Client IP", ref clientIP, 64);
                ImGui.InputInt("Client Port", ref clientPort);
                settings_client.Ip = hostIP;
                settings_client.Port = hostPort;
                ImGui.Unindent();
            }

            if (ImGui.Button("Save & Apply"))
            {
                Configuration.Instance.Host.LanSettings.Ip = hostIP;
                Configuration.Instance.Host.LanSettings.Port = hostPort;
                Configuration.Instance.Client.LanSettings.Ip = clientIP;
                Configuration.Instance.Client.LanSettings.Port = clientPort;

                NetworkConfig.NetworkTransport selected_transport = NetworkConfig.NetworkTransport.STEAMWORKS;
                if (selectedTransportType == 0)
                {
                    selected_transport = NetworkConfig.NetworkTransport.STEAMWORKS;
                }
                else
                {
                    selected_transport = NetworkConfig.NetworkTransport.RIPTIDE;
                }
                Configuration.Instance.Host.NetworkTransport = (int)selected_transport;
                NetworkConfig.UpdateTransport(selected_transport);
                Configuration.Instance.Save();
            }
        }
        private void DrawOxySyncTab()
        {
            if (OxySyncManager.Instance == null)
            {
                ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "OxySyncManager not initialized");
                return;
            }

            ImGui.Text($"Registered Behaviours: {OxySyncManager.Instance.RegisteredCount}");

            ImGui.Separator();

            var behaviours = OxySyncManager.Instance.AllBehaviours;
            if (behaviours.Count == 0)
            {
                ImGui.TextDisabled("No OxySync NetworkBehaviours registered.");
                return;
            }

            for (int b = 0; b < behaviours.Count; b++)
            {
                var behaviour = behaviours[b];
                if (behaviour.IsNullOrDestroyed()) continue;

                string header = $"{behaviour.GetType().Name} (NetId: {behaviour.NetId}, Sync: {behaviour.SyncInterval:F2}s)##oxy_{b}";
                if (!ImGui.CollapsingHeader(header)) continue;

                ImGui.Indent();

                var syncVars = behaviour.SyncVarFields;
                if (syncVars.Count > 0)
                {
                    ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "SyncVars:");
                    ImGui.Separator();

                    for (int i = 0; i < syncVars.Count; i++)
                    {
                        var field = syncVars[i];
                        var currentValue = behaviour.GetSyncVarValue(field.Hash);
                        string typeName = field.Info.FieldType.Name;
                        string valueStr = currentValue?.ToString() ?? "null";

                        ImGui.PushID($"syncvar_{b}_{i}");
                        ImGui.Text($"{field.Info.Name} ({typeName}): {valueStr}");
                        ImGui.SameLine();

                        if (field.Info.FieldType == typeof(bool))
                        {
                            if (ImGui.SmallButton("Toggle"))
                            {
                                behaviour.SetSyncVarValue(field.Hash, !(bool)(currentValue ?? false));
                            }
                        }
                        else
                        {
                            string popupId = $"set_{b}_{i}";
                            if (ImGui.SmallButton("Set"))
                            {
                                ImGui.OpenPopup(popupId);
                            }

                            bool openModal = true;
                            if (ImGui.BeginPopupModal(popupId, ref openModal, ImGuiWindowFlags.AlwaysAutoResize))
                            {
                                string input = currentValue?.ToString() ?? "";
                                ImGui.Text($"Set {field.Info.Name}:");
                                ImGui.SetNextItemWidth(200);
                                if (ImGui.InputText("##value", ref input, 256, ImGuiInputTextFlags.EnterReturnsTrue))
                                {
                                    object? newVal = ParseValue(input, field.Info.FieldType);
                                    if (newVal != null)
                                        behaviour.SetSyncVarValue(field.Hash, newVal);
                                    ImGui.CloseCurrentPopup();
                                }
                                if (ImGui.Button("Cancel"))
                                    ImGui.CloseCurrentPopup();
                                ImGui.EndPopup();
                            }
                        }
                        ImGui.PopID();
                    }
                }

                var commands = behaviour.Commands;
                if (commands.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), "Commands:");
                    ImGui.Separator();

                    foreach (var kvp in commands)
                    {
                        var cmd = kvp.Value;
                        string label = $"[Command] {cmd.Info.Name}({string.Join(", ", cmd.ArgTypes.Select(t => t.Name))})";
                        ImGui.PushID($"cmd_{kvp.Key}");
                        ImGui.Text(label);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Call"))
                        {
                            var packet = new Networking.OxySync.Packets.CommandPacket
                            {
                                NetId = behaviour.NetId,
                                MethodHash = cmd.Hash,
                                Args = Shared.OxySync.RpcSerializer.Serialize(System.Array.Empty<object>(), cmd.ArgTypes),
                            };

                            if (MultiplayerSession.IsHost)
                            {
                                if (NetworkIdentityRegistry.TryGetComponent<NetworkBehaviour>(behaviour.NetId, out var nb))
                                    nb.InvokeCommand(cmd.Hash, packet.Args);
                            }
                            else
                            {
                                PacketSender.SendToHost(packet);
                            }
                        }
                        ImGui.PopID();
                    }
                }

                var clientRpcs = behaviour.ClientRpcs;
                if (clientRpcs.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.3f, 0.8f, 1f, 1f), "ClientRpcs:");
                    ImGui.Separator();

                    foreach (var kvp in clientRpcs)
                    {
                        var rpc = kvp.Value;
                        string label = $"[ClientRpc] {rpc.Info.Name}({string.Join(", ", rpc.ArgTypes.Select(t => t.Name))})";
                        ImGui.PushID($"rpc_{kvp.Key}");
                        ImGui.Text(label);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Call"))
                        {
                            if (MultiplayerSession.IsHost)
                            {
                                var packet = new Networking.OxySync.Packets.ClientRpcPacket
                                {
                                    NetId = behaviour.NetId,
                                    MethodHash = rpc.Hash,
                                    Args = Shared.OxySync.RpcSerializer.Serialize(System.Array.Empty<object>(), rpc.ArgTypes),
                                    TargetPlayerId = ulong.MaxValue,
                                };
                                PacketSender.SendToAllClients(packet);
                            }
                        }
                        ImGui.PopID();
                    }
                }

                var targetRpcs = behaviour.TargetRpcs;
                if (targetRpcs.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.8f, 0.3f, 1f, 1f), "TargetRpcs:");
                    ImGui.Separator();

                    foreach (var kvp in targetRpcs)
                    {
                        var rpc = kvp.Value;
                        string label = $"[TargetRpc] {rpc.Info.Name}({string.Join(", ", rpc.ArgTypes.Select(t => t.Name))})";
                        ImGui.PushID($"trpc_{kvp.Key}");
                        ImGui.Text(label);
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Call"))
                        {
                            if (MultiplayerSession.IsHost)
                            {
                                foreach (var player in MultiplayerSession.ConnectedPlayers)
                                {
                                    if (player.Value.PlayerId == MultiplayerSession.HostUserID) continue;

                                    var packet = new Networking.OxySync.Packets.ClientRpcPacket
                                    {
                                        NetId = behaviour.NetId,
                                        MethodHash = rpc.Hash,
                                        Args = Shared.OxySync.RpcSerializer.Serialize(System.Array.Empty<object>(), rpc.ArgTypes),
                                        TargetPlayerId = player.Value.PlayerId,
                                    };
                                    PacketSender.SendToPlayer(player.Value.PlayerId, packet);
                                }
                            }
                        }
                        ImGui.PopID();
                    }
                }

                ImGui.Spacing();
                if (ImGui.Button($"Remove Component##rm_{b}"))
                {
                    UnityEngine.Object.Destroy(behaviour);
                }

                ImGui.Unindent();
            }

            ImGui.Separator();

            if (ImGui.Button("Spawn Test Entity"))
            {
                var go = new GameObject("OxySync_TestEntity");
                go.AddComponent<Networking.Components.NetworkIdentity>();
                go.AddComponent<OxySyncTestComponent>();
                UnityEngine.Object.DontDestroyOnLoad(go);
                NetworkIdentityRegistry.TryGet(go.GetComponent<ONI_Together.Networking.Components.NetworkIdentity>().NetId, out _);
                DebugConsole.Log("[OxySync] Spawned test entity at runtime");
            }

            ImGui.SameLine();
            if (ImGui.Button("Attach to Selected"))
            {
                var selected = SelectTool.Instance?.selected;
                if (selected != null)
                {
                    var go = selected.gameObject;
                    if (go.GetComponent<OxySyncTestComponent>() == null)
                    {
                        go.AddComponent<Networking.Components.NetworkIdentity>();
                        go.AddComponent<OxySyncTestComponent>();
                        DebugConsole.Log($"[OxySync] Attached test component to {go.name}");
                    }
                }
            }
        }

        private static object? ParseValue(string input, Type targetType)
        {
            try
            {
                if (targetType == typeof(int)) return int.Parse(input);
                if (targetType == typeof(float)) return float.Parse(input);
                if (targetType == typeof(byte)) return byte.Parse(input);
                if (targetType == typeof(long)) return long.Parse(input);
                if (targetType == typeof(double)) return double.Parse(input);
                if (targetType == typeof(string)) return input;
                if (targetType == typeof(bool)) return bool.Parse(input);
            }
            catch { }
            return null;
        }
    }
}
#endif
