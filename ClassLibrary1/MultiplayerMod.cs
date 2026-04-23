using HarmonyLib;
using KMod;
using ONI_MP.Components;
using ONI_MP.DebugTools;
using ONI_MP.Misc;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Networking.Transport.Steamworks;
using PeterHan.PLib.AVC;
using System;
using System.Collections.Generic;
using System.Reflection;
using Shared.Profiling;
using UnityEngine;
using static DistributionPlatform;

namespace ONI_MP
{
	//Template: https://github.com/O-n-y/OxygenNotIncludedModTemplate

	public class MultiplayerMod : UserMod2
	{

		public static readonly Dictionary<string, AssetBundle> LoadedBundles = new Dictionary<string, AssetBundle>();

		public static System.Action OnPostSceneLoaded;
		public static Harmony Harmony;

		public static bool UseSteamOverlay = true; // Will be false for non steam instances
		private static bool _inLogHandler = false;

        public override void OnLoad(Harmony harmony)
		{
			using var _ = Profiler.Scope();

			Harmony = harmony;
			base.OnLoad(harmony);

            ModAssets.LoadAssetBundles();

            string logPath = System.IO.Path.Combine(Application.dataPath, "../ONI_MP_Log.txt");

			try
			{
				DebugConsole.Init(); // Init console first to catch logs
				PacketTracker.Init();
				DebugConsole.Log("[ONI_MP] Loaded Oxygen Not Included Together Multiplayer Mod.");

                PacketRegistry.RegisterDefaults();

                // CHECKPOINT 1
                System.IO.File.AppendAllText(logPath, "[Trace] Checkpoint 1: Pre-DebugMenu\n");
				DebugMenu.Init();

                // CHECKPOINT 2
                System.IO.File.AppendAllText(logPath, "[Trace] Checkpoint 2: Pre-SteamLobby\n");
				SteamLobby.Initialize();

				// CHECKPOINT 3
				System.IO.File.AppendAllText(logPath, "[Trace] Checkpoint 3: Pre-GameObjects\n");
				var go = new GameObject("Multiplayer_Modules");
				UnityEngine.Object.DontDestroyOnLoad(go);

				// CHECKPOINT 4
				System.IO.File.AppendAllText(logPath, "[Trace] Checkpoint 4: Pre-Components\n");
				go.AddComponent<NetworkingComponent>();
				go.AddComponent<UIVisibilityController>();
				go.AddComponent<MainThreadExecutor>();
				go.AddComponent<CursorManager>();
				go.AddComponent<PingManager>();
				go.AddComponent<BuildingSyncer>();
				go.AddComponent<WorldStateSyncer>();
				go.AddComponent<PlantGrowthSyncer>();
				go.AddComponent<AnimSyncCoordinator>();
				go.AddComponent<AnimResyncRequester>();
				go.AddComponent<BulkPacketMonitor>();

				// CHECKPOINT 5
				System.IO.File.AppendAllText(logPath, "[Trace] Checkpoint 5: Pre-Listeners\n");
				SetupListeners();

				// CHECKPOINT 6
				System.IO.File.AppendAllText(logPath, "[Trace] Checkpoint 6: Pre-ResLoad\n");
				LoadAssetBundles();

				foreach (var res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
				{
					DebugConsole.Log("Embedded Resource: " + res);
				}

				System.IO.File.AppendAllText(logPath, "[Trace] Checkpoint 7: Success\n");
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[ONI_MP] CRITICAL ERROR IN ONLOAD: {ex}");
				DebugConsole.LogException(ex);
			}


			RegisterDevTools();
			LoadNetworkRelay();

			// Diagnostic hooks for unhandled exceptions
			Application.logMessageReceived += (condition, stackTrace, type) =>
			{
				if (_inLogHandler) return;
				if (type == LogType.Exception || type == LogType.Error)
				{
					_inLogHandler = true;
					DebugConsole.LogError($"[Unity] {type}: {condition}\n{stackTrace}");
					_inLogHandler = false;
				}
			};

			AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
			{
				DebugConsole.LogError($"[AppDomain] Unhandled exception: {args.ExceptionObject}");
			};
        }

        void LoadNetworkRelay()
		{
			int relay = Configuration.Instance.Host.NetworkTransport;
			NetworkConfig.UpdateTransport((NetworkConfig.NetworkTransport)relay);

			//version checker that doesnt restart the game
			var VersionChecker = new PVersionCheck();
			VersionChecker.Register(this, new SteamVersionChecker());

		}

		void LoadAssetBundles()
		{
			using var _ = Profiler.Scope();

			// Load custom asset bundles
			string cursor_bundle = GetBundleBasedOnPlatform("ONI_MP.Assets.bundles.playercursor_win.bundle",
															"ONI_MP.Assets.bundles.playercursor_mac.bundle",
															"ONI_MP.Assets.bundles.playercursor_lin.bundle");
			LoadAssetBundle("playercursorbundle", cursor_bundle);

            string network_indicators = GetBundleBasedOnPlatform("ONI_MP.Assets.bundles.networkindicators_win.bundle",
																 "ONI_MP.Assets.bundles.networkindicators_mac.bundle",
																 "ONI_MP.Assets.bundles.networkindicators_lin.bundle");
            LoadAssetBundle("networkindicators", network_indicators);
        }

		private void SetupListeners()
		{
			using var _ = Profiler.Scope();

			App.OnPostLoadScene += () =>
			{
				OnPostSceneLoaded?.Invoke();
			};

			ReadyManager.SetupListeners();
		}
		public static AssetBundle LoadAssetBundle(string bundleKey, string resourceName)
		{
			using var _ = Profiler.Scope();

			if (LoadedBundles.TryGetValue(bundleKey, out var bundle))
			{
				DebugConsole.Log($"LoadAssetBundle: Reusing cached AssetBundle '{bundleKey}'.");
				return bundle;
			}

			// load with your existing loader
			bundle = ResourceLoader.LoadEmbeddedAssetBundle(resourceName);

			if (bundle != null)
			{
				LoadedBundles[bundleKey] = bundle;
				DebugConsole.LogSuccess($"LoadAssetBundle: Successfully loaded AssetBundle '{bundleKey}' from resource '{resourceName}'.");

				foreach (var name in bundle.GetAllAssetNames())
				{
					DebugConsole.LogAssert($"[ONI_MP] Bundle Asset: {name}");
				}

				foreach (var name in bundle.GetAllScenePaths())
				{
					DebugConsole.LogAssert($"[ONI_MP] Scene: {name}");
				}

				foreach (var name in bundle.GetAllAssetNames())
				{
					DebugConsole.LogAssert($"[ONI_MP] Asset: {name}");
				}
				return bundle;
			}
			else
			{
				DebugConsole.LogError($"LoadAssetBundle: Could not load AssetBundle from resource '{resourceName}'");
				return null;
			}
		}

		public string GetBundleBasedOnPlatform(string windows_bundle, string mac_bundle, string linux_bundle)
		{
			using var _ = Profiler.Scope();

			switch (Application.platform)
			{
				case RuntimePlatform.OSXPlayer:
					return mac_bundle;
				case RuntimePlatform.LinuxPlayer:
					return linux_bundle;
				default:
					return windows_bundle;
			}
		}

		private static void RegisterDevTools()
		{
			using var _ = Profiler.Scope();

#if DEBUG // DevTool is not accessible on mac.
			var baseMethod = AccessTools.Method(typeof(DevToolManager), "RegisterDevTool");
			var twitchDevToolRegister = baseMethod.MakeGenericMethod(typeof(DevToolMultiplayer));
			twitchDevToolRegister.Invoke(DevToolManager.Instance, new object[] { "Mods/MultiplayerMod" });
			DevToolManager.Instance.showImGui = true;
#endif
		}

        public override void OnAllModsLoaded(Harmony harmony, IReadOnlyList<Mod> mods)
        {
	        using var _ = Profiler.Scope();

            base.OnAllModsLoaded(harmony, mods);
			//does weird force restarts; replaced with plib version checker that doesnt restart the game
			//ModUpdater.Updater.CheckForUpdate();

#if DEBUG
			UnitTestRegistry.DiscoverTests();
			ProtocolCompatibility.BypassChecks = true; // If DEBUG bypass by default
#endif
			// For now default to the steam transport
			NetworkConfig.UpdateTransport(NetworkConfig.NetworkTransport.STEAMWORKS);
		}
	}
}
