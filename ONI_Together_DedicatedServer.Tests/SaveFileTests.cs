using System;
using System.IO;
using ONI_Together_DedicatedServer.ONI;
using Xunit;

public class SaveFileTests
{
    [Fact]
    public void FromFile_loads_and_reports_size()
    {
        string dir = NewTemp();
        string file = Path.Combine(dir, "test.sav");
        byte[] data = { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(file, data);
        try
        {
            var sf = SaveFile.FromFile(file);
            Assert.Equal("test.sav", sf.Name);
            Assert.Equal(data, sf.Data);
        }
        finally { Clean(dir); }
    }

    [Fact]
    public void FromFile_rejects_missing()
    {
        Assert.Throws<FileNotFoundException>(() => SaveFile.FromFile(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".sav")));
    }

    [Fact]
    public void FromFile_rejects_empty_path()
    {
        Assert.Throws<ArgumentException>(() => SaveFile.FromFile(""));
    }

    [Fact]
    public void SaveToFile_atomic_writes_and_replaces()
    {
        string dir = NewTemp();
        string file = Path.Combine(dir, "out.sav");
        try
        {
            var sf = new SaveFile("out.sav", new byte[] { 9, 9, 9 });
            sf.SaveToFile(file);
            Assert.True(File.Exists(file));
            Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(file));

            sf.Data = new byte[] { 1, 2, 3, 4 };
            sf.SaveToFile(file); // replace
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(file));

            // No leftover temp files.
            Assert.Empty(Directory.GetFiles(dir, "*.tmp-*"));
        }
        finally { Clean(dir); }
    }

    [Fact]
    public void SaveToFile_creates_directory()
    {
        string dir = NewTemp();
        string file = Path.Combine(dir, "nested", "out.sav");
        try
        {
            new SaveFile("out.sav", new byte[] { 1 }).SaveToFile(file);
            Assert.True(File.Exists(file));
        }
        finally { Clean(dir); }
    }

    private static string NewTemp()
    {
        string d = Path.Combine(Path.GetTempPath(), "oni-sf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
    private static void Clean(string d) { try { Directory.Delete(d, true); } catch { } }
}
