using ONI_Together.DedicatedServer;
using ONI_Together_DedicatedServer;
using Xunit;

public class CliTests
{
    [Fact]
    public void ParseArgs_recognizes_core_flags()
    {
        var o = DedicatedServer.ParseArgs(new[]
        {
            "--non-interactive", "--headless",
            "--config-dir", "/cfg",
            "--save-dir", "/saves",
            "--ip", "127.0.0.1",
            "--port", "1234",
            "--max-players", "8",
            "--tick-rate", "30",
            "--save", "world",
            "--server-name", "My Server",
            "--verbose",
            "--version", "--health-check", "--help"
        });

        Assert.True(o.NonInteractive);
        Assert.Equal("/cfg", o.ConfigDir);
        Assert.Equal("/saves", o.SaveDir);
        Assert.Equal("127.0.0.1", o.Ip);
        Assert.Equal(1234, o.Port);
        Assert.Equal(8, o.MaxPlayers);
        Assert.Equal(30, o.TickRate);
        Assert.Equal("world", o.SaveFile);
        Assert.Equal("My Server", o.ServerName);
        Assert.True(o.VerbosePackets);
        Assert.True(o.ShowVersion);
        Assert.True(o.HealthCheck);
        Assert.True(o.ShowHelp);
    }

    [Fact]
    public void ParseArgs_ignores_malformed_numbers_and_caps_length()
    {
        var o = DedicatedServer.ParseArgs(new[] { "--port", "notanumber", "--tick-rate", "x", "--name", new string('a', 5000) });
        Assert.Null(o.Port);
        Assert.Null(o.TickRate);
        Assert.NotNull(o.ServerName);
        Assert.True(o.ServerName!.Length <= 4096);
    }

    [Fact]
    public void ParseArgs_unknown_flags_ignored()
    {
        var o = DedicatedServer.ParseArgs(new[] { "--bogus", "value", "positional" });
        Assert.False(o.NonInteractive);
        Assert.Null(o.Port);
    }

    [Fact]
    public void ApplyCliOverrides_applies_and_sanitize_clamps()
    {
        var cfg = new DedicatedConfig();
        var o = DedicatedServer.ParseArgs(new[] { "--port", "99999", "--max-players", "0", "--tick-rate", "9999", "--name", "X", "--ip", "0.0.0.0" });
        DedicatedServer.ApplyCliOverrides(cfg, o);
        cfg.Sanitize();
        Assert.InRange(cfg.Port, 1, 65535);
        Assert.InRange(cfg.MaxLobbySize, 1, 1000);
        Assert.Equal(240, cfg.TickRate);
        Assert.Equal("X", cfg.ServerName);
    }
}
