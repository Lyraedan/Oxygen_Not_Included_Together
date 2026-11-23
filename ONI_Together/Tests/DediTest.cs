#if DEBUG
using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Tests
{
    public class DediTest
    {
        private static NetManager _client;
        private static EventBasedNetListener _listener;
        private static NetPeer _serverPeer;

        public static void Connect(string ip = "127.0.0.1", int port = 7777)
        {
            using var _ = Profiler.Scope();

            _listener = new EventBasedNetListener();
            _client = new NetManager(_listener)
            {
                AutoRecycle = true,
                DisconnectTimeout = 30000,
                UnsyncedEvents = true
            };

            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;

            _client.Start();
            DebugConsole.Log($"[DediTest] Connecting to {ip}:{port}");
            _serverPeer = _client.Connect(ip, port, "ONI_TOGETHER");
        }

        private static void OnPeerConnected(NetPeer peer)
        {
            using var _ = Profiler.Scope();

            DebugConsole.Log("[DediTest] Successfully connected to the Dedicated server!");
            SendTestPacket();
        }

        private static void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            using var _ = Profiler.Scope();

            DebugConsole.Log($"[DediTest] Disconnected from Dedicated server: {disconnectInfo.Reason}");
            _serverPeer = null;
        }

        public static void Update()
        {
            using var _ = Profiler.Scope();

            if (_client == null)
                return;
            _client.PollEvents();
        }

        public static void Disconnect()
        {
            using var _ = Profiler.Scope();

            if (_client == null || !_client.IsRunning)
                return;
            _client.Stop();
            _serverPeer = null;
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

            if (_serverPeer == null) return;

            byte[] bytes = PacketSender.SerializePacketForSending(packet);
            _serverPeer.Send(bytes, DeliveryMethod.ReliableOrdered);
        }
    }
}
#endif