using ONI_Together.Networking;
using ONI_Together.Networking.Transport;
using ONI_Together.Networking.Transport.External;
using System;
using System.Collections.Generic;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class ExternalTransportTests
    {
        private const string TestTransportId = "mock-external-transport";

        [UnitTest(name: "Custom transport provider metadata and settings", category: "Transport")]
        public static UnitTestResult CustomTransportProviderMetadata()
        {
            var provider = new MockTransportProvider();

            if (!ExternalTransportProvider.TryCreate(provider, out var wrapper, out string error))
                return UnitTestResult.Fail($"Provider failed to bind: {error}");

            if (wrapper.Id != TestTransportId)
                return UnitTestResult.Fail($"Id mismatch: {wrapper.Id}");
            if (wrapper.ApiVersion != 1)
                return UnitTestResult.Fail($"ApiVersion mismatch: {wrapper.ApiVersion}");
            if (!wrapper.IsDirect)
                return UnitTestResult.Fail("DirectConnect capability was not read");

            var settings = wrapper.GetSettings();
            if (settings.Count != 2)
                return UnitTestResult.Fail($"Expected 2 settings, got {settings.Count}");
            if (settings[0].Key != "address" || settings[0].Kind != ExternalTransportSettingKind.Text)
                return UnitTestResult.Fail("First setting metadata mismatch");
            if (settings[1].Key != "port"
                || settings[1].Kind != ExternalTransportSettingKind.Integer
                || !settings[1].Required)
                return UnitTestResult.Fail("Second setting metadata mismatch");

            return UnitTestResult.Pass("Custom provider metadata and schema are readable");
        }

        [UnitTest(name: "Custom transport adapters bind and send raw bytes", category: "Transport")]
        public static UnitTestResult CustomTransportAdapters()
        {
            var provider = new MockTransportProvider();
            if (!ExternalTransportProvider.TryCreate(provider, out var wrapper, out string error))
                return UnitTestResult.Fail($"Provider failed to bind: {error}");

            var server = wrapper.CreateServer();
            if (server == null)
                return UnitTestResult.Fail("CreateServer returned null");
            if (server.LocalPlayerId != 1)
                return UnitTestResult.Fail($"Server local id mismatch: {server.LocalPlayerId}");

            var client = wrapper.CreateClient();
            if (client == null)
                return UnitTestResult.Fail("CreateClient returned null");
            if (client.LocalPlayerId != 2 || client.HostPlayerId != 1)
                return UnitTestResult.Fail("Client ids mismatch");

            var payload = new byte[] { 1, 2, 3 };
            if (!server.SendRaw(2, payload, PacketSendMode.Reliable))
                return UnitTestResult.Fail("Server SendRaw failed");
            if (!client.SendRaw(1, payload, PacketSendMode.Unreliable))
                return UnitTestResult.Fail("Client SendRaw failed");

            if (MockTransportProvider.LastServer?.Sent.Count != 1)
                return UnitTestResult.Fail("Server did not receive the sent payload");
            if (MockTransportProvider.LastClient?.Sent.Count != 1)
                return UnitTestResult.Fail("Client did not receive the sent payload");

            var ids = server.GetConnectedPlayerIds();
            if (ids.Count != 2 || ids[0] != 1 || ids[1] != 2)
                return UnitTestResult.Fail("Connected player ids mismatch");

            return UnitTestResult.Pass("External server/client adapters bind and send");
        }

        [UnitTest(name: "Custom transport registration is idempotent per id", category: "Transport")]
        public static UnitTestResult CustomTransportRegistration()
        {
            var provider = new MockTransportProvider();
            var existing = TransportRegistry.GetExternalProvider(TestTransportId);
            if (existing == null)
            {
                if (!TransportRegistry.RegisterExternalTransport_API(provider))
                    return UnitTestResult.Fail("Registration failed");
                existing = TransportRegistry.GetExternalProvider(TestTransportId);
            }

            if (existing == null || existing.Id != TestTransportId)
                return UnitTestResult.Fail("Registered provider not found");

            if (TransportRegistry.RegisterExternalTransport_API(new MockTransportProvider()))
                return UnitTestResult.Fail("Duplicate registration should be rejected");

            return UnitTestResult.Pass("Custom provider registration is tracked by id");
        }
    }

    internal sealed class MockTransportProvider
    {
        public static MockServer LastServer;
        public static MockClient LastClient;

        public string Id => "mock-external-transport";
        public string DisplayName => "Mock Transport";
        public int ApiVersion => 1;
        public int Capabilities => (int)ExternalTransportCapabilities.DirectConnect;

        public MockSetting[] GetSettings()
        {
            return new[]
            {
                new MockSetting
                {
                    Key = "address",
                    Label = "Address",
                    Kind = 0,
                    Scope = 3,
                    DefaultValue = "127.0.0.1",
                    Required = true
                },
                new MockSetting
                {
                    Key = "port",
                    Label = "Port",
                    Kind = 1,
                    Scope = 3,
                    DefaultValue = "8080",
                    Required = true
                }
            };
        }

        public MockServer CreateServer()
        {
            LastServer = new MockServer();
            return LastServer;
        }

        public MockClient CreateClient()
        {
            LastClient = new MockClient();
            return LastClient;
        }
    }

    internal sealed class MockSetting
    {
        public string Key;
        public string Label;
        public string Tooltip;
        public string Placeholder;
        public int Kind;
        public int Scope;
        public string DefaultValue;
        public bool Required;
    }

    internal sealed class MockServer
    {
        public readonly List<(ulong playerId, byte[] payload, int sendMode)> Sent = new();
        public ulong LocalPlayerId => 1;

        public void Start(Dictionary<string, string> settings) { }
        public void Stop() { }
        public void Update() { }
        public bool TryDequeueReceived(out ulong senderId, out byte[] payload)
        {
            senderId = 0;
            payload = null;
            return false;
        }
        public ulong[] GetConnectedPlayerIds() => new[] { 1UL, 2UL };
        public bool Send(ulong playerId, byte[] payload, int sendMode)
        {
            Sent.Add((playerId, payload, sendMode));
            return true;
        }
        public void Kick(ulong playerId) { }
        public string LastError => string.Empty;
    }

    internal sealed class MockClient
    {
        public readonly List<(ulong playerId, byte[] payload, int sendMode)> Sent = new();
        public ulong LocalPlayerId => 2;
        public ulong HostPlayerId => 1;
        public bool IsConnected => true;

        public void Connect(Dictionary<string, string> settings) { }
        public void Disconnect() { }
        public void Reconnect(Dictionary<string, string> settings) { }
        public void Update() { }
        public bool TryDequeueReceived(out ulong senderId, out byte[] payload)
        {
            senderId = 0;
            payload = null;
            return false;
        }
        public bool Send(ulong playerId, byte[] payload, int sendMode)
        {
            Sent.Add((playerId, payload, sendMode));
            return true;
        }
        public string LastDisconnectReason => string.Empty;
        public string LastDisconnectMessage => string.Empty;
        public int GetPing() => 10;
        public int GetJitterState() => 0;
        public int GetLatencyState() => 0;
        public int GetPacketLossState() => 0;
        public int GetServerPerformanceState() => 0;
    }
}
