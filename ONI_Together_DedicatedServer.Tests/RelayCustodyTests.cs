using ONI_Together_DedicatedServer.ONI;
using Shared.Networking.Dedicated;
using Xunit;

public sealed class RelayCustodyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oni-custody-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void A_transfer_with_a_gap_never_completes()
    {
        var frames = RelayProtocol.SaveFrames("Base", new byte[RelayProtocol.SaveChunkBytes * 3]).ToList();
        frames.RemoveAt(2);
        var assembler = new RelaySaveAssembler(1_000_000);

        Assert.DoesNotContain(true, frames.Select(f => assembler.Accept(f, out _, out _)).ToList());
    }

    [Fact]
    public void An_oversized_announcement_is_ignored()
    {
        var assembler = new RelaySaveAssembler(1000);

        foreach (byte[] frame in RelayProtocol.SaveFrames("Base", new byte[1001]))
            Assert.False(assembler.Accept(frame, out _, out _));
        Assert.False(assembler.InProgress);
    }

    [Theory]
    [InlineData("..\\..\\evil", "evil")]
    [InlineData("a/b:c", "abc")]
    [InlineData("  ", "Dedicated Colony")]
    [InlineData("Käse Basis (Dedicated)", "Käse Basis (Dedicated)")]
    public void Colony_names_cannot_leave_their_folder(string raw, string expected)
    {
        Assert.Equal(expected, RelayProtocol.SanitizeColonyName(raw));
    }

    [Fact]
    public void The_stored_colony_keeps_its_name_across_a_restart()
    {
        var store = new SaveStore(_root);
        store.SaveCurrent(new byte[] { 1, 2, 3 }, "current");
        store.WriteColonyName("current", "My Base");

        var restored = new SaveStore(_root).TryLoadColony("current");

        Assert.NotNull(restored);
        Assert.Equal("My Base.sav", restored!.Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, restored.Data);
    }

    [Fact]
    public void Saving_unchanged_data_keeps_the_older_backups()
    {
        var store = new SaveStore(_root, backupCount: 2);
        store.SaveCurrent(new byte[] { 1 }, "current");
        store.SaveCurrent(new byte[] { 2 }, "current");

        for (int i = 0; i < 5; i++)
            store.SaveCurrent(new byte[] { 2 }, "current");

        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(store.BackupsDirectory, "current.1.sav")));
        Assert.False(File.Exists(Path.Combine(store.BackupsDirectory, "current.2.sav")));
    }
}
