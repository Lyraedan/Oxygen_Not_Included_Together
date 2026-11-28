using ONI_Together_DedicatedServer.Transports;
using Xunit;

public class PauseWhenEmptyTests
{
    [Fact]
    public void PauseWhenEmpty_pauses_when_no_players_and_resumes_on_join()
    {
        // empty + PauseWhenEmpty -> paused (no processing)
        Assert.True(DedicatedLiteNetLibServer.ComputePaused(pauseWhenEmpty: true, playerCount: 0));
        // join -> not paused (resume)
        Assert.False(DedicatedLiteNetLibServer.ComputePaused(pauseWhenEmpty: true, playerCount: 1));
        // disabled -> never paused
        Assert.False(DedicatedLiteNetLibServer.ComputePaused(pauseWhenEmpty: false, playerCount: 0));
    }

    [Fact]
    public void SetPaused_disables_relay_processing_while_empty_and_reenables_on_resume()
    {
        var server = new DedicatedLiteNetLibServer();

        // No processing while empty/paused.
        server.SetPaused(DedicatedLiteNetLibServer.ComputePaused(true, 0));
        Assert.True(server.IsPaused);
        Assert.False(server.RelayAllowed);

        // Resume on join.
        server.SetPaused(DedicatedLiteNetLibServer.ComputePaused(true, 1));
        Assert.False(server.IsPaused);
        Assert.True(server.RelayAllowed);
    }
}
