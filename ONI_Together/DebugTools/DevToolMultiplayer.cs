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

        // OxySync
        private string _oxySyncFilter = string.Empty;
        private NetworkBehaviour? _selectedBehaviour = null;
        private int _selectedNetId = int.MinValue;
        private int _oxySyncSelectedWorldIdx = 0;
        private int _oxySyncSelectedTypeIdx = 0;
        private List<string> _oxySyncTypeNames = new() { "All" };
        private string[] _oxySyncWorldOptions = new[] { "All", "Group -1 (Broadcast)" };
        private int[] _oxySyncWorldIds = new[] { -2, -1 };
        private bool _oxySyncShowSyncingOnly = false;

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

            var behaviours = OxySyncManager.Instance.AllBehaviours;

            BuildOxySyncWorldOptions();
            BuildOxySyncTypeOptions(behaviours);

            ImGui.SetNextItemWidth(140);
            if (ImGui.BeginCombo("World", _oxySyncWorldOptions[_oxySyncSelectedWorldIdx]))
            {
                for (int i = 0; i < _oxySyncWorldOptions.Length; i++)
                {
                    if (ImGui.Selectable(_oxySyncWorldOptions[i], _oxySyncSelectedWorldIdx == i))
                        _oxySyncSelectedWorldIdx = i;
                }
                ImGui.EndCombo();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(160);
            if (ImGui.BeginCombo("Type", _oxySyncTypeNames[_oxySyncSelectedTypeIdx]))
            {
                for (int i = 0; i < _oxySyncTypeNames.Count; i++)
                {
                    if (ImGui.Selectable(_oxySyncTypeNames[i], _oxySyncSelectedTypeIdx == i))
                        _oxySyncSelectedTypeIdx = i;
                }
                ImGui.EndCombo();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Search", ref _oxySyncFilter, 128);

            ImGui.SameLine();
            ImGui.Checkbox("Syncing only", ref _oxySyncShowSyncingOnly);

            ImGui.Text($"Registered Behaviours: {behaviours.Count}");

            if (ImGui.CollapsingHeader("Interest Groups", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ClusterManager.Instance != null)
                {
                    int activeWorld = ClusterManager.Instance.activeWorldId;
                    ImGui.TextColored(new Vector4(0.3f, 1f, 1f, 1f), $"Active World ID: {activeWorld}");
                }

                ImGui.Separator();
                ImGui.Text("Player Group Memberships:");
                if (MultiplayerSession.InSession)
                {
                    foreach (var player in MultiplayerSession.ConnectedPlayers)
                    {
                        ulong pid = player.Key;
                        string name = player.Value.PlayerName ?? pid.ToString();
                        ImGui.Text($"  {name} ({pid}):");
                        ImGui.SameLine();
                        ImGui.PushID($"ig_player_{pid}");
                        for (int i = 0; i < 5; i++)
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"+{i}"))
                            {
                                InterestGroupManager.AddPlayerToGroup(pid, i);
                            }
                        }
                        ImGui.SameLine();
                        ImGui.Spacing();
                        for (int i = 0; i < 5; i++)
                        {
                            ImGui.SameLine();
                            if (ImGui.SmallButton($"-{i}"))
                            {
                                InterestGroupManager.RemovePlayerFromGroup(pid, i);
                            }
                        }

                        var groups = InterestGroupManager.GetGroupsPlayerIsIn(pid);
                        if (groups.Count > 0)
                            ImGui.Text($"  Groups: {string.Join(", ", groups)}");
                        else if (pid == MultiplayerSession.HostUserID)
                            ImGui.TextDisabled("  Host — no groups needed");
                        else
                            ImGui.TextDisabled("  No groups");
                        ImGui.PopID();
                    }
                }
                else
                {
                    ImGui.TextDisabled("  Not in a multiplayer session.");
                }
                ImGui.TextDisabled("  (-1 = broadcast to all)");
            }

            ImGui.Separator();

            if (behaviours.Count == 0)
            {
                ImGui.TextDisabled("No OxySync NetworkBehaviours registered.");
                return;
            }

            bool hasTextFilter = !string.IsNullOrEmpty(_oxySyncFilter);
            int selectedWorldId = _oxySyncWorldIds[_oxySyncSelectedWorldIdx];
            string selectedTypeName = _oxySyncTypeNames[_oxySyncSelectedTypeIdx];
            bool hasTypeFilter = _oxySyncSelectedTypeIdx > 0;
            var available = ImGui.GetContentRegionAvail();

            // Build filtered list
            var filteredBehaviours = new List<NetworkBehaviour>();
            for (int i = 0; i < behaviours.Count; i++)
            {
                var b = behaviours[i];
                if (b.IsNullOrDestroyed()) continue;

                if (selectedWorldId == -1)
                {
                    if (b.InterestGroup != -1) continue;
                }
                else if (selectedWorldId >= 0)
                {
                    int myWorldId = b.GetMyWorldId();
                    if (myWorldId < 0 || myWorldId != selectedWorldId) continue;
                }

                string typeName = b.GetType().Name;
                if (hasTypeFilter && typeName != selectedTypeName) continue;

                if (hasTextFilter)
                {
                    string netIdStr = b.NetId.ToString();
                    string groupStr = b.InterestGroup.ToString();
                    string goName = b.gameObject?.name ?? "?";
                    bool matchesType = typeName.IndexOf(_oxySyncFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchesId = netIdStr.IndexOf(_oxySyncFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchesGroup = groupStr.IndexOf(_oxySyncFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchesName = goName.IndexOf(_oxySyncFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchesType && !matchesId && !matchesGroup && !matchesName) continue;
                }

                if (_oxySyncShowSyncingOnly && MultiplayerSession.IsHost &&
                    (Time.unscaledTime - b._lastActiveSyncTime) > 2f) continue;

                filteredBehaviours.Add(b);
            }

            // Build NetId groups
            var netIdGroups = new Dictionary<int, List<NetworkBehaviour>>();
            for (int i = 0; i < filteredBehaviours.Count; i++)
            {
                var b = filteredBehaviours[i];
                int netId = b.NetId;
                if (!netIdGroups.TryGetValue(netId, out var list))
                {
                    list = new List<NetworkBehaviour>();
                    netIdGroups[netId] = list;
                }
                list.Add(b);
            }

            var sortedNetIds = netIdGroups.Keys.OrderBy(id => id).ToList();

            if (ImGui.BeginTable("OxySyncTable", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings,
                new Vector2(available.x, available.y - 30f)))
            {
                ImGui.TableSetupColumn("NetId Groups", ImGuiTableColumnFlags.WidthFixed, 350);
                ImGui.TableSetupColumn("Behaviours", ImGuiTableColumnFlags.WidthFixed, 300);
                ImGui.TableSetupColumn("Detail", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);

                ImGui.BeginChild("OxySyncNetIdList", new Vector2(0, 0), true);

                foreach (int netId in sortedNetIds)
                {
                    var group = netIdGroups[netId];
                    var first = group[0];
                    string goName = first.gameObject?.name ?? "?";
                    int count = group.Count;
                    int interestGroup = first.InterestGroup;

                    string displayName = string.Format(global::STRINGS.UI.StripLinkFormatting(goName));
                    string label = $"{displayName} ({count} behaviours) (NetID: {netId}, Group: {interestGroup})";
                    bool isSelected = netId == _selectedNetId;

                    if (MultiplayerSession.IsHost)
                    {
                        bool anySyncing = group.Any(b => (Time.unscaledTime - b._lastActiveSyncTime) <= 2f);
                        if (anySyncing)
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1f, 0.3f, 1f));
                    }

                    if (ImGui.Selectable(label, isSelected))
                    {
                        _selectedNetId = netId;
                        _selectedBehaviour = null;
                    }

                    if (MultiplayerSession.IsHost)
                    {
                        bool anySyncing = group.Any(b => (Time.unscaledTime - b._lastActiveSyncTime) <= 2f);
                        if (anySyncing)
                            ImGui.PopStyleColor();
                    }
                }

                ImGui.EndChild();

                ImGui.TableSetColumnIndex(1);

                ImGui.BeginChild("OxySyncBehaviourList", new Vector2(0, 0), true);

                if (_selectedNetId == int.MinValue)
                {
                    ImGui.TextDisabled("Select a NetId group from the left panel");
                }
                else if (netIdGroups.TryGetValue(_selectedNetId, out var selectedGroup))
                {
                    foreach (var b in selectedGroup)
                    {
                        string typeName = b.GetType().Name;
                        string goName = b.gameObject?.name ?? "?";
                        string label = $"{typeName}";
                        bool isSelected = b == _selectedBehaviour;

                        if (MultiplayerSession.IsHost && (Time.unscaledTime - b._lastActiveSyncTime) <= 2f)
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 1f, 0.3f, 1f));

                        if (ImGui.Selectable(label, isSelected))
                        {
                            _selectedBehaviour = b;
                        }

                        if (MultiplayerSession.IsHost && (Time.unscaledTime - b._lastActiveSyncTime) <= 2f)
                            ImGui.PopStyleColor();
                    }
                }

                ImGui.EndChild();

                ImGui.TableSetColumnIndex(2);

                ImGui.BeginChild("OxySyncDetail", new Vector2(0, 0), true);

                if (_selectedBehaviour != null && !_selectedBehaviour.IsNullOrDestroyed())
                {
                    DrawOxySyncBehaviourDetail(_selectedBehaviour);
                }
                else
                {
                    if (_selectedBehaviour != null)
                        _selectedBehaviour = null;
                    if (_selectedNetId != int.MinValue)
                        ImGui.TextDisabled("Select a behaviour from the centre panel to inspect.");
                    else
                        ImGui.TextDisabled("Select a NetId group from the left panel.");
                }

                ImGui.EndChild();

                ImGui.EndTable();
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

        private void DrawOxySyncBehaviourDetail(NetworkBehaviour behaviour)
        {
            string goName = behaviour.gameObject?.name ?? "?";
            ImGui.TextColored(new Vector4(1f, 1f, 0.3f, 1f),
                $"{behaviour.GetType().Name}  (NetId: {behaviour.NetId}, Sync: {behaviour.SyncInterval:F2}s)  [{goName}]");

            if (MultiplayerSession.IsHost)
            {
                bool isSyncing = (Time.unscaledTime - behaviour._lastActiveSyncTime) <= 2f;
                if (isSyncing)
                    ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), "● Syncing");
                else
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "○ Idle");
            }
            else
            {
                ImGui.TextDisabled("Sync status only available on host");
            }

            if (MultiplayerSession.IsHost)
            {
                float lastActive = Time.unscaledTime - behaviour._lastActiveSyncTime;
                float lastCheck = Time.unscaledTime - behaviour._lastSyncTime;
                ImGui.Text($"Last synced: {lastActive:F1}s ago  |  Last check: {lastCheck:F1}s ago");
            }
            else
            {
                ImGui.TextDisabled("Last synced: N/A (client)");
            }

            int ig = behaviour.InterestGroup;
            ImGui.TextColored(new Vector4(0.3f, 1f, 1f, 1f),
                ig == -1 ? "Interest Group: -1 (broadcast)" : $"Interest Group: {ig}");
            if (ig != -1 && MultiplayerSession.InSession)
            {
                ImGui.SameLine();
                if (MultiplayerSession.IsHost)
                    ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), " (host — all groups)");
                else
                {
                    var localGroups = InterestGroupManager.GetGroupsPlayerIsIn(MultiplayerSession.LocalUserID);
                    if (localGroups.Contains(ig))
                        ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), " (subscribed)");
                    else
                        ImGui.TextColored(new Vector4(1f, 0.5f, 0.3f, 1f), " (not subscribed)");
                }
            }

            ImGui.Separator();

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
                    string groupLabel = field.InterestGroup == -1 ? "" : $" [Group: {field.InterestGroup}]";

                    ImGui.PushID($"detail_syncvar_{i}");
                    ImGui.Text($"{field.Info.Name}{groupLabel} ({typeName}): {valueStr}");
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
                        string popupId = $"detail_set_{i}";
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
                    ImGui.PushID($"detail_cmd_{kvp.Key}");
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
                    string groupLabel = rpc.InterestGroup == -1 ? "" : $" [Group: {rpc.InterestGroup}]";
                    string label = $"[ClientRpc] {rpc.Info.Name}{groupLabel}({string.Join(", ", rpc.ArgTypes.Select(t => t.Name))})";
                    ImGui.PushID($"detail_rpc_{kvp.Key}");
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
                    ImGui.PushID($"detail_trpc_{kvp.Key}");
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
            ImGui.Separator();
            if (ImGui.Button("Remove Component"))
            {
                UnityEngine.Object.Destroy(behaviour);
                _selectedBehaviour = null;
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

        private void BuildOxySyncWorldOptions()
        {
            var worldList = new List<string> { "All", "Group -1 (Broadcast)" };
            var idList = new List<int> { -2, -1 };

            if (ClusterManager.Instance != null)
            {
                foreach (var world in ClusterManager.Instance.WorldContainers)
                {
                    if (world != null)
                    {
                        string name = world.GetProperName();
                        if (name.Length > 30)
                            name = name[..30] + "…";
                        worldList.Add($"World {world.id}: {name}");
                        idList.Add(world.id);
                    }
                }
            }

            _oxySyncWorldOptions = worldList.ToArray();
            _oxySyncWorldIds = idList.ToArray();

            if (_oxySyncSelectedWorldIdx >= _oxySyncWorldOptions.Length)
                _oxySyncSelectedWorldIdx = 0;
        }

        private void BuildOxySyncTypeOptions(IReadOnlyList<NetworkBehaviour> behaviours)
        {
            var types = new HashSet<string>();
            for (int i = 0; i < behaviours.Count; i++)
            {
                if (!behaviours[i].IsNullOrDestroyed())
                    types.Add(behaviours[i].GetType().Name);
            }

            _oxySyncTypeNames = new List<string> { "All" };
            _oxySyncTypeNames.AddRange(types.OrderBy(t => t));

            if (_oxySyncSelectedTypeIdx >= _oxySyncTypeNames.Count)
                _oxySyncSelectedTypeIdx = 0;
        }
    }
}
#endif
