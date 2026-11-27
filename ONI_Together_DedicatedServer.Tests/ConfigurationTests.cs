using System;
using System.IO;
using ONI_Together_DedicatedServer;
using Xunit;

public class ConfigurationTests
{
    [Fact]
    public void Sanitize_clamps_dangerous_values()
    {
        var c = new DedicatedConfig
        {
            Port = -5,
            MaxLobbySize = 0,
            TimeoutSeconds = 1,
            TickRate = 99999,
            MaxMessagesPerPoll = 0,
            MaxPacketSize = 1,
            RateLimitMessagesPerSecond = 0,
            SaveBackupCount = -3,
            MaxSaveBytes = 1,
            ServerName = "  Hi\0There  ",
            Ip = "  0.0.0.0 "
        };
        c.Sanitize();

        Assert.InRange(c.Port, 1, 65535);
        Assert.InRange(c.MaxLobbySize, 1, 1000);
        Assert.InRange(c.TimeoutSeconds, 5, 3600);
        Assert.Equal(240, c.TickRate);
        Assert.InRange(c.MaxMessagesPerPoll, 1, 100_000);
        Assert.InRange(c.MaxPacketSize, 1024, 16 * 1024 * 1024);
        Assert.InRange(c.RateLimitMessagesPerSecond, 1, 1_000_000);
        Assert.InRange(c.SaveBackupCount, 0, 100);
        Assert.True(c.MaxSaveBytes >= 1024);
        Assert.False(c.ServerName.Contains('\0'));
        Assert.Equal("HiThere", c.ServerName);
        Assert.Equal("0.0.0.0", c.Ip);
    }

    [Fact]
    public void ResolveSaveFilePath_appends_sav_and_confines_to_saves_dir()
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-cfg-" + Guid.NewGuid().ToString("N"));
        string saves = Path.Combine(root, "saves");
        Directory.CreateDirectory(saves);
        try
        {
            ServerConfiguration.InitializePaths(root, saves);
            _ = ServerConfiguration.Instance; // loads/creates

            string? p = ServerConfiguration.ResolveSaveFilePath("myworld");
            Assert.NotNull(p);
            Assert.EndsWith("myworld.sav", p!, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.GetFullPath(saves), p!, StringComparison.OrdinalIgnoreCase);

            // Traversal rejected.
            Assert.Null(ServerConfiguration.ResolveSaveFilePath("../escape"));

            // Blank -> null.
            Assert.Null(ServerConfiguration.ResolveSaveFilePath(""));
            Assert.Null(ServerConfiguration.ResolveSaveFilePath("  "));

            // Rooted external rejected by default (allowExternal=false).
            string external = Path.Combine(Path.GetTempPath(), "ext-" + Guid.NewGuid().ToString("N") + ".sav");
            File.WriteAllBytes(external, new byte[] { 1 });
            Assert.Null(ServerConfiguration.ResolveSaveFilePath(external));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ResolveSaveFilePath_rooted_inside_saves_allowed()
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-cfg-" + Guid.NewGuid().ToString("N"));
        string saves = Path.Combine(root, "saves");
        Directory.CreateDirectory(saves);
        try
        {
            ServerConfiguration.InitializePaths(root, saves);
            _ = ServerConfiguration.Instance;
            string inside = Path.Combine(saves, "x.sav");
            string? p = ServerConfiguration.ResolveSaveFilePath(inside);
            Assert.NotNull(p);
            Assert.Equal(Path.GetFullPath(inside), p);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ApplyEnvironmentOverrides_sets_max_message_size()
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ServerConfiguration.InitializePaths(root, Path.Combine(root, "saves"));
            Environment.SetEnvironmentVariable("ONI_MAX_MESSAGE_SIZE", "65536");
            var cfg = ServerConfiguration.Instance.Config;
            Assert.Equal(65536, cfg.MaxMessageSize);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ONI_MAX_MESSAGE_SIZE", null);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SplitCsv_splits_and_trims()
    {
        Assert.Equal(new[] { "a", "b", "c" }, ServerConfiguration.SplitCsv("a,b ; c"));
        Assert.Empty(ServerConfiguration.SplitCsv(""));
        Assert.Empty(ServerConfiguration.SplitCsv(null));
    }
}
