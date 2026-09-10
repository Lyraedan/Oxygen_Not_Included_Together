using HarmonyLib;
using Newtonsoft.Json;
using ONI_Together.Misc;
using ONI_Together.Misc.World;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Tools.Build;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ONI_Together.DebugTools
{
	/// <summary>
	/// Scripted test rig, inactive unless the process was started with the environment
	/// variable ONI_TOGETHER_TEST_DIR. It exists so that a host and a client can run on
	/// one machine, be driven from a script and compared without anyone clicking:
	///  - ONI_TOGETHER_KLEI_ROOT: replaces Documents\Klei for save files, prefs and mods,
	///    so a second game instance never touches the real profile.
	///  - ONI_TOGETHER_AUTO: "host:<save path>[:<ip>:<port>]" loads that save and starts a
	///    LAN server from the main menu; "join:<ip>:<port>" connects as a client.
	///  - <TEST_DIR>\commands.txt is polled twice a second; every new line is a command
	///    (see Execute) and its result is appended to <TEST_DIR>\results.txt. Dumps of a
	///    world region go to <TEST_DIR>\dump_<name>.json so the two sides can be diffed.
	/// Nothing here runs for ordinary players.
	/// </summary>
	internal static class TestHarness
	{
		internal static readonly string Dir = Environment.GetEnvironmentVariable("ONI_TOGETHER_TEST_DIR");
		internal static readonly string KleiRoot = Environment.GetEnvironmentVariable("ONI_TOGETHER_KLEI_ROOT");
		internal static readonly string Auto = Environment.GetEnvironmentVariable("ONI_TOGETHER_AUTO");
		internal static bool Enabled => !string.IsNullOrEmpty(Dir);

		/// <summary>ONI_TOGETHER_TEST_LOSS: share (0..1) of outgoing unreliable datagrams to drop; simulates a lossy link.</summary>
		internal static readonly float DropUnreliableChance = ParseLoss(Environment.GetEnvironmentVariable("ONI_TOGETHER_TEST_LOSS"));
		private static int _droppedUnreliable;
		internal static void CountDroppedUnreliable() => _droppedUnreliable++;

		private static float ParseLoss(string text)
		{
			return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? Mathf.Clamp01(value) : 0f;
		}

		private static long _consumed;
		private static float _nextPoll;
		private static bool _autoDone;

		/// <summary>
		/// The profile redirect starts at the main menu, not at mod load: KMod.Manager
		/// clears mods.json's mod_load_in_progress flag right after the mods loaded, and
		/// with the redirect already active that write landed in the test profile while
		/// the real file kept "true", so the next instance booted in mod safe mode.
		/// </summary>
		internal static bool RedirectActive;
		private static string CommandsPath => Path.Combine(Dir, "commands.txt");
		private static string ResultsPath => Path.Combine(Dir, "results.txt");

		internal static void Init(GameObject modules)
		{
			if (!Enabled) return;
			try
			{
				Directory.CreateDirectory(Dir);
				modules.AddComponent<TestHarnessBehaviour>();
				Result($"harness ready; kleiRoot={KleiRoot}; auto={Auto}");
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[TestHarness] Init failed: {ex}");
			}
		}

		internal static void OnMainMenu()
		{
			if (!Enabled) return;
			RedirectActive = !string.IsNullOrEmpty(KleiRoot);
			if (_autoDone || string.IsNullOrEmpty(Auto)) return;
			_autoDone = true;
			try
			{
				// One frame later: MainMenu.OnSpawn is still on the stack.
				CoroutineRunner.RunOne(AutoFlow());
			}
			catch (Exception ex)
			{
				Result($"auto failed: {ex}");
			}
		}

		private static System.Collections.IEnumerator AutoFlow()
		{
			yield return new WaitForSecondsRealtime(2f);
			// host|<save path>|<ip>|<port>   or   join|<ip>|<port>
			var parts = Auto.Split('|');
			if (parts[0] == "host" && parts.Length >= 2)
			{
				string save = parts[1];
				string ip = parts.Length >= 3 ? parts[2] : "127.0.0.1";
				int port = parts.Length >= 4 ? int.Parse(parts[3]) : 8090;
				Configuration.Instance.Host.LanSettings.Ip = ip;
				Configuration.Instance.Host.LanSettings.Port = port;
				Configuration.Instance.Host.LanSettings.Transport = LanTransportType.LiteNetLib;
				NetworkConfig.UpdateLanTransport();
				MultiplayerSession.ShouldHostAfterLoad = true;
				KCrashReporter.MOST_RECENT_SAVEFILE = save;
				SaveLoader.SetActiveSaveFilePath(save);
				Result($"auto host: loading {save}, will listen on {ip}:{port}");
				App.LoadScene("backend");
			}
			else if (parts[0] == "join" && parts.Length >= 3)
			{
				string ip = parts[1];
				int port = int.Parse(parts[2]);
				Configuration.Instance.Host.LanSettings.Transport = LanTransportType.LiteNetLib;
				NetworkConfig.UpdateLanTransport();
				Configuration.Instance.Client.LanSettings.Ip = ip;
				Configuration.Instance.Client.LanSettings.Port = port;
				Result($"auto join: {ip}:{port}");
				GameClient.ConnectToHost(ip: ip, port: port);
			}
			else
			{
				Result($"auto: unrecognised '{Auto}'");
			}
		}

		internal static void Tick()
		{
			if (Time.realtimeSinceStartup < _nextPoll) return;
			_nextPoll = Time.realtimeSinceStartup + 0.5f;
			try
			{
				if (!File.Exists(CommandsPath)) return;
				string[] lines;
				using (var fs = new FileStream(CommandsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					if (fs.Length <= _consumed) return;
					fs.Seek(_consumed, SeekOrigin.Begin);
					using var reader = new StreamReader(fs, Encoding.UTF8);
					string text = reader.ReadToEnd();
					_consumed = fs.Length;
					lines = text.Split('\n');
				}
				foreach (var raw in lines)
				{
					string line = raw.Trim();
					if (line.Length == 0 || line.StartsWith("#")) continue;
					string result;
					try { result = Execute(line); }
					catch (Exception ex) { result = "EXCEPTION " + ex; }
					Result($"{line} => {result}");
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[TestHarness] Tick failed: {ex}");
			}
		}

		private static void Result(string text)
		{
			string line = $"[{System.DateTime.UtcNow:HH:mm:ss.fff}] {text}";
			DebugConsole.Log("[TestHarness] " + text);
			try { File.AppendAllText(ResultsPath, line + "\n"); } catch { }
		}

		/// <summary>
		/// Commands (cells are Grid cell indices; "x,y" is accepted too):
		///  dig CELL | cancel CELL | deconstruct CELL | sweep CELL | sweepitem CELL | unsweepitem CELL
		///  build PREFAB CELL ELEMENT [ORIENTATION 0-3] | prio CELL VALUE | speed 0-3 | pause | unpause
		///  camera CELL | dump NAME X0 Y0 X1 Y1 | dupes NAME | hardsync | fps N
		///  log TEXT | status | xy CELL | dumpc NAME CELL RADIUS | items CELL | quit
		/// </summary>
		private static string Execute(string line)
		{
			var a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			string cmd = a[0].ToLowerInvariant();
			switch (cmd)
			{
				case "seed":
					return $"seed={CustomGameSettings.Instance?.GetCurrentWorldgenSeed()} save={SaveGame.Instance?.BaseName} world={ClusterManager.Instance?.activeWorld?.name} grid={Grid.WidthInCells}x{Grid.HeightInCells}";
				case "status":
					return $"host={MultiplayerSession.IsHost} loss={DropUnreliableChance} dropped={_droppedUnreliable} inSession={MultiplayerSession.InActiveSession} client={GameClient.State} players={MultiplayerSession.PlayerCount} inGame={Utils.IsInGame()} speed={SpeedControlScreen.Instance?.GetSpeed()} paused={SpeedControlScreen.Instance?.IsPaused} cycle={GameClock.Instance?.GetCycle()} time={GameClock.Instance?.GetTime():F1} ids={NetworkIdentityRegistry.Count} grid={Grid.WidthInCells}x{Grid.HeightInCells}";
				case "xy":
				{
					int cell = ParseCell(a[1]);
					Grid.CellToXY(cell, out int x, out int y);
					return $"cell {cell} = {x},{y}";
				}
				case "census":
				{
					var byPrefab = new SortedDictionary<string, int[]>();
					int total = 0, local = 0;
					foreach (var identity in NetworkIdentityRegistry.AllIdentities)
					{
						if (identity == null) continue;
						string prefab;
						bool pickupable;
						try { prefab = identity.gameObject.PrefabID().ToString(); pickupable = identity.GetComponent<Pickupable>() != null; }
						catch { continue; }
						total++;
						if (!byPrefab.TryGetValue(prefab, out var counts)) byPrefab[prefab] = counts = new int[3];
						counts[0]++;
						// the local-vs-host origin column needs NetworkIdentity.HostAssigned (identity PR); left at 0 here
						if (pickupable) counts[2]++;
					}
					return WriteJson("census_" + a[1], new Dictionary<string, object>
					{
						["host"] = MultiplayerSession.IsHost,
						["cycle"] = GameClock.Instance?.GetCycle(),
						["time"] = GameClock.Instance != null ? Math.Round(GameClock.Instance.GetTime(), 1) : 0,
						["ids"] = total,
						["local"] = local,
						["pending"] = 0, // PendingRemovals arrives with the identity PR
						["prefabs"] = byPrefab,
					});
				}
				case "dumpc":
				{
					int cell = ParseCell(a[2]);
					int radius = int.Parse(a[3]);
					Grid.CellToXY(cell, out int x, out int y);
					return WriteJson("dump_" + a[1], DumpRegion(x - radius, y - radius, x + radius, y + radius));
				}
				case "items":
				{
					int cell = ParseCell(a[1]);
					var sb = new StringBuilder();
					foreach (var go in ItemsAt(cell))
					{
						var clearable = go.GetComponent<Clearable>();
						sb.Append(Describe(go)).Append(clearable != null && clearable.isMarkedForClear ? " [sweep]" : "").Append("; ");
					}
					return sb.Length > 0 ? sb.ToString() : "no items";
				}
				case "log":
					return "logged";
				case "quit":
					App.Quit();
					return "quitting";
				case "fps":
					Application.targetFrameRate = int.Parse(a[1]);
					return $"targetFrameRate={Application.targetFrameRate}";
				case "speed":
					SpeedControlScreen.Instance.SetSpeed(int.Parse(a[1]));
					return $"speed={SpeedControlScreen.Instance.GetSpeed()}";
				case "pause":
					SpeedControlScreen.Instance.Pause(false);
					return "paused";
				case "unpause":
					SpeedControlScreen.Instance.Unpause(false);
					return "unpaused";
				case "camera":
				{
					int cell = ParseCell(a[1]);
					float size = a.Length > 2 ? float.Parse(a[2]) : 12f;
					CameraController.Instance.SetTargetPos(Grid.CellToPosCCC(cell, Grid.SceneLayer.Move), size, false);
					return $"camera -> {cell}";
				}
				case "dig":
				{
					int cell = ParseCell(a[1]);
					var go = DigTool.PlaceDig(cell, 0);
					return go != null ? $"diggable {Describe(go)}" : "nothing placed";
				}
				case "cancel":
					CancelTool.Instance.OnDragTool(ParseCell(a[1]), 0);
					return "cancel sent";
				case "deconstruct":
					DeconstructTool.Instance.OnDragTool(ParseCell(a[1]), 0);
					return "deconstruct sent";
				case "sweep":
					ClearTool.Instance.OnDragTool(ParseCell(a[1]), 0);
					return "sweep sent";
				case "sweepitem":
				case "unsweepitem":
				{
					int cell = ParseCell(a[1]);
					var items = ItemsAt(cell);
					if (items.Count == 0) return "no item at cell";
					var clearable = items[0].GetComponent<Clearable>();
					if (clearable == null) return "first item has no Clearable";
					if (cmd == "sweepitem") clearable.MarkForClear(false, false); else clearable.CancelClearing();
					return $"{cmd} {Describe(items[0])}";
				}
				case "prio":
				{
					int cell = ParseCell(a[1]);
					int value = int.Parse(a[2]);
					var go = Grid.Objects[cell, (int)ObjectLayer.Building] ?? Grid.Objects[cell, (int)ObjectLayer.DigPlacer] ?? Grid.Objects[cell, (int)ObjectLayer.FoundationTile];
					var prioritizable = go != null ? go.GetComponent<Prioritizable>() : null;
					if (prioritizable == null) return "nothing prioritizable at cell";
					prioritizable.SetMasterPriority(new PrioritySetting(PriorityScreen.PriorityClass.basic, value));
					return $"priority {value} on {Describe(go)}";
				}
				case "build":
				{
					// The path a click takes: BuildTool.Activate creates the visualizer and
					// TryBuild places, so whatever the mod patches on the tool runs as well.
					string prefab = a[1];
					int cell = ParseCell(a[2]);
					var def = Assets.GetBuildingDef(prefab);
					if (def == null) return $"unknown def {prefab}";
					var elements = new List<Tag> { TagManager.Create(a[3]) };
					var orientation = a.Length > 4 ? (Orientation)int.Parse(a[4]) : Orientation.Neutral;
					var tool = BuildTool.Instance;
					if (tool == null) return "no BuildTool";
					tool.Activate(def, elements);
					tool.buildingOrientation = orientation;
					tool.lastDragCell = -1;
					tool.TryBuild(cell);
					var built = Grid.Objects[cell, (int)def.ObjectLayer];
					if (built == null && def.ReplacementLayer != ObjectLayer.NumLayers) built = Grid.Objects[cell, (int)def.ReplacementLayer];
					PlayerController.Instance.ActivateTool(SelectTool.Instance);
					return built != null ? $"placed {Describe(built)}" : "nothing at cell after TryBuild (invalid location?)";
				}
				case "buildraw":
				{
					// Places without the tool and sends the packet by hand; kept for branches where TryBuild is not patched.
					string prefab = a[1];
					int cell = ParseCell(a[2]);
					var def = Assets.GetBuildingDef(prefab);
					if (def == null) return $"unknown def {prefab}";
					var elements = new List<Tag> { TagManager.Create(a[3]) };
					var orientation = a.Length > 4 ? (Orientation)int.Parse(a[4]) : Orientation.Neutral;
					Vector3 pos = Grid.CellToPosCBC(cell, Grid.SceneLayer.Building);
					GameObject built;
					if (def.ReplacementLayer != ObjectLayer.NumLayers && def.GetReplacementCandidate(cell) != null && !def.IsReplacementLayerOccupied(cell))
					{
						var visualizer = Util.KInstantiate(def.BuildingPreview, pos);
						built = def.TryReplaceTile(visualizer, pos, orientation, elements, "DEFAULT_FACADE");
						if (built != null) Grid.Objects[cell, (int)def.ReplacementLayer] = built;
					}
					else
					{
						var visualizer = Util.KInstantiate(def.BuildingPreview, pos);
						built = def.TryPlace(visualizer, pos, orientation, elements, "DEFAULT_FACADE");
					}
					if (built == null) return "TryPlace returned null (invalid location?)";
					// What BuildToolPatch does after BuildTool.TryBuild placed something.
					DebugConsole.Log($"[BuildTool] Placed {def.PrefabID} at cell {cell}");
					PacketSender.SendToAllOtherPeers(new BuildPacket(def.PrefabID, cell, orientation, elements, def.ObjectLayer, false));
					return $"placed {Describe(built)}";
				}
				case "hardsync":
					GameServerHardSync.PerformHardSync();
					return "hard sync requested";
				case "dupes":
					return WriteJson("dupes_" + a[1], DumpDupes());
				case "dump":
					return WriteJson("dump_" + a[1], DumpRegion(int.Parse(a[2]), int.Parse(a[3]), int.Parse(a[4]), int.Parse(a[5])));
				default:
					return "unknown command";
			}
		}

		private static int ParseCell(string s)
		{
			if (s.Contains(","))
			{
				var xy = s.Split(',');
				return Grid.XYToCell(int.Parse(xy[0]), int.Parse(xy[1]));
			}
			return int.Parse(s);
		}

		private static string Describe(GameObject go)
		{
			if (go == null) return "null";
			var identity = go.GetExistingNetIdentity();
			return $"{go.name} cell={Grid.PosToCell(go)} netId={(identity != null ? identity.NetId : 0)}";
		}

		private static List<GameObject> ItemsAt(int cell)
		{
			var result = new List<GameObject>();
			var head = Grid.Objects[cell, (int)ObjectLayer.Pickupables];
			var item = head != null ? head.GetComponent<Pickupable>()?.objectLayerListItem : null;
			int guard = 0;
			while (item != null && guard++ < 10000)
			{
				if (item.gameObject != null) result.Add(item.gameObject);
				item = item.nextItem;
			}
			return result;
		}

		private static string WriteJson(string name, object data)
		{
			string path = Path.Combine(Dir, name + ".json");
			File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
			return path;
		}

		private static readonly ObjectLayer[] DumpLayers =
		{
			ObjectLayer.Building, ObjectLayer.FoundationTile, ObjectLayer.ReplacementTile, ObjectLayer.DigPlacer,
			ObjectLayer.Wire, ObjectLayer.LiquidConduit, ObjectLayer.GasConduit, ObjectLayer.LogicWire,
			ObjectLayer.Backwall, ObjectLayer.Plants,
		};

		/// <summary>Per cell: element, mass, the object on each structural layer, the items lying there.</summary>
		private static object DumpRegion(int x0, int y0, int x1, int y1)
		{
			var cells = new List<object>();
			for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
			for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
			{
				int cell = Grid.XYToCell(x, y);
				if (!Grid.IsValidCell(cell)) continue;
				var entry = new Dictionary<string, object>
				{
					["cell"] = cell,
					["el"] = Grid.Element[cell].id.ToString(),
					["mass"] = Math.Round(Grid.Mass[cell], 1),
					["solid"] = Grid.Solid[cell],
				};
				foreach (var layer in DumpLayers)
				{
					var go = Grid.Objects[cell, (int)layer];
					if (go == null) continue;
					var obj = new Dictionary<string, object> { ["prefab"] = go.PrefabID().ToString() };
					var identity = go.GetExistingNetIdentity();
					if (identity != null) obj["netId"] = identity.NetId;
					if (go.GetComponent<Constructable>() != null) obj["state"] = "constructable";
					else if (go.GetComponent<BuildingComplete>() != null) obj["state"] = "complete";
					var prioritizable = go.GetComponent<Prioritizable>();
					if (prioritizable != null) obj["prio"] = prioritizable.GetMasterPriority().priority_value;
					var deconstructable = go.GetComponent<Deconstructable>();
					if (deconstructable != null && deconstructable.IsMarkedForDeconstruction()) obj["deconstruct"] = true;
					var primary = go.GetComponent<PrimaryElement>();
					if (primary != null) { obj["pel"] = primary.ElementID.ToString(); obj["pmass"] = Math.Round(primary.Mass, 1); }
					entry[layer.ToString()] = obj;
				}
				var items = ItemsAt(cell);
				if (items.Count > 0)
				{
					var list = new List<object>();
					foreach (var go in items)
					{
						var obj = new Dictionary<string, object> { ["prefab"] = go.PrefabID().ToString() };
						var identity = go.GetExistingNetIdentity();
						if (identity != null) obj["netId"] = identity.NetId;
						var primary = go.GetComponent<PrimaryElement>();
						if (primary != null) obj["mass"] = Math.Round(primary.Mass, 2);
						var clearable = go.GetComponent<Clearable>();
						if (clearable != null && clearable.isMarkedForClear) obj["sweep"] = true;
						list.Add(obj);
					}
					entry["items"] = list;
				}
				cells.Add(entry);
			}
			return new Dictionary<string, object>
			{
				["host"] = MultiplayerSession.IsHost,
				["cycle"] = GameClock.Instance?.GetCycle(),
				["time"] = GameClock.Instance != null ? Math.Round(GameClock.Instance.GetTime(), 1) : 0,
				["ids"] = NetworkIdentityRegistry.Count,
				["cells"] = cells,
			};
		}

		private static object DumpDupes()
		{
			var list = new List<object>();
			foreach (var identity in global::Components.MinionIdentities.Items)
			{
				if (identity == null) continue;
				var go = identity.gameObject;
				var netIdentity = go.GetExistingNetIdentity();
				var chore = identity.GetComponent<ChoreDriver>()?.GetCurrentChore();
				list.Add(new Dictionary<string, object>
				{
					["name"] = identity.GetProperName(),
					["netId"] = netIdentity != null ? netIdentity.NetId : 0,
					["cell"] = Grid.PosToCell(go),
					["chore"] = chore != null ? chore.GetType().Name : "none",
					["target"] = chore?.target != null ? Grid.PosToCell(chore.target.gameObject) : -1,
				});
			}
			return list;
		}
	}

	internal class TestHarnessBehaviour : MonoBehaviour
	{
		private void Update()
		{
			TestHarness.Tick();
		}
	}

	[HarmonyPatch(typeof(Util), nameof(Util.GetKleiRootPath))]
	internal static class TestHarness_KleiRootPatch
	{
		private static bool Prefix(ref string __result)
		{
			if (!TestHarness.RedirectActive || string.IsNullOrEmpty(TestHarness.KleiRoot)) return true;
			__result = TestHarness.KleiRoot;
			return false;
		}
	}

	[HarmonyPatch(typeof(MainMenu), "OnSpawn")]
	internal static class TestHarness_MainMenuPatch
	{
		private static void Postfix()
		{
			TestHarness.OnMainMenu();
		}
	}
}
