using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ONI_Together_DedicatedServer;
using ONI_Together_DedicatedServer.Logging;
using ONI_Together_DedicatedServer.Oni;
using ONI_Together_DedicatedServer.ONI;
using ONI_Together_DedicatedServer.Security;
using ONI_Together_DedicatedServer.Transports;

namespace ONI_Together.DedicatedServer
{
    public class DedicatedServer
    {
        public const string Version = "1.0.0";

        public enum Transports
        {
            LiteNetLib = 0
        }

        public struct Command
        {
            public string Name;
            public string Description;
            public Action<string[]> Execute;
        }

        internal sealed class LaunchOptions
        {
            public string? ConfigDir;
            public string? SaveDir;
            public string? Ip;
            public int? Port;
            public int? MaxPlayers;
            public int? TickRate;
            public string? SaveFile;
            public string? ServerName;
            public bool? VerbosePackets;
            public bool NonInteractive;
            public bool ShowHelp;
            public bool ShowVersion;
            public bool HealthCheck;
        }

        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan HeartbeatMaxAge = TimeSpan.FromSeconds(30);
        internal static string HeartbeatPath => Path.Combine(ServerConfiguration.ConfigDirectory, "heartbeat");

        private static Transports _transport = Transports.LiteNetLib;
        private static ServerLogger _logger = new();
        internal static DedicatedTransportServer? _server;
        internal static SaveFile? _saveFile;
        internal static SaveStore? _saveStore;
        internal static volatile bool _stopRequested;
        private static readonly ManualResetEventSlim _shutdownCompleted = new(false);

        private static readonly Dictionary<string, Command> _commands = new();
        private static readonly Dictionary<string, string> _whatisDictionary = new(StringComparer.OrdinalIgnoreCase)
        {
            { "master", "The master is the first player to connect. Their game hosts and simulates the colony; when they leave, the session ends and the next player hosts from the stored save." },
            { "player", "A player is any connected client." },
            { "savefile", "The save file contains the current state of the game world." }
        };

