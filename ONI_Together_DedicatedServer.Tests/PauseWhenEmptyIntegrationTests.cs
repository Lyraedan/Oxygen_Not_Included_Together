using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.DedicatedServer;
using ONI_Together_DedicatedServer;
using ONI_Together_DedicatedServer.ONI;
using ONI_Together_DedicatedServer.Transports;
using Xunit;

public class PauseWhenEmptyIntegrationTests
{
    [Fact]
    public void PauseWhenEmpty_persists_server_owned_state_to_saves_current_sav_no_relay_while_empty_and_resume_on_join()
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-pause-it-" + Guid.NewGuid().ToString("N"));
        string savesDir = Path.Combine(root, "saves");
        Directory.CreateDirectory(savesDir);

        var fake = new FakeTransport();
        try
        {
            // Reset process-wide state.
            DedicatedServer._server = fake;
            DedicatedServer._stopRequested = false;
            DedicatedServer._saveFile = new SaveFile("world.sav", new byte[] { 1, 2, 3, 4 });
            DedicatedServer._saveStore = new SaveStore(savesDir, backupCount: 2);
            DedicatedServer._saveFile.Name = "world.sav";

            ServerConfiguration.InitializePaths(root, savesDir);
            var cfg = ServerConfiguration.Instance.Config;
            cfg.PauseWhenEmpty = true;
            cfg.SaveFileName = "current";
            cfg.TickRate = 60;
            cfg.SaveAutosaveSeconds = 0;
            cfg.Sanitize();

            // Empty: no relay; still allow resume check.
            var emptyRelay = new DedicatedLiteNetLibServer();
            emptyRelay.SetPaused(DedicatedLiteNetLibServer.ComputePaused(cfg.PauseWhenEmpty, playerCount: 0));
            Assert.True(emptyRelay.IsPaused);
            Assert.False(emptyRelay.RelayAllowed); // no relay while empty

            fake.OnUpdate = _ => DedicatedServer._stopRequested = true;

            DedicatedServer.RunTickLoop();

            // PauseWhenEmpty persisted server-owned state when empty.
            string current = Path.Combine(savesDir, "current.sav");
            Assert.True(File.Exists(current));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(current));
            Assert.True(fake.Paused); // server set paused while empty
            Assert.Equal(1, fake.UpdateCount); // update still called to accept joins, but no relay processing

            // Resume on join: relay allowed.
            emptyRelay.SetPaused(DedicatedLiteNetLibServer.ComputePaused(cfg.PauseWhenEmpty, playerCount: 1));
            Assert.False(emptyRelay.IsPaused);
            Assert.True(emptyRelay.RelayAllowed); // resumes relay on join

            var joinedRelay = new DedicatedLiteNetLibServer();
            joinedRelay.SetPaused(DedicatedLiteNetLibServer.ComputePaused(cfg.PauseWhenEmpty, playerCount: 1));
            Assert.True(joinedRelay.RelayAllowed);
        }
        finally
        {
            DedicatedServer._server = null;
            DedicatedServer._saveFile = null;
            DedicatedServer._saveStore = null;
            DedicatedServer._stopRequested = false;
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private sealed class FakeTransport : DedicatedTransportServer
    {
        public bool Paused { get; private set; }
        public int UpdateCount { get; private set; }
        public Action<FakeTransport>? OnUpdate { get; set; }

        public override bool Start() => true;
        public override void Stop() { }
        public override bool IsRunning() => !DedicatedServer._stopRequested;
        public override bool IsPaused => Paused;
        public override void SetPaused(bool paused) => Paused = paused;
        public override void ReloadConfiguration() { }
        public override void SetWorldInfo(string worldName, int cycle = 1) { }
        public override Dictionary<ulong, Player> GetPlayers() => new();

        public override void Update()
        {
            UpdateCount++;
            OnUpdate?.Invoke(this);
        }
    }
}
