using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ONI_Together_DedicatedServer;
using ONI_Together_DedicatedServer.ONI;
using ONI_Together_DedicatedServer.Transports;

namespace ONI_Together.DedicatedServer
{
    public class DedicatedServer
    {
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

        private sealed class LaunchOptions
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
        }

        private static Transports _transport = Transports.LiteNetLib;
        private static DedicatedTransportServer? _server;
        private static SaveFile? _saveFile;
        private static volatile bool _stopRequested;
        private static readonly ManualResetEventSlim _shutdownCompleted = new(false);

        private static readonly Dictionary<string, Command> _commands = new();
        private static readonly Dictionary<string, string> _whatisDictionary = new(StringComparer.OrdinalIgnoreCase)
        {
            { "master", "The master is the primary client controlling the game simulation state." },
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

            ServerConfiguration.InitializePaths(options.ConfigDir, options.SaveDir);
            var config = ServerConfiguration.Instance.Config;
            ApplyCliOverrides(config, options);

            bool nonInteractive = options.NonInteractive
                                  || IsEnvTrue("ONI_NON_INTERACTIVE");

            Console.WriteLine("ONI Together: Dedicated Server starting (LiteNetLib)...");
            Console.WriteLine($"[Config] Directory: {ServerConfiguration.ConfigDirectory}");
            Console.WriteLine($"[Config] Saves:     {ServerConfiguration.SavesDirectory}");

            RegisterCommands();
            RegisterShutdownHooks();

            try
            {
                LoadConfiguredSave(config.SaveFile);

                _transport = (Transports)config.Transport;
                _server = SetupTransport();

                if (_saveFile != null)
                    _server.SetWorldInfo(Path.GetFileNameWithoutExtension(_saveFile.Name));

                if (!_server.Start())
                {
                    Console.WriteLine("[DedicatedServer] Server failed to start.");
                    return 1;
                }

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
                Console.WriteLine($"[DedicatedServer] Fatal error: {ex.Message}");
                return 1;
            }
            finally
            {
                _server?.Stop();
                _shutdownCompleted.Set();
            }
        }

        private static void RunTickLoop()
        {
            var stopwatch = Stopwatch.StartNew();
            while (!_stopRequested && _server != null && _server.IsRunning())
            {
                long tickStartMs = stopwatch.ElapsedMilliseconds;

                _server.Update();

                int tickRate = Math.Clamp(ServerConfiguration.Instance.Config.TickRate, 10, 240);
                int targetFrameMs = Math.Max(1, 1000 / tickRate);
                int elapsedMs = (int)(stopwatch.ElapsedMilliseconds - tickStartMs);
                int sleepMs = targetFrameMs - elapsedMs;
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);
            }
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
                Console.WriteLine("[SaveFile] No initial save configured; waiting for master client state.");
                return;
            }

            if (!File.Exists(resolvedPath))
            {
                _saveFile = null;
                Console.WriteLine($"[SaveFile] Configured save not found at '{resolvedPath}'; starting without initial save.");
                return;
            }

            _saveFile = SaveFile.FromFile(resolvedPath);
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
                    Console.WriteLine($"TickRate: {cfg.TickRate} Hz");
                    Console.WriteLine($"Save:     {_saveFile?.Name ?? "(none)"}");
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
                        Console.WriteLine("Configuration reloaded successfully.");

                        LoadConfiguredSave(cfg.SaveFile);

                        if (_server != null && _server.IsRunning())
                        {
                            Console.WriteLine("Stopping current transport server for hot-reload...");
                            _server.Stop();
                            Thread.Sleep(250);
                        }

                        _transport = (Transports)cfg.Transport;
                        _server = SetupTransport();
                        if (_saveFile != null)
                            _server.SetWorldInfo(Path.GetFileNameWithoutExtension(_saveFile.Name));
                        _server.Start();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to reload configuration: {ex.Message}");
                    }
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

        private static LaunchOptions ParseArgs(string[] args)
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
                    case "--non-interactive":
                    case "--headless":
                        opts.NonInteractive = true;
                        break;
                    case "--verbose":
                        opts.VerbosePackets = true;
                        break;
                    case "--config-dir":
                    case "--data-dir":
                        if (i + 1 < args.Length) opts.ConfigDir = args[++i];
                        break;
                    case "--save-dir":
                        if (i + 1 < args.Length) opts.SaveDir = args[++i];
                        break;
                    case "--ip":
                        if (i + 1 < args.Length) opts.Ip = args[++i];
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
                        if (i + 1 < args.Length) opts.SaveFile = args[++i];
                        break;
                    case "--name":
                    case "--server-name":
                        if (i + 1 < args.Length) opts.ServerName = args[++i];
                        break;
                }
            }
            return opts;
        }

        private static void ApplyCliOverrides(DedicatedConfig config, LaunchOptions opts)
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
            Console.WriteLine("ONI Together Dedicated Server");
            Console.WriteLine("Options:");
            Console.WriteLine("  --config-dir <path>   Override persistent config/data directory");
            Console.WriteLine("  --save-dir <path>     Override save files directory");
            Console.WriteLine("  --ip <address>        Bind IP address (default: 0.0.0.0)");
            Console.WriteLine("  --port <port>         UDP port to listen on (default: 7777)");
            Console.WriteLine("  --max-players <n>     Max connected players (default: 4)");
            Console.WriteLine("  --tick-rate <hz>      Server poll rate in Hz, 10-240 (default: 60)");
            Console.WriteLine("  --server-name <name>  Server name shown in LAN discovery");
            Console.WriteLine("  --save <filename>     Initial .sav file name or path");
            Console.WriteLine("  --non-interactive     Disable interactive stdin console");
            Console.WriteLine("  --verbose             Log relayed packets");
            Console.WriteLine("  -h, --help            Show this help");
        }
    }
}