        static int Main(string[] args)
        {
            var options = ParseArgs(args);

            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            if (options.ShowVersion)
            {
                Console.WriteLine($"ONI Together Dedicated Server {Version} (.NET 8, LiteNetLib 1.3.1)");
                return 0;
            }

            if (options.HealthCheck)
            {
                ServerConfiguration.InitializePaths(options.ConfigDir, options.SaveDir);
                var (healthy, message) = CheckHeartbeat(HeartbeatPath, DateTime.UtcNow, HeartbeatMaxAge);
                Console.WriteLine(message);
                return healthy ? 0 : 1;
            }

            try
            {
                ServerConfiguration.InitializePaths(options.ConfigDir, options.SaveDir);
                var config = ServerConfiguration.Instance.Config;
                ApplyCliOverrides(config, options);
                config.Sanitize();

                bool nonInteractive = options.NonInteractive
                                      || config.NonInteractive
                                      || IsEnvTrue("ONI_NON_INTERACTIVE");

                // Structured console + optional file logging. Never crash on logging.
                _logger = new ServerLogger(
                    config.LogToFile ? Path.Combine(ServerConfiguration.ConfigDirectory, "logs", "server.log") : null,
                    ServerLogger.ParseLevel(config.LogLevel));
                ServerLogger.Current = _logger;

                // Save store rooted at persistent saves dir.
                _saveStore = new SaveStore(
                    ServerConfiguration.SavesDirectory,
                    config.SaveBackupCount,
                    config.MaxSaveBytes);

                _logger.Info($"ONI Together: Dedicated Server {Version} starting (LiteNetLib)...");
                _logger.Info($"[Config] Directory: {ServerConfiguration.ConfigDirectory}");
                _logger.Info($"[Config] Saves:     {ServerConfiguration.SavesDirectory}");
                _logger.Info($"[Config] Port: {config.Port} UDP, MaxPlayers={config.MaxLobbySize}, TickRate={config.TickRate}Hz, MaxPacketSize={config.MaxPacketSize}");
                _logger.Info($"[Config] DLC check: {config.DlcCheckMode}, Discovery={(config.DiscoveryEnabled ? "on" : "off")}, PauseWhenEmpty={config.PauseWhenEmpty}");
                _logger.Info($"[Config] LogToFile={config.LogToFile}, LogLevel={config.LogLevel}");
                // Final milestone investigation: honest ONI runtime detection. NOT headless-authoritative; NOT Klei-verified.
                _logger.Info($"[ONI] {OniRuntimeDetector.Detect().Report()}");

                RegisterCommands();
                RegisterShutdownHooks();

                _saveFile = _saveStore.TryLoadColony(config.SaveFileName);
                if (_saveFile == null)
                    LoadConfiguredSave(config.SaveFile);
                else
                    _logger.Info($"[SaveFile] Continuing stored colony '{_saveFile.Name}'.");

                if (_saveFile != null)
                {
                    var header = SaveHeader.TryRead(_saveFile.Data);
                    _logger.Info(header == null
                        ? "[SaveFile] Could not read the colony's header; players are not matched against its DLC."
                        : $"[SaveFile] Colony DLC: {ColonyDlcCheck.Describe(header.DlcIds)}.");
                }

                _transport = (Transports)config.Transport;
                _server = SetupTransport();
                if (_server is DedicatedLiteNetLibServer relay)
                {
                    relay.ColonyProvider = () => _saveFile;
                    relay.ColonyReceived += colony =>
                    {
                        _saveFile = colony;
                        TrySaveCurrent("upload from host");
                        AnnounceColony();
                    };
                }

                AnnounceColony();

                if (!_server.Start())
                {
                    _logger.Error("[DedicatedServer] Server failed to start.");
                    return 1;
                }

                // Persist initially loaded save into server-owned store.
                TrySaveCurrent("initial");

                if (!nonInteractive)
                {
                    Console.WriteLine("Type \"help\" to view a list of commands.");
                    var inputThread = new Thread(ReadConsole)
                    {
                        IsBackground = true,
                        Name = "DedicatedServer-Console"
                    };
                    inputThread.Start();
                }
                else
                {
                    Console.WriteLine("[DedicatedServer] Running in non-interactive headless mode.");
                }

                RunTickLoop();
                return 0;
            }
            catch (Exception ex)
            {
                ServerLogger.Current.Error($"[DedicatedServer] Fatal error: {ex}");
                return 1;
            }
            finally
            {
                _server?.Stop();
                TryDelete(HeartbeatPath);
                _shutdownCompleted.Set();
            }
        }

        internal static void RunTickLoop()
        {
            var stopwatch = Stopwatch.StartNew();
            long lastAutosaveMs = 0;
            long lastHeartbeatMs = -(long)HeartbeatInterval.TotalMilliseconds;
            bool wasPaused = false;
            while (!_stopRequested && _server != null && _server.IsRunning())
            {
                long tickStartMs = stopwatch.ElapsedMilliseconds;

                if (tickStartMs - lastHeartbeatMs >= HeartbeatInterval.TotalMilliseconds)
                {
                    lastHeartbeatMs = tickStartMs;
                    WriteHeartbeat();
                }

                var cfg = ServerConfiguration.Instance.Config;
                int playerCount = _server.GetPlayers().Count;
                bool paused = DedicatedLiteNetLibServer.ComputePaused(cfg.PauseWhenEmpty, playerCount);

                // PauseWhenEmpty: no packet processing while empty; still poll for joins. Resume on join.
                _server.SetPaused(paused);
                _server.Update();

                int tickRate = Math.Clamp(cfg.TickRate, 10, 240);
                int targetFrameMs = Math.Max(1, 1000 / tickRate);

                if (paused)
                {
                    if (!wasPaused)
                    {
                        wasPaused = true;
                        ServerLogger.Current.Info("[DedicatedServer] No players connected; paused (PauseWhenEmpty).");
                        TrySaveCurrent("paused-empty");
                    }
                    // no autosave / no relay processing while paused
                    int elapsedPausedMs = (int)(stopwatch.ElapsedMilliseconds - tickStartMs);
                    int sleepPausedMs = targetFrameMs - elapsedPausedMs;
                    if (sleepPausedMs > 0) Thread.Sleep(sleepPausedMs);
                    continue;
                }

                if (wasPaused)
                {
                    wasPaused = false;
                    ServerLogger.Current.Info("[DedicatedServer] Player joined; resumed (PauseWhenEmpty).");
                }

                int elapsedMs = (int)(stopwatch.ElapsedMilliseconds - tickStartMs);
                int sleepMs = targetFrameMs - elapsedMs;
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);

                // Server-owned autosave (only persists server-held bytes; not authoritative ONI sim).
                int autosaveSeconds = cfg.SaveAutosaveSeconds;
                if (autosaveSeconds > 0 && _saveFile != null && _saveStore != null)
                {
                    long now = stopwatch.ElapsedMilliseconds;
                    if (now - lastAutosaveMs >= autosaveSeconds * 1000L)
                    {
                        lastAutosaveMs = now;
                        TrySaveCurrent("[autosave]");
                    }
                }
            }
        }

