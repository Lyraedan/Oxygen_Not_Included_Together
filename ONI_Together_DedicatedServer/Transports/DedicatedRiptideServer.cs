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
            throw new NotSupportedException(
                "Dedicated server is disabled: authenticated designated-host handshake is not implemented.");
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
            using var _ = Profiler.Scope();

            ulong clientId = e.FromConnection.Id;
            byte[] rawData = e.Message.GetBytes();
            int size = rawData.Length;

            if (ConnectedPlayers.TryGetValue(clientId, out ONI.Player player))
            {
                int packetType = 0;
                if (rawData.Length >= 4)
                    packetType = BitConverter.ToInt32(rawData, 0);

                if (packetType == Utils.DEDICATED_SERVER_PACKET_ID)
                {
                    Console.WriteLine($"Rejected nested dedicated wrapper from client {clientId}");
                    return;
                }

                Console.WriteLine(
                    $"\nServer received packet from {clientId}, " +
                    $"PacketType={packetType}, Size={size} bytes"
                );

                MessageSendMode sendMode = e.Message.SendMode == MessageSendMode.Reliable
                    ? MessageSendMode.Reliable
                    : MessageSendMode.Unreliable;
                // Wrap this as a DedicatedServerMessagePacket
                byte[] relayedPacketData = Utils.SerializePacketForSending(Utils.DEDICATED_SERVER_PACKET_ID, (writer) =>
                {
                    writer.Write(packetType); // PacketID
                    writer.Write((int)sendMode); // Send Type
                    writer.Write(clientId); // Original sender
                    writer.Write(player.IsMaster); // Original sender role
                    writer.Write(rawData.Length);
                    writer.Write(rawData); // PacketData
                });

                // Check if player.IsMaster
                // If we're not the master, send this to the master
                // If we're the master, send it to everyone else and not the master
                if (player.IsMaster)
                {
                    //_server.SendToAll(msg);
                    Console.WriteLine("Broadcasting master packet to clients");
                    var slaves = ConnectedPlayers.Values.Where(p => !p.IsMaster);
                    if (slaves.Any())
                    {
                        foreach (ONI.Player client in slaves)
                        {
                            SendRelay(client.Connection, relayedPacketData, sendMode);
                        }
                    }
                }
                else
                {
                    Console.WriteLine("Recieved packet from client, sending to master!");
                    ONI.Player master = ConnectedPlayers.Values.Where(p => p.IsMaster).FirstOrDefault();
                    if (master != null)
                    {
                        SendRelay(master.Connection, relayedPacketData, sendMode);
                    }
                }
            }
        }

        private static void SendRelay(Connection connection, byte[] data, MessageSendMode sendMode)
        {
            Riptide.Message message = Riptide.Message.Create(sendMode, 1);
            message.AddBytes(data);
            connection.Send(message);
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
