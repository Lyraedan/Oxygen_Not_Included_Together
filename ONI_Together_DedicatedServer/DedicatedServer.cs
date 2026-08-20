using System;
using System.Collections.Generic;
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
            LiteNetLib = 0
        }

        public static Transports transport = Transports.LiteNetLib;
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

        static void Main(string[] args)
        {
            using var _ = Profiler.Scope();

            Console.WriteLine("ONI Together: Dedicated Server starting (LiteNetLib)...");

            server = SetupTransport();
            stopped = false;

            RegisterCommands();

            server.Start();

            // Run server loop
            while (!stopped)
            {
                server.Update();
                Thread.Sleep(15);
            }
        }

        private static DedicatedTransportServer SetupTransport()
        {
            return new DedicatedLiteNetLibServer();
        }

        private static void RegisterCommands()
        {
            commands["help"] = new Command
            {
                Name = "help",
                Description = "Displays all available commands",
                Execute = (args) =>
                {
                    Console.WriteLine("Available commands:");
                    foreach (var cmd in commands.Values)
                    {
                        Console.WriteLine($"  {cmd.Name} - {cmd.Description}");
                    }
                }
            };

            commands["stop"] = new Command
            {
                Name = "stop",
                Description = "Stops the dedicated server",
                Execute = (args) =>
                {
                    Console.WriteLine("Stopping server...");
                    server?.Stop();
                    stopped = true;
                }
            };

            commands["players"] = new Command
            {
                Name = "players",
                Description = "Lists all connected players",
                Execute = (args) =>
                {
                    var players = server?.GetPlayers();
                    if (players == null || players.Count == 0)
                    {
                        Console.WriteLine("No players connected.");
                        return;
                    }

                    Console.WriteLine($"Connected players ({players.Count}):");
                    foreach (var p in players.Values)
                    {
                        Console.WriteLine($"  ClientID: {p.ClientID}, Master: {p.IsMaster}, Ping: {p.Connection.Ping}ms");
                    }
                }
            };
        }
    }
}
