using System.Text;
using ONI_Together_DedicatedServer.ONI;
using ONI_Together_DedicatedServer.Security;
using Xunit;

public sealed class ColonyDlcTests
{
    internal static byte[] FakeSave(string dlcJson, string name = "Test Colony")
    {
        byte[] json = Encoding.UTF8.GetBytes($"{{\"numberOfCycles\":42,\"numberOfDuplicants\":7,\"baseName\":\"{name}\",\"dlcIds\":{dlcJson}}}");
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(744825);
        w.Write(json.Length);
        w.Write(1);
        w.Write(1);
        w.Write(json);
        w.Write(new byte[2000]);
        return ms.ToArray();
    }

    [Fact]
    public void Reads_the_header_and_drops_the_base_game_id()
    {
        var header = SaveHeader.TryRead(FakeSave("[\"\",\"EXPANSION1_ID\",\"DLC2_ID\"]"));

        Assert.NotNull(header);
        Assert.Equal("Test Colony", header!.BaseName);
        Assert.Equal(42, header.Cycles);
        Assert.Equal(7, header.Duplicants);
        Assert.Equal(new[] { "EXPANSION1_ID", "DLC2_ID" }, header.DlcIds);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 })]
    public void Garbage_is_not_a_header(byte[] data)
    {
        Assert.Null(SaveHeader.TryRead(data));
    }

    [Fact]
    public void Reads_a_real_save_when_one_is_installed()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Klei", "OxygenNotIncluded", "save_files");
        string? save = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.sav", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (save == null)
            return;

        var header = SaveHeader.TryRead(File.ReadAllBytes(save));

        Assert.NotNull(header);
        Assert.False(string.IsNullOrEmpty(header!.BaseName));
    }

    private static string[] L(params string[] ids) => ids;

    [Fact]
    public void Base_game_colony()
    {
        Assert.True(ColonyDlcCheck.Check(L(), L(), L()).Allowed);
        Assert.True(ColonyDlcCheck.Check(L(), L(), L("EXPANSION1_ID")).Allowed);
        Assert.False(ColonyDlcCheck.Check(L(), L("EXPANSION1_ID"), L("EXPANSION1_ID")).Allowed);
    }

    [Fact]
    public void Spaced_out_colony_needs_spaced_out_active()
    {
        Assert.True(ColonyDlcCheck.Check(L("EXPANSION1_ID"), L("EXPANSION1_ID"), L("EXPANSION1_ID")).Allowed);

        var off = ColonyDlcCheck.Check(L("EXPANSION1_ID"), L(), L("EXPANSION1_ID"));
        Assert.False(off.Allowed);
        Assert.Contains("Enable", off.Reason);

        var notOwned = ColonyDlcCheck.Check(L("EXPANSION1_ID"), L(), L());
        Assert.False(notOwned.Allowed);
        Assert.Contains("does not own", notOwned.Reason);
    }

    [Fact]
    public void Content_packs_must_be_owned()
    {
        var missing = ColonyDlcCheck.Check(L("DLC2_ID", "DLC3_ID"), L(), L("DLC2_ID"));
        Assert.False(missing.Allowed);
        Assert.Contains("Bionic Booster", missing.Reason);
        Assert.DoesNotContain("Frosty", missing.Reason);

        Assert.True(ColonyDlcCheck.Check(L("DLC2_ID", "DLC3_ID"), L("DLC3_ID"), L("DLC2_ID")).Allowed);
    }
}