        private static void AnnounceColony()
        {
            if (_saveFile == null || _server == null)
                return;
            var header = SaveHeader.TryRead(_saveFile.Data);
            _server.SetWorldInfo(Path.GetFileNameWithoutExtension(_saveFile.Name), header?.DisplayCycle ?? 1);
        }

        private static void WriteHeartbeat()
        {
            try
            {
                File.WriteAllText(HeartbeatPath, DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                ServerLogger.Current.Debug($"[DedicatedServer] Could not write heartbeat: {ex.Message}");
            }
        }

        internal static (bool healthy, string message) CheckHeartbeat(string path, DateTime nowUtc, TimeSpan maxAge)
        {
            try
            {
                if (!File.Exists(path))
                    return (false, "unhealthy: no server is running (no heartbeat)");

                string text = File.ReadAllText(path).Trim();
                if (!long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long ticks))
                    return (false, "unhealthy: heartbeat unreadable");

                TimeSpan age = nowUtc - new DateTime(ticks, DateTimeKind.Utc);
                return age <= maxAge
                    ? (true, "ok")
                    : (false, $"unhealthy: last heartbeat {age.TotalSeconds:0}s ago");
            }
            catch (Exception ex)
            {
                return (false, $"unhealthy: {ex.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void RegisterShutdownHooks()
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n[DedicatedServer] Received interrupt signal (SIGINT), shutting down...");
                RequestStop();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                if (!_stopRequested)
                {
                    Console.WriteLine("[DedicatedServer] Received process exit signal (SIGTERM), shutting down...");
                    RequestStop();
                    _shutdownCompleted.Wait(TimeSpan.FromSeconds(5));
                }
            };
        }

        private static void RequestStop()
        {
            _stopRequested = true;
        }

        private static void LoadConfiguredSave(string? saveFileName)
        {
            string? resolvedPath = ServerConfiguration.ResolveSaveFilePath(saveFileName);
            if (resolvedPath == null)
            {
                _saveFile = null;
                ServerLogger.Current.Warn("[SaveFile] No colony stored and no initial save configured. Pass --save <file.sav> once.");
                return;
            }

            if (!File.Exists(resolvedPath))
            {
                _saveFile = null;
                ServerLogger.Current.Warn($"[SaveFile] Configured save not found at '{resolvedPath}'; starting without initial save.");
                return;
            }

            try
            {
                _saveFile = SaveFile.FromFile(resolvedPath, ServerConfiguration.Instance.Config.MaxSaveBytes);
            }
            catch (Exception ex)
            {
                _saveFile = null;
                ServerLogger.Current.Error($"[SaveFile] Failed to load save '{resolvedPath}': {ex.Message}; starting without initial save.");
            }
        }

        internal static void TrySaveCurrent(string reason)
        {
            if (_saveFile == null || _saveStore == null) return;
            try
            {
                string cfgName = ServerConfiguration.Instance.Config.SaveFileName;
                string path = _saveStore.SaveCurrent(_saveFile.Data, cfgName);
                _saveStore.WriteColonyName(cfgName, Path.GetFileNameWithoutExtension(_saveFile.Name));
                if (_saveStore.LastSaveUnchanged)
                    ServerLogger.Current.Debug($"[SaveFile] Unchanged, nothing written ({reason}).");
                else
                    ServerLogger.Current.Info($"[SaveFile] Saved ({reason}): {path}");
            }
            catch (Exception ex)
            {
                ServerLogger.Current.Error($"[SaveFile] Save failed ({reason}): {ex.Message}");
            }
        }

        private static void ReadConsole()
        {
            while (!_stopRequested && _server != null && _server.IsRunning())
            {
                string? line = Console.ReadLine();
                if (line == null)
                    break; // EOF on stdin (pipe closed / headless container)

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Cap console line length to avoid abuse.
                if (line.Length > 8192)
                    line = line.Substring(0, 8192);

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string cmdName = parts[0].ToLowerInvariant();
                string[] cmdArgs = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

                if (_commands.TryGetValue(cmdName, out var command))
                {
                    try
                    {
                        command.Execute.Invoke(cmdArgs);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error executing command '{cmdName}': {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Unknown command: {cmdName}");
                }
            }
        }

        private static void RegisterCommands()
        {
            RegisterCommand(new Command
            {
                Name = "quit",
                Description = "Stops the dedicated server",
                Execute = _ =>
                {
                    Console.WriteLine("Stopping server...");
                    RequestStop();
                }
            });

            BindExistingCommandTo("stop", "quit");

            RegisterCommand(new Command
            {
                Name = "help",
                Description = "Displays all available commands",
                Execute = _ =>
                {
                    Console.WriteLine("Available commands:");
                    foreach (var cmd in _commands.Values)
                        Console.WriteLine($" - {cmd.Name} : {cmd.Description}");
                }
            });

            RegisterCommand(new Command
            {
                Name = "players",
                Description = "Displays a list of all connected clients",
                Execute = _ =>
                {
                    var players = _server?.GetPlayers();
                    if (players == null || players.Count == 0)
                    {
                        Console.WriteLine("Connected players: None");
                        return;
                    }

                    Console.WriteLine($"Connected players ({players.Count}):");
                    foreach (var player in players.Values)
                        Console.WriteLine($" - [{player.ClientID}{(player.IsMaster ? "/Master" : string.Empty)}] {player.PingMs}ms");
                }
            });

            BindExistingCommandTo("listplayers", "players");

            RegisterCommand(new Command
            {
                Name = "status",
                Description = "Displays server status and configuration summary",
                Execute = _ =>
                {
                    var cfg = ServerConfiguration.Instance.Config;
                    int count = _server?.GetPlayers().Count ?? 0;
                    Console.WriteLine($"Server:   {cfg.ServerName}");
                    Console.WriteLine($"Address:  {cfg.Ip}:{cfg.Port}");
                    Console.WriteLine($"Players:  {count}/{cfg.MaxLobbySize}");
                    if (_server is DedicatedLiteNetLibServer relay)
                        Console.WriteLine($"Host:     {relay.HostStatus}");
                    var header = _saveFile == null ? null : SaveHeader.TryRead(_saveFile.Data);
                    if (header != null)
                        Console.WriteLine($"Colony:   {header.BaseName}, cycle {header.DisplayCycle}, {header.Duplicants} duplicants, {ColonyDlcCheck.Describe(header.DlcIds)}{(cfg.MatchColonyDlc ? "" : " (DLC match off)")}");
                    Console.WriteLine($"TickRate: {cfg.TickRate} Hz");
                    Console.WriteLine($"Save:     {_saveFile?.Name ?? "(none)"}");
                    Console.WriteLine($"Server-owned saves: {_saveStore?.SavesDirectory ?? "(none)"}");
                    Console.WriteLine($"PauseWhenEmpty: {cfg.PauseWhenEmpty}; DlcCheckMode: {cfg.DlcCheckMode}");
                    Console.WriteLine($"MaxPacketSize: {cfg.MaxPacketSize}; RateLimit: {cfg.RateLimitMessagesPerSecond}/s");
                }
            });

            RegisterCommand(new Command
            {
                Name = "whatis",
                Description = "Displays information about a given thing. Usage: whatis <thing>",
                Execute = args =>
                {
                    if (args.Length == 0)
                    {
                        Console.WriteLine("Usage: whatis <thing>");
                        return;
                    }

                    string thing = string.Join(' ', args);
                    if (thing.Equals("list", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Available things you can 'whatis':");
                        foreach (var key in _whatisDictionary.Keys)
                            Console.WriteLine($" - {key}");
                    }
                    else if (_whatisDictionary.TryGetValue(thing, out var description))
                    {
                        Console.WriteLine(description);
                    }
                    else
                    {
                        Console.WriteLine($"I don't know anything about \"{thing}\"");
                    }
                }
            });

            RegisterCommand(new Command
            {
                Name = "reloadconfig",
                Description = "Reloads the server configuration from disk at runtime",
                Execute = _ =>
                {
                    try
                    {
                        ServerConfiguration.Instance.Reload();
                        var cfg = ServerConfiguration.Instance.Config;
                        cfg.Sanitize();
                        ServerLogger.Current.Info("Configuration reloaded successfully.");

                        if (_saveFile == null)
                            LoadConfiguredSave(cfg.SaveFile);

                        // Re-read live config WITHOUT restarting transport or dropping connected players.
                        // Ip/Port/Transport changes require a manual restart (not applied here).
                        _transport = (Transports)cfg.Transport;
                        if (_server != null && _server.IsRunning())
                        {
                            _server.ReloadConfiguration();
                            AnnounceColony();
                            ServerLogger.Current.Info("Live configuration applied; connected players preserved.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to reload configuration: {ex.Message}");
                    }
                }
            });

            // Server-owned save persistence commands.
            RegisterCommand(new Command
            {
                Name = "save",
                Description = "Persist server-owned save to saves/current.sav (atomic, rotates backups)",
                Execute = _ => TrySaveCurrent("manual")
            });

            RegisterCommand(new Command
            {
                Name = "saves",
                Description = "List saves in the server-owned saves directory",
                Execute = _ =>
                {
                    if (_saveStore == null) { Console.WriteLine("No save store."); return; }
                    var list = _saveStore.ListSaves();
                    if (list.Count == 0) { Console.WriteLine("No saves."); return; }
                    Console.WriteLine($"Saves in {_saveStore.SavesDirectory}:");
                    foreach (var s in list) Console.WriteLine($" - {s}");
                }
            });

            RegisterCommand(new Command
            {
                Name = "backup",
                Description = "Create a rotated backup of the current server-owned save",
                Execute = _ =>
                {
                    if (_saveStore == null || _saveFile == null) { Console.WriteLine("No save."); return; }
                    try
                    {
                        TrySaveCurrent("backup");
                        _saveStore.RotateBackups(_saveStore.CurrentSavePath);
                        Console.WriteLine("Backup created.");
                    }
                    catch (Exception ex) { Console.WriteLine($"Backup failed: {ex.Message}"); }
                }
            });
        }

        public static void RegisterCommand(Command command)
        {
            _commands[command.Name.ToLowerInvariant()] = command;
        }

        public static void BindExistingCommandTo(string newBinding, string commandToBindTo)
        {
            if (!_commands.TryGetValue(commandToBindTo.ToLowerInvariant(), out var existing))
            {
                Console.WriteLine($"Failed to bind {newBinding} to {commandToBindTo}");
                return;
            }

            RegisterCommand(new Command
            {
                Name = newBinding,
                Description = existing.Description,
                Execute = existing.Execute
            });
        }

        public static DedicatedTransportServer SetupTransport()
        {
            return _transport switch
            {
                Transports.LiteNetLib => new DedicatedLiteNetLibServer(),
                _ => new DedicatedLiteNetLibServer()
            };
        }

        internal static LaunchOptions ParseArgs(string[] args)
        {
            var opts = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--help":
                    case "-h":
                        opts.ShowHelp = true;
                        break;
                    case "--version":
                    case "-v":
                        opts.ShowVersion = true;
                        break;
                    case "--health-check":
                        opts.HealthCheck = true;
                        break;
                    case "--non-interactive":
                    case "--headless":
                        opts.NonInteractive = true;
                        break;
                    case "--verbose":
                        opts.VerbosePackets = true;
                        break;
                    case "--config-dir":
                    case "--data-dir":
                        if (i + 1 < args.Length) opts.ConfigDir = SafeArg(args[++i]);
                        break;
                    case "--save-dir":
                        if (i + 1 < args.Length) opts.SaveDir = SafeArg(args[++i]);
                        break;
                    case "--ip":
                        if (i + 1 < args.Length) opts.Ip = SafeArg(args[++i]);
                        break;
                    case "--port":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int port)) opts.Port = port;
                        break;
                    case "--max-players":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int maxPlayers)) opts.MaxPlayers = maxPlayers;
                        break;
                    case "--tick-rate":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int tickRate)) opts.TickRate = tickRate;
                        break;
                    case "--save":
                        if (i + 1 < args.Length) opts.SaveFile = SafeArg(args[++i]);
                        break;
                    case "--name":
                    case "--server-name":
                        if (i + 1 < args.Length) opts.ServerName = SafeArg(args[++i]);
                        break;
                }
            }
            return opts;
        }

        private static string? SafeArg(string? value)
        {
            if (value == null) return null;
            // Cap length; strip NUL. Keep content otherwise.
            value = value.Replace("\0", "");
            return value.Length > 4096 ? value.Substring(0, 4096) : value;
        }

        internal static void ApplyCliOverrides(DedicatedConfig config, LaunchOptions opts)
        {
            if (!string.IsNullOrWhiteSpace(opts.Ip)) config.Ip = opts.Ip;
            if (opts.Port.HasValue) config.Port = opts.Port.Value;
            if (opts.MaxPlayers.HasValue) config.MaxLobbySize = opts.MaxPlayers.Value;
            if (opts.TickRate.HasValue) config.TickRate = opts.TickRate.Value;
            if (!string.IsNullOrWhiteSpace(opts.SaveFile)) config.SaveFile = opts.SaveFile;
            if (!string.IsNullOrWhiteSpace(opts.ServerName)) config.ServerName = opts.ServerName;
            if (opts.VerbosePackets.HasValue) config.VerbosePacketLogging = opts.VerbosePackets.Value;
        }

        private static bool IsEnvTrue(string name)
        {
            string? val = Environment.GetEnvironmentVariable(name);
            return val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintUsage()
        {
            Console.WriteLine($"ONI Together Dedicated Server {Version}");
            Console.WriteLine("Options:");
            Console.WriteLine("  --config-dir <path>   Override persistent config/data directory");
            Console.WriteLine("  --save-dir <path>     Override save files directory");
            Console.WriteLine("  --ip <address>        Bind IP address (default: 0.0.0.0)");
            Console.WriteLine("  --port <port>         UDP port to listen on (default: 7777)");
            Console.WriteLine("  --max-players <n>     Max connected players (default: 4)");
            Console.WriteLine("  --tick-rate <hz>      Server poll rate in Hz, 10-240 (default: 60)");
            Console.WriteLine("  --server-name <name>  Server name shown in LAN discovery");
            Console.WriteLine("  --save <filename>     Initial .sav file name or path (sandboxed unless allowed)");
            Console.WriteLine("  --non-interactive     Disable interactive stdin console");
            Console.WriteLine("  --headless            Alias for --non-interactive");
            Console.WriteLine("  --verbose             Log relayed packets");
            Console.WriteLine("  --health-check        Print 'ok' and exit 0 (for containers)");
            Console.WriteLine("  --version, -v         Show version");
            Console.WriteLine("  -h, --help            Show this help");
            Console.WriteLine();
            Console.WriteLine("Environment overrides are documented in TODO.md / README.md.");
        }
    }
}
