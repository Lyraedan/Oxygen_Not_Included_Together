#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking;
using Riptide;
using Riptide.Utils;
using Shared.Profiling;

namespace ONI_Together.Tests
{
    public class DediTest
    {
        private static Client _client;

        public static void Connect(string ip = "127.0.0.1", int port = 7777)
        {
            using var _ = Profiler.Scope();

            RiptideLogger.Initialize(DebugConsole.Log, false);
            _client = new Client("Dedicated client test");
            _client.Connected += OnClientConnected;
            _client.Disconnected += OnClientDisconnected;

            DebugConsole.Log($"Connecting to {ip}:{port}");
            _client.Connect($"{ip}:{port}", useMessageHandlers: false);
        }

        private static void OnClientConnected(object sender, EventArgs e)
        {
            using var _ = Profiler.Scope();

            DebugConsole.Log("[DediTest] Successfully connected to the Dedicated server!");

            SendTestPacket();
        }

        private static void OnClientDisconnected(object sender, DisconnectedEventArgs e)
        {
            using var _ = Profiler.Scope();

            DebugConsole.Log("[DediTest] Successfully disconnected to the Dedicated server!");
        }

        public static void Update()
        {
            using var _ = Profiler.Scope();

            if (_client == null)
                return;
            _client.Update();
        }

        public static void Disconnect()
        {
            using var _ = Profiler.Scope();

            if (_client == null || _client.IsNotConnected)
                return;
            _client.Disconnect();
        }

        public static void SendTestPacket()
        {
            using var _ = Profiler.Scope();

            TestPacket testPacket = new TestPacket();
            testPacket.ClientID = 123;
            SendPacket(testPacket);
            DebugConsole.Log("[DediTest] Sent test packet!");
        }

        private static void SendPacket(IPacket packet)
        {
            using var _ = Profiler.Scope();

            byte[] bytes = PacketSender.SerializePacketForSending(packet);

            Riptide.Message msg = Riptide.Message.Create(MessageSendMode.Reliable, 1); // dummy ID
            msg.AddBytes(bytes);

            _client.Send(msg);
        }
    }
}
#endif