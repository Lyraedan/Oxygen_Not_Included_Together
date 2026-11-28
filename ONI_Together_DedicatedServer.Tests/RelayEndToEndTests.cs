using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using ONI_Together_DedicatedServer;
using ONI_Together_DedicatedServer.ONI;
using ONI_Together_DedicatedServer.Security;
using ONI_Together_DedicatedServer.Transports;
using Shared.Networking.Dedicated;
using Xunit;

public sealed class RelayEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oni-relay-" + Guid.NewGuid().ToString("N"));
    private readonly DedicatedLiteNetLibServer _server = new();
    private readonly List<FakePeer> _peers = new();
    private readonly DedicatedConfig _config;
    private readonly int _port;
    private SaveFile? _colony = new("My Base.sav", MakeBytes(100_000));

    public RelayEndToEndTests()
    {
        ServerConfiguration.InitializePaths(_root, Path.Combine(_root, "saves"));
        _config = ServerConfiguration.Instance.Config;
        _port = FreeUdpPort();
        _config.Ip = "0.0.0.0";
        _config.Port = _port;
        _config.MaxLobbySize = 4;
        _config.DiscoveryEnabled = false;
        _server.ColonyProvider = () => _colony;
        Assert.True(_server.Start());
    }

    public void Dispose()
    {
        foreach (var peer in _peers)
            peer.Net.Stop();
        _server.Stop();
        ServerConfiguration.InitializePaths();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void First_peer_hosts_and_is_told_about_the_stored_colony()
    {
        var master = Join(token: 1);

        var frame = master.WaitFrame(this, RelayFrameType.MasterAssigned);
        using var reader = RelayProtocol.ReadBody(frame);
        Assert.Equal(RelayProtocol.Version, reader.ReadInt32());
        Assert.True(reader.ReadBoolean());
        Assert.Equal("My Base", reader.ReadString());
        Assert.Equal(100_000, reader.ReadInt32());
    }

    [Fact]
    public void Players_are_refused_until_the_host_is_ready_then_get_the_ids_a_direct_host_would_give()
    {
        var master = Join(token: 1);
        master.WaitFrame(this, RelayFrameType.MasterAssigned);

        AssertRefused(Connect(token: 2));

        Ready(master);
        var client = Join(token: 2);

        Assert.Equal((ulong)client.Server!.RemoteId + 2, PeerIdIn(master.WaitFrame(this, RelayFrameType.PeerJoined)));
    }

    [Fact]
    public void Client_packets_reach_only_the_host_and_host_packets_reach_only_their_target()
    {
        var (master, first, firstId) = HostWithOneClient();
        var second = Join(token: 3);
        master.WaitFrame(this, RelayFrameType.PeerJoined);

        byte[] fromClient = { 11, 22, 33, 44, 55 };
        first.Send(fromClient);
        var inbound = master.WaitFrame(this, RelayFrameType.FromPeer);
        Assert.True(RelayProtocol.TryReadPeerData(inbound, out ulong sender, out byte[] payload));
        Assert.Equal(firstId, sender);
        Assert.Equal(fromClient, payload);

        byte[] toClient = { 99, 88, 77, 66 };
        master.Send(RelayProtocol.PeerData(RelayFrameType.ToPeer, firstId, toClient));
        Pump(() => first.Received.Count > 0);
        Assert.Equal(toClient, first.Received.Dequeue());

        PumpFor(150);
        Assert.Empty(second.Received);
    }

    [Fact]
    public void A_burst_of_reliable_packets_arrives_completely_and_in_order()
    {
        var (master, client, _) = HostWithOneClient();

        for (int i = 0; i < 2000; i++)
            client.Send(BitConverter.GetBytes(i));

        var seen = new List<int>();
        Pump(() =>
        {
            while (master.Received.Count > 0)
            {
                RelayProtocol.TryReadPeerData(master.Received.Dequeue(), out _, out byte[] payload);
                seen.Add(BitConverter.ToInt32(payload));
            }
            return seen.Count >= 2000;
        });

        Assert.Equal(Enumerable.Range(0, 2000), seen);
    }

    [Fact]
    public void Large_unreliable_host_packets_still_arrive()
    {
        var (master, client, clientId) = HostWithOneClient();
        byte[] payload = MakeBytes(1000);

        master.Send(RelayProtocol.PeerData(RelayFrameType.ToPeer, clientId, payload), DeliveryMethod.ReliableOrdered);

        Pump(() => client.Received.Count > 0);
        Assert.Equal(payload, client.Received.Dequeue());
    }

    [Fact]
    public void Host_downloads_the_stored_colony_unchanged()
    {
        var master = Join(token: 1);
        master.WaitFrame(this, RelayFrameType.MasterAssigned);
        master.Send(RelayProtocol.Signal(RelayFrameType.SaveRequest));

        var (name, data) = ReceiveSave(master);

        Assert.Equal("My Base", name);
        Assert.Equal(_colony!.Data, data);
    }

    [Fact]
    public void Host_upload_replaces_the_colony_but_a_client_cannot_upload()
    {
        var uploads = new List<SaveFile>();
        _server.ColonyReceived += uploads.Add;
        var (master, client, _) = HostWithOneClient();

        foreach (byte[] frame in RelayProtocol.SaveFrames("Evil", MakeBytes(5000)))
            client.Send(frame);
        PumpFor(200);
        Assert.Empty(uploads);
        master.Received.Clear();

        byte[] save = MakeBytes(200_000, seed: 7);
        foreach (byte[] frame in RelayProtocol.SaveFrames("My Base", save))
            master.Send(frame, channel: RelayProtocol.SaveChannel);

        Pump(() => uploads.Count > 0);
        Assert.Equal("My Base.sav", uploads[0].Name);
        Assert.Equal(save, uploads[0].Data);
    }

    [Fact]
    public void When_the_host_leaves_the_session_ends_and_the_next_peer_hosts()
    {
        var (master, client, _) = HostWithOneClient();

        master.Server!.Disconnect();
        Pump(() => client.Disconnected);
        Pump(() => _server.GetPlayers().Count == 0);

        var successor = Join(token: 5);
        successor.WaitFrame(this, RelayFrameType.MasterAssigned);
        Assert.False(_server.HostReady);
    }

    [Fact]
    public void The_host_slot_is_reserved_while_the_host_loads_the_colony()
    {
        var master = Join(token: 1);
        master.WaitFrame(this, RelayFrameType.MasterAssigned);
        master.Send(RelayProtocol.Signal(RelayFrameType.SaveRequest));
        ReceiveSave(master);

        master.Server!.Disconnect();
        Pump(() => _server.GetPlayers().Count == 0);

        AssertRefused(Connect(token: 2));

        var back = Join(token: 1);
        back.WaitFrame(this, RelayFrameType.MasterAssigned);
    }

    [Fact]
    public void The_host_can_kick_a_player()
    {
        var (master, client, clientId) = HostWithOneClient();

        master.Send(RelayProtocol.PeerEvent(RelayFrameType.KickPeer, clientId));

        Pump(() => client.Disconnected);
        Assert.Equal(clientId, PeerIdIn(master.WaitFrame(this, RelayFrameType.PeerLeft)));
    }

    [Fact]
    public void A_player_who_cannot_load_the_colony_is_refused_with_the_reason()
    {
        _colony = new SaveFile("Rocket Base.sav", ColonyDlcTests.FakeSave("[\"\",\"EXPANSION1_ID\"]"));

        var baseGame = Connect(token: 1);
        AssertRefused(baseGame);
        Assert.Equal(RelayRejectCode.NeedsSpacedOut, baseGame.RejectCode);
        Assert.Contains("Spaced Out!", baseGame.RejectReason);

        var owner = Connect(token: 2, "EXPANSION1_ID");
        Pump(() => owner.Connected);
        owner.WaitFrame(this, RelayFrameType.MasterAssigned);
    }

    [Fact]
    public void A_full_server_says_so()
    {
        _config.MaxLobbySize = 1;
        Join(token: 1);

        var late = Connect(token: 2);
        AssertRefused(late);
        Assert.Equal(RelayRejectCode.ServerFull, late.RejectCode);
        Assert.Contains("full", late.RejectReason);
    }

    [Fact]
    public void Missing_content_packs_are_sent_as_ids_the_mod_can_name()
    {
        _colony = new SaveFile("Frozen.sav", ColonyDlcTests.FakeSave("[\"\",\"DLC2_ID\",\"DLC3_ID\"]"));

        var player = Connect(token: 1, "DLC2_ID");
        AssertRefused(player);
        Assert.Equal(RelayRejectCode.MissingDlc, player.RejectCode);
        Assert.Equal(new[] { "DLC3_ID" }, player.RejectArgs);
    }

    [Fact]
    public void A_host_that_cannot_load_the_colony_frees_the_slot_at_once()
    {
        var master = Join(token: 1);
        master.WaitFrame(this, RelayFrameType.MasterAssigned);
        master.Send(RelayProtocol.Signal(RelayFrameType.SaveRequest));
        ReceiveSave(master);

        master.Server!.Disconnect(RelayProtocol.Signal(RelayFrameType.HostAborted));
        master.Net.Stop();
        Pump(() => _server.GetPlayers().Count == 0);

        var next = Join(token: 2);
        next.WaitFrame(this, RelayFrameType.MasterAssigned);
    }

    [Fact]
    public void A_mod_without_a_relay_hello_is_refused()
    {
        var old = NewPeer();
        old.Server = old.Net.Connect("127.0.0.1", _port, RelayProtocol.ConnectionKey);

        AssertRefused(old);
    }

    [Fact]
    public void The_dlc_list_sent_by_the_mod_is_enforced()
    {
        _config.DlcCheckMode = DlcCheckMode.Required;
        _config.RequiredDlcIds = new[] { "EXPANSION1_ID" };

        AssertRefused(Connect(token: 1));

        var owner = Connect(token: 2, "EXPANSION1_ID");
        Pump(() => owner.Connected);
    }

    private (FakePeer master, FakePeer client, ulong clientId) HostWithOneClient()
    {
        var master = Join(token: 1);
        master.WaitFrame(this, RelayFrameType.MasterAssigned);
        Ready(master);
        var client = Join(token: 2);
        ulong id = PeerIdIn(master.WaitFrame(this, RelayFrameType.PeerJoined));
        return (master, client, id);
    }

    private void Ready(FakePeer master)
    {
        master.Send(RelayProtocol.Signal(RelayFrameType.HostReady));
        Pump(() => _server.HostReady);
    }

    private (string name, byte[] data) ReceiveSave(FakePeer master)
    {
        var assembler = new RelaySaveAssembler(10_000_000);
        string name = string.Empty;
        byte[] data = Array.Empty<byte>();
        Pump(() =>
        {
            while (master.Received.Count > 0)
            {
                if (assembler.Accept(master.Received.Dequeue(), out var n, out var d))
                {
                    name = n;
                    data = d;
                    return true;
                }
            }
            return false;
        });
        return (name, data);
    }

    private static ulong PeerIdIn(byte[] frame) => BitConverter.ToUInt64(frame, RelayProtocol.HeaderSize);

    private FakePeer NewPeer()
    {
        var peer = new FakePeer();
        _peers.Add(peer);
        return peer;
    }

    private FakePeer Connect(ulong token, params string[] dlc)
    {
        var hello = new RelayProtocol.Hello { InstanceToken = token };
        hello.ActiveDlcIds.AddRange(dlc);
        hello.OwnedDlcIds.AddRange(dlc);

        var writer = new NetDataWriter();
        writer.Put(RelayProtocol.ConnectionKey);
        writer.Put(hello.ToBytes());

        var peer = NewPeer();
        peer.Server = peer.Net.Connect("127.0.0.1", _port, writer);
        return peer;
    }

    private FakePeer Join(ulong token)
    {
        var peer = Connect(token);
        Pump(() => peer.Connected);
        return peer;
    }

    private void AssertRefused(FakePeer peer)
    {
        Pump(() => peer.Disconnected);
        Assert.False(peer.Connected);
    }

    private void Pump(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 8000)
        {
            Tick();
            if (done())
                return;
        }
        Assert.Fail("Timed out waiting for the relay.");
    }

    private void PumpFor(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
            Tick();
    }

    private void Tick()
    {
        _server.Update();
        foreach (var peer in _peers)
            peer.Net.PollEvents();
        Thread.Sleep(2);
    }

    private static byte[] MakeBytes(int length, int seed = 1)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static int FreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private sealed class FakePeer
    {
        public readonly NetManager Net;
        public readonly Queue<byte[]> Received = new();
        public NetPeer? Server;
        public bool Connected;
        public bool Disconnected;
        public string RejectReason = string.Empty;
        public RelayRejectCode RejectCode;
        public string[] RejectArgs = Array.Empty<string>();

        public FakePeer()
        {
            var listener = new EventBasedNetListener();
            listener.PeerConnectedEvent += _ => Connected = true;
            listener.PeerDisconnectedEvent += (_, info) =>
            {
                Disconnected = true;
                var data = info.AdditionalData;
                if (data != null && data.TryGetString(out var key) && key == RelayProtocol.RejectKey
                    && data.TryGetInt(out int code) && data.TryGetString(out RejectReason))
                {
                    RejectCode = (RelayRejectCode)code;
                    if (data.TryGetStringArray(out string[] args))
                        RejectArgs = args;
                }
            };
            listener.NetworkReceiveEvent += (_, reader, _, _) => Received.Enqueue(reader.GetRemainingBytes());
            Net = new NetManager(listener) { AutoRecycle = true, ChannelsCount = 4 };
            Net.Start();
        }

        public void Send(byte[] data, DeliveryMethod method = DeliveryMethod.ReliableOrdered, byte channel = 0)
        {
            Server!.Send(data, channel, method);
        }

        public byte[] WaitFrame(RelayEndToEndTests test, RelayFrameType type)
        {
            byte[]? found = null;
            test.Pump(() =>
            {
                while (Received.Count > 0)
                {
                    var data = Received.Dequeue();
                    if (RelayProtocol.IsFrame(data) && RelayProtocol.TypeOf(data) == type)
                    {
                        found = data;
                        return true;
                    }
                }
                return false;
            });
            return found!;
        }
    }
}
