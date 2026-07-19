#if DEBUG
using System;
using ONI_Together.DebugTools;
using Riptide;
using Riptide.Utils;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_Together.Tests
{
    public static class RiptideSmokeTest
    {
        private static Server _server;
        private static Client _client;
        private static bool _packetReceived;
        private static ulong _riptideSenderId;

        public static void Run(string ip = "127.0.0.1", ushort port = 7777)
        {
            using var _ = Profiler.Scope();

            DebugConsole.Log("[RiptideSmokeTest] Starting");

            _packetReceived = false;
            _riptideSenderId = 0;
            _client = null;
            _server = null;
            TestPacket.Reset();

            try
            {
                RiptideLogger.Initialize(DebugConsole.Log, false);

                _server = new Server("Riptide SmokeTest");
                _server.MessageReceived += OnServerMessageReceived;
                _server.Start(port, 1, useMessageHandlers: false);

                //Game.Instance?.Trigger(MP_HASHES.OnConnected);
                //Game.Instance?.Trigger(MP_HASHES.GameServer_OnServerStarted);

                _client = new Client("Host client");
                _client.Connected += OnClientConnected;
                _client.Disconnected += OnClientDisconnected;
                _client.Connect($"{ip}:{port}", useMessageHandlers: false);

                // Tick until packet received or timeout
                int ticks = 0;
                while (!_packetReceived && ticks < 200)
                {
                    _server.Update();
                    _client.Update();
                    ticks++;
                }

                if (!_packetReceived)
                    throw new TimeoutException("Packet was never received by server");

                if (!TestPacket.WasDispatched)
                    throw new InvalidOperationException("Test packet was received but not dispatched");
                if (TestPacket.PayloadClientId != 512)
                    throw new InvalidOperationException($"Expected payload ClientID 512, got {TestPacket.PayloadClientId}");
                if (TestPacket.SenderId != _riptideSenderId)
                    throw new InvalidOperationException($"Dispatch sender {TestPacket.SenderId} did not match Riptide connection {_riptideSenderId}");
                if (TestPacket.SenderIsHost)
                    throw new InvalidOperationException("Client-to-server test packet was incorrectly marked as sent by the host");

            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[RiptideSmokeTest] FAILED: {ex}", false);
                throw;
            }
            finally
            {
                _client?.Disconnect();
                _server?.Stop();
            }

            DebugConsole.Log("[RiptideSmokeTest] PASSED");
        }

        private static void OnClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            using var _ = Profiler.Scope();

            //MultiplayerSession.InSession = false;
            DebugConsole.Log("[RiptideSmokeTest] Client disconnected");
        }

        private static void OnClientConnected(object sender, EventArgs e)
        {
            using var _ = Profiler.Scope();

            DebugConsole.Log("[RiptideSmokeTest] Client connected");

            //MultiplayerSession.InSession = true;
            //MultiplayerSession.SetHost(_client.Id);

            TestPacket packet = new TestPacket();
            packet.ClientID = 512;
            SendPacket(packet);
        }

        private static void SendPacket(IPacket packet)
        {
            using var _ = Profiler.Scope();

            byte[] bytes = PacketSender.SerializePacketForSending(packet);

            Riptide.Message msg = Riptide.Message.Create(MessageSendMode.Reliable, 1); // dummy ID
            msg.AddBytes(bytes);

            _client.Send(msg);
        }

        private static void OnServerMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            using var _ = Profiler.Scope();

            ulong clientId = e.FromConnection.Id;
            _riptideSenderId = clientId;
            byte[] rawData = e.Message.GetBytes();
            int size = rawData.Length;

            // Try to read the 4-byte packet type at the start
            int packetType = 0;
            if (rawData.Length >= 4)
                packetType = BitConverter.ToInt32(rawData, 0);

            DebugConsole.Log(
                $"[RiptideSmokeTest] Server received packet from {clientId}, " +
                $"PacketType={packetType}, Size={size} bytes"
            );

            DebugConsole.Log($"[RiptideSmokeTest] Handling packet: " + packetType);

            var scope = Profiler.Scope();

            // Pass the full payload (including packetType) to your handler
            PacketHandler.HandleIncoming(rawData, new DispatchContext(clientId, senderIsHost: false));

            scope.End(1, size);

            _packetReceived = true;
        }
    }
}
#endif
