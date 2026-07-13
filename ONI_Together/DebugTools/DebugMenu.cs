using ONI_Together.Networking;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;
using ONI_Together.Patches.ToolPatches;
using System;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.DebugTools
{
	public class DebugMenu : MonoBehaviour
	{
		private static DebugMenu _instance;

		private bool showMenu = false;
		private Rect windowRect = new Rect(10, 10, 250, 300); // Position and size
		private HierarchyViewer hierarchyViewer;
		private DebugConsole debugConsole;

		private Vector2 scrollPosition = Vector2.zero;

		// LAN
        private string lanHostIP = "127.0.0.1";
        private string lanHostPort = "7777";
        private string[] hostTransportOptions = new string[]
        {
            "Steam",
            "LAN"
        };
        private int selectedHostTransport = 0;

        private string lanJoinAddress = "127.0.0.1:7777";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		public static void Init()
		{
            using var _ = Profiler.Scope();

			if (_instance != null) return;

			GameObject go = new GameObject("ONI_Together_DebugMenu");
			DontDestroyOnLoad(go);
			_instance = go.AddComponent<DebugMenu>();

            _instance.lanHostIP = Configuration.Instance.Host.LanSettings.Ip;
            _instance.lanHostPort = Configuration.Instance.Host.LanSettings.Port.ToString();

            _instance.lanJoinAddress = $"{Configuration.Instance.Client.LanSettings.Ip}:{Configuration.Instance.Client.LanSettings.Port}";

        }

		private void Awake()
		{
            using var _ = Profiler.Scope();

			hierarchyViewer = gameObject.AddComponent<HierarchyViewer>();
			//debugConsole = gameObject.AddComponent<DebugConsole>();
		}

		private void Update()
		{
            using var _ = Profiler.Scope();

            return; // Disabled, no longer in use (for now)
            if (Input.GetKeyDown(KeyCode.F2) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
			{
				showMenu = !showMenu;
			}
		}

		private void OnGUI()
		{
            using var _ = Profiler.Scope();

			if (!showMenu) return;

			GUIStyle windowStyle = new GUIStyle(GUI.skin.window) { padding = new RectOffset(10, 10, 20, 20) };
			windowRect = GUI.ModalWindow(888, windowRect, DrawMenuContents, "DEBUG MENU", windowStyle);
		}

        private void DrawMenuContents(int windowID)
        {
            using var _ = Profiler.Scope();

            scrollPosition = GUILayout.BeginScrollView(
                scrollPosition,
                false,
                true,
                GUILayout.Width(windowRect.width - 20),
                GUILayout.Height(windowRect.height - 40)
            );

            GUILayout.Label("Hosting", GUI.skin.box);
            GUILayout.Label("Transport:");
            selectedHostTransport = GUILayout.Toolbar(selectedHostTransport, hostTransportOptions);

            GUILayout.Space(5);

            GUILayout.Label("Host IP:");
            lanHostIP = GUILayout.TextField(lanHostIP);

            GUILayout.Label("Port:");
            lanHostPort = GUILayout.TextField(lanHostPort);

            if (GUILayout.Button("Start Hosting"))
            {
                if(selectedHostTransport == 0)
                {
                    NetworkConfig.NetworkTransport selected_transport = NetworkConfig.NetworkTransport.STEAMWORKS;
                    Configuration.Instance.Host.NetworkTransport = (int)selected_transport;
                    NetworkConfig.UpdateTransport(selected_transport);
                    Configuration.Instance.Save();

                    NetworkConfig.StartServer();
                    return;
                }

                if (int.TryParse(lanHostPort, out int port))
                {
                    DebugConsole.Log($"[LAN] Hosting on {lanHostIP}:{port}");

                    Configuration.Instance.Host.LanSettings.Ip = lanHostIP;
                    Configuration.Instance.Host.LanSettings.Port = port;

                    NetworkConfig.NetworkTransport selected_transport = NetworkConfig.NetworkTransport.RIPTIDE;
                    Configuration.Instance.Host.NetworkTransport = (int)selected_transport;
                    NetworkConfig.UpdateTransport(selected_transport);

                    Configuration.Instance.Save();

                    NetworkConfig.StartServer();
                }
                else
                {
                    DebugConsole.LogError("Invalid port!");
                }
            }
            if (GUILayout.Button("Stop Hosting"))
            {
                NetworkConfig.Stop();
            }


            GUILayout.Space(10);

            GUILayout.Label("LAN Join", GUI.skin.box);

            GUILayout.Label("Server Address:");
            lanJoinAddress = GUILayout.TextField(lanJoinAddress);

            if (GUILayout.Button("Join Server"))
            {
                NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.RIPTIDE); // Force into riptide (Testing)
                DebugConsole.Log($"[LAN] Joining {lanJoinAddress}");

                string[] address = lanJoinAddress.Split(':');
                if(address.Length != 2)
                {
                    DebugConsole.LogError("Invalid address format! Use IP:Port", false);
                    return;
                }

                if (int.TryParse(address[1], out int port))
                {
                    Configuration.Instance.Client.LanSettings.Ip = address[0];
                    Configuration.Instance.Client.LanSettings.Port = port;
                    Configuration.Instance.Save();

                    Join(address[0], port);
                }
            }

            GUILayout.EndScrollView();

            GUI.DragWindow();
        }

        void Join(string ip, int port)
        {
            using var _ = Profiler.Scope();

            GameClient.ConnectToHost(ip: ip, port: port);
        }
	}
}
