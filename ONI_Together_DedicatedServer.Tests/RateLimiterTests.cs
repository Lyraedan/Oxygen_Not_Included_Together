using ONI_Together_DedicatedServer.Security;
using Xunit;

public class RateLimiterTests
{
    [Fact]
    public void Allows_up_to_capacity_then_blocks()
    {
        var rl = new RateLimiter(capacity: 3, refillPerSecond: 0);
        Assert.True(rl.TryAcquire("k"));
        Assert.True(rl.TryAcquire("k"));
        Assert.True(rl.TryAcquire("k"));
        Assert.False(rl.TryAcquire("k"));
    }

    [Fact]
    public void Different_keys_independent()
    {
        var rl = new RateLimiter(capacity: 1, refillPerSecond: 0);
        Assert.True(rl.TryAcquire("a"));
        Assert.True(rl.TryAcquire("b"));
        Assert.False(rl.TryAcquire("a"));
        Assert.False(rl.TryAcquire("b"));
    }

    [Fact]
    public void Reset_clears_bucket()
    {
        var rl = new RateLimiter(capacity: 1, refillPerSecond: 0);
        Assert.True(rl.TryAcquire("k"));
        Assert.False(rl.TryAcquire("k"));
        rl.Reset("k");
        Assert.True(rl.TryAcquire("k"));
    }

    [Fact]
    public void Unknown_key_treated_as_unknown()
    {
        var rl = new RateLimiter(capacity: 1, refillPerSecond: 0);
        Assert.True(rl.TryAcquire(""));
        Assert.False(rl.TryAcquire(""));
    }
}
