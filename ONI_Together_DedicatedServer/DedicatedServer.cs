using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ONI_Together_DedicatedServer;
using ONI_Together_DedicatedServer.ONI;
using ONI_Together_DedicatedServer.Transports;
using Shared.Profiling;

namespace ONI_Together.DedicatedServer
{
    public class DedicatedServer
    {
        public enum Transports
        {
            Riptide = 0
        }

        public static Transports transport = Transports.Riptide;
        private static DedicatedTransportServer? server;
        private static SaveFile? saveFile;

        public struct Command
        {
            public string Name;
            public string Description;
            public System.Action<string[]> Execute;
        }

        private static readonly Dictionary<string, Command> commands = new Dictionary<string, Command>();
        private static readonly Dictionary<string, string> whatisDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "master", "The master is the primary client controlling the game state." },
            { "player", "A player is any connected client." },
            { "savefile", "The save file contains the current state of the game world.\nThe save file is controlled by the master." }
        };
        private static bool stopped = true;

        /// <summary>
        /// THIS IS PURELY EXPERIMENTAL!
        /// This is essentially a listening server, it doesn't run the simulation. It listens for network traffic and relays it to the clients.
        /// It will need to be informed about things like the save file etc.
        /// A save file is uploaded by the host, a client connects and downloads that save file, the first client is considered the master and their state is what overwrites the dedi save
        /// If a save action happens on the master, upload it to the dedi, if the master disconnects with clients present, the next client sends the save state to the dedi and it overwrites it with that one
        ///
        /// This is purely conceptual
        ///
        /// Maybe it'll be better to hold the save file in Memory and use that then only save locally if the server shuts down
        ///
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            using var _ = Profiler.Scope();

            Console.WriteLine("ONI Together: Dedicated Server starting...");

            server = SetupTransport();
            stopped = false;

            RegisterCommands();

            try
            {
                string savePath = Path.Combine(ServerConfiguration.ConfigDirectory, ServerConfiguration.Instance.Config.SaveFile);
                saveFile = SaveFile.FromFile($"{savePath}.sav");
                server.Start();

                Console.WriteLine("\nType \"help\" to view a list of commands.");

                var inputThread = new Thread(ReadConsole)
                {
                    IsBackground = true
                };
                inputThread.Start();

                while (server.IsRunning())
                {
                    using var scope = Profiler.Scope();

                    server.Update();

                    if (stopped)
                    {
                        server.Stop();
                        break;
                    }
                }
            } catch (Exception ex)
            {
                Console.WriteLine($"Failed to start server: {ex.Message}");
            }

            Console.WriteLine("Server stopped. Press Enter to close.");
            Console.ReadLine();
        }

        static void ReadConsole()
        {
            using var _ = Profiler.Scope();

            if (server == null)
                return;

            while (server.IsRunning())
            {
                var line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var cmdName = parts[0].ToLowerInvariant();
                var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

                if (commands.TryGetValue(cmdName, out var command))
                {
                    try
                    {
                        command.Execute.Invoke(args);
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

                if (stopped)
                    break;
            }
        }

        static void RegisterCommands()
        {
            using var _ = Profiler.Scope();

            RegisterCommand(new Command
            {
                Name = "quit",
                Description = "Stops the dedicated server",
                Execute = (args) =>
                {
                    Console.WriteLine("Stopping server...");
                    stopped = true;
                }
            });

            BindExistingCommandTo("stop", "quit");

            RegisterCommand(new Command
            {
                Name = "help",
                Description = "Displays all available commands",
                Execute = (args) =>
                {
                    Console.WriteLine("Available commands:");
                    foreach (var cmd in commands.Values)
                    {
                        Console.WriteLine($" - {cmd.Name} : {cmd.Description}");
                    }
                }
            });

            RegisterCommand(new Command
            {
                Name = "listplayers",
                Description = "Displays a list of all connected clients",
                Execute = (args) =>
                {
                    Console.WriteLine("Connected players:");
                    if (server.GetPlayers().Count == 0)
                    {
                        Console.WriteLine(" - None");
                        return;
                    }
                    foreach(ONI_Together_DedicatedServer.ONI.Player player in server.GetPlayers().Values)
                    {
                        Console.WriteLine($" - [{player.ClientID}{(player.IsMaster ? "/Master" : string.Empty)}] {player.Connection.SmoothRTT}ms");
                    }
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
                        foreach (var key in whatisDictionary.Keys)
                        {
                            Console.WriteLine($" - {key}");
                        }
                    }
                    else if (whatisDictionary.TryGetValue(thing, out var description))
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
                Execute = (args) =>
                {
                    try
                    {
                        ServerConfiguration.Instance.Reload();
                        Console.WriteLine("Configuration reloaded successfully.");

                        string savePath = Path.Combine(ServerConfiguration.ConfigDirectory, ServerConfiguration.Instance.Config.SaveFile);
                        saveFile = SaveFile.FromFile($"{savePath}.sav");
                        Console.WriteLine("Save file reloaded.");

                        try
                        {
                            if (server != null && server.IsRunning())
                            {
                                Console.WriteLine("Stopping current transport server for hot-reload...");
                                server.Stop();
                                Thread.Sleep(500); // wait half a second
                            }

                            transport = (Transports)ServerConfiguration.Instance.Config.Transport;

                            server = SetupTransport();
                            server.Start();
                            Console.WriteLine("Transport server hot-reloaded and started.");
                        }
                        catch (Exception transportEx)
                        {
                            Console.WriteLine($"Failed to hot-reload transport: {transportEx.Message}");
                        }
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
            using var _ = Profiler.Scope();

            commands[command.Name.ToLowerInvariant()] = command;
        }

        public static void BindExistingCommandTo(string newBinding, string commandToBindTo)
        {
            using var _ = Profiler.Scope();

            if (!commands.TryGetValue(commandToBindTo.ToLowerInvariant(), out var existing))
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
            using var _ = Profiler.Scope();

            switch (transport) {
                case Transports.Riptide:
                    return new DedicatedRiptideServer();
                default:
                    return new DedicatedRiptideServer();
            }
        }
    }
}
