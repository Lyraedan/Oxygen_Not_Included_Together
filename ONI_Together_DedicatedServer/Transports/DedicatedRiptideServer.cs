using Riptide;
using Riptide.Utils;
using ONI_Together_DedicatedServer.ONI;
using Shared.Profiling;

namespace ONI_Together_DedicatedServer.Transports
{
    public class DedicatedRiptideServer : DedicatedTransportServer
    {
        public Server? _server;

        public Dictionary<ulong, ONI.Player> ConnectedPlayers = new Dictionary<ulong, ONI.Player>(); // clientId, Player

        public DedicatedRiptideServer()
        {
            using var _ = Profiler.Scope();

            RiptideLogger.Initialize(Console.WriteLine, false);
        }

        public override void Start()
        {
            using var _ = Profiler.Scope();

            if (IsRunning())
                return;

            string ip = ServerConfiguration.Instance.Config.Ip;
            int port = ServerConfiguration.Instance.Config.Port;
            int maxPlayers = ServerConfiguration.Instance.Config.MaxLobbySize;

            _server = new Server("ONI Together: Dedicated Server");
            _server.MessageReceived += OnServerMessageReceived;
            _server.ClientConnected += OnClientConnected;
            _server.ClientDisconnected += OnClientDisconnected;
            _server.Start((ushort)port, (ushort)maxPlayers, useMessageHandlers: false);
            Console.WriteLine($"Started server on {ip}:{port}");
        }

        private void OnClientConnected(object? sender, ServerConnectedEventArgs e)
        {
            using var _ = Profiler.Scope();

            ulong clientId = e.Client.Id;
            if(!ConnectedPlayers.ContainsKey(clientId))
            {
                ONI.Player player = new ONI.Player(e.Client, ConnectedPlayers.Count == 0); // If there are no connected clients we are the master
                ConnectedPlayers.Add(clientId, player);
                Console.Write($"A new player joined the server. {player.ClientID} : {player.IsMaster}");
            }
        }

        private void OnClientDisconnected(object? sender, ServerDisconnectedEventArgs e)
        {
            using var _ = Profiler.Scope();

            ulong clientId = e.Client.Id;
            bool wasMaster = false;
            if(ConnectedPlayers.TryGetValue(clientId, out ONI.Player? player))
            {
                wasMaster = player.IsMaster;
                ConnectedPlayers.Remove(clientId);
            }
            Console.Write($"A player disconnected from the server. {clientId} : {wasMaster}");

            if (!wasMaster) // We wasn't the master we don't care
                return;

            Console.WriteLine("\nThe master disconnected! Attempting to assign a new master!");
            if (_server?.Clients.Length > 0)
            {
                // Find the client with the smallest ping
                Connection? newMasterClient = _server.Clients.Where(c => c.SmoothRTT >= 0).OrderBy(c => c.SmoothRTT).FirstOrDefault();

                if (newMasterClient != null && ConnectedPlayers.TryGetValue(newMasterClient.Id, out ONI.Player newMaster))
                {
                    newMaster.UpdateMasterState(true);
                    Console.WriteLine($"New master assigned: Client {newMasterClient.Id} with ping {newMasterClient.SmoothRTT}");

                    // Notify this client that they are now the master, TODO: Send a migration packet
                }
            }
            else
            {
                Console.WriteLine("No other clients connected. No master assigned.");
            }
        }

        private void OnServerMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            ulong clientId = e.FromConnection.Id;
            byte[] rawData = e.Message.GetBytes();

            if (!ConnectedPlayers.TryGetValue(clientId, out var player))
                return;

            MessageSendMode SendMode = MessageSendMode.Reliable;

            if (player.IsMaster)
            {
                // --- CHECK FOR ROUTING HEADER ---
                // Format: [0xDD][TargetId(8 bytes)][inner packet bytes]
                if (rawData.Length >= 9 && rawData[0] == 0xDD)
                {
                    ulong targetId = BitConverter.ToUInt64(rawData, 1);
                    byte[] innerData = new byte[rawData.Length - 9];
                    Buffer.BlockCopy(rawData, 9, innerData, 0, innerData.Length);

                    int innerPacketType = innerData.Length >= 4
                        ? BitConverter.ToInt32(innerData, 0) : 0;

                    Console.WriteLine($"Routing master packet {innerPacketType} to slave {targetId}");

                    byte[] relayed = Utils.SerializePacketForSending(Utils.DEDICATED_SERVER_PACKET_ID, (writer) =>
                    {
                        writer.Write(innerPacketType);
                        writer.Write((int)SendMode);
                        writer.Write(clientId);                    // SenderId = master
                        writer.Write(innerData.Length);
                        writer.Write(innerData);
                    });

                    var msg = Riptide.Message.Create(SendMode, 1);
                    msg.AddBytes(relayed);

                    if (ConnectedPlayers.TryGetValue(targetId, out var targetPlayer))
                    {
                        targetPlayer.Connection.Send(msg);
                    }
                    return;
                }

                // --- BROADCAST TO ALL SLAVES (no routing header) ---
                Console.WriteLine("Broadcasting master packet to all slaves");

                byte[] relayedPacket = Utils.SerializePacketForSending(Utils.DEDICATED_SERVER_PACKET_ID, (writer) =>
                {
                    int packetType = rawData.Length >= 4 ? BitConverter.ToInt32(rawData, 0) : 0;
                    writer.Write(packetType);
                    writer.Write((int)SendMode);
                    writer.Write(clientId);                        // SenderId = master
                    writer.Write(rawData.Length);
                    writer.Write(rawData);
                });

                var broadcastMsg = Riptide.Message.Create(SendMode, 1);
                broadcastMsg.AddBytes(relayedPacket);

                foreach (var slave in ConnectedPlayers.Values.Where(p => !p.IsMaster))
                {
                    slave.Connection.Send(broadcastMsg);
                }
            }
            else
            {
                // Slave → Master: always relay with sender ID
                Console.WriteLine($"Relaying slave packet to master");
                int packetType = rawData.Length >= 4 ? BitConverter.ToInt32(rawData, 0) : 0;

                byte[] slaveRelayed = Utils.SerializePacketForSending(Utils.DEDICATED_SERVER_PACKET_ID, (writer) =>
                {
                    writer.Write(packetType);
                    writer.Write((int)SendMode);
                    writer.Write(clientId);                        // SenderId = slave
                    writer.Write(rawData.Length);
                    writer.Write(rawData);
                });

                var slaveMsg = Riptide.Message.Create(SendMode, 1);
                slaveMsg.AddBytes(slaveRelayed);

                var master = ConnectedPlayers.Values.FirstOrDefault(p => p.IsMaster);
                if (master != null)
                {
                    _server?.Send(slaveMsg, master.Connection);
                }
            }
        }

        public override void Stop()
        {
            using var _ = Profiler.Scope();

            if (!IsRunning())
                return;

            _server.Stop();
            _server = null;
        }

        public override bool IsRunning()
        {
            using var _ = Profiler.Scope();

            if (_server == null)
                return false;

            return _server.IsRunning;
        }

        public override void Update()
        {
            using var _ = Profiler.Scope();

            _server?.Update();
        }

        public override Dictionary<ulong, ONI.Player> GetPlayers()
        {
            using var _ = Profiler.Scope();

            return ConnectedPlayers;
        }
    }
}
