using ONI_Together.DedicatedServer;
using ONI_Together_DedicatedServer;
using Xunit;

public sealed class OperationsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oni-ops-" + Guid.NewGuid().ToString("N"));

    public OperationsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Health_check_needs_a_recent_heartbeat()
    {
        string path = Path.Combine(_root, "heartbeat");
        var now = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(DedicatedServer.CheckHeartbeat(path, now, TimeSpan.FromSeconds(30)).healthy);

        File.WriteAllText(path, now.AddSeconds(-10).Ticks.ToString());
        Assert.True(DedicatedServer.CheckHeartbeat(path, now, TimeSpan.FromSeconds(30)).healthy);

        File.WriteAllText(path, now.AddMinutes(-5).Ticks.ToString());
        Assert.False(DedicatedServer.CheckHeartbeat(path, now, TimeSpan.FromSeconds(30)).healthy);

        File.WriteAllText(path, "garbage");
        Assert.False(DedicatedServer.CheckHeartbeat(path, now, TimeSpan.FromSeconds(30)).healthy);
    }

    [Fact]
    public void Zero_or_negative_settings_fall_back_to_defaults_instead_of_the_minimum()
    {
        var c = new DedicatedConfig { Port = 0, RateLimitMessagesPerSecond = 0, MaxLobbySize = -1 };
        c.Sanitize();

        Assert.Equal(7777, c.Port);
        Assert.Equal(600, c.RateLimitMessagesPerSecond);
        Assert.Equal(4, c.MaxLobbySize);
    }

    [Fact]
    public void Too_large_settings_are_capped()
    {
        var c = new DedicatedConfig { Port = 99999, TickRate = 5000 };
        c.Sanitize();

        Assert.Equal(65535, c.Port);
        Assert.Equal(240, c.TickRate);
    }
}
