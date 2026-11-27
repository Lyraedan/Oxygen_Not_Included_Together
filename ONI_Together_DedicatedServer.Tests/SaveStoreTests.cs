using System;
using System.IO;
using System.Linq;
using ONI_Together_DedicatedServer.ONI;
using Xunit;

public class SaveStoreTests
{
    [Fact]
    public void SaveCurrent_writes_current_and_rotates_backups()
    {
        string root = NewTemp();
        try
        {
            var store = new SaveStore(root, backupCount: 2);
            string current = Path.Combine(root, "current.sav");

            store.SaveCurrent(new byte[] { 1 });
            store.SaveCurrent(new byte[] { 1, 2 });
            store.SaveCurrent(new byte[] { 1, 2, 3 });

            Assert.True(File.Exists(current));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(current));

            string backups = Path.Combine(root, "backups");
            Assert.True(Directory.Exists(backups));
            var backupsList = Directory.GetFiles(backups, "current.*.sav").OrderBy(f => f).ToList();
            // Retention: at most backupCount.
            Assert.True(backupsList.Count <= 2);
            // current.1 holds the most recent previous data; ordering by name isn't recency.
            string newest = Directory.GetFiles(backups, "current.1.sav").Single();
            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(newest));
        }
        finally { Clean(root); }
    }

    [Fact]
    public void AtomicWrite_rejects_outside_saves_directory()
    {
        string root = NewTemp();
        try
        {
            var store = new SaveStore(root);
            Assert.Throws<InvalidOperationException>(() =>
                store.AtomicWrite(Path.Combine(root, "..", "escape.sav"), new byte[] { 1 }));
        }
        finally { Clean(root); }
    }

    [Fact]
    public void AtomicWrite_rejects_oversize()
    {
        string root = NewTemp();
        try
        {
            var store = new SaveStore(root, maxSaveBytes: 1024);
            Assert.Throws<InvalidOperationException>(() =>
                store.AtomicWrite(Path.Combine(root, "big.sav"), new byte[2048]));
        }
        finally { Clean(root); }
    }

    [Fact]
    public void TryReadSave_rejects_unsafe_name_and_missing()
    {
        string root = NewTemp();
        try
        {
            var store = new SaveStore(root);
            store.SaveCurrent(new byte[] { 7, 7 }, "current");
            Assert.Equal(new byte[] { 7, 7 }, store.TryReadSave("current"));
            Assert.Null(store.TryReadSave("../secret"));
            Assert.Null(store.TryReadSave("missing"));
            Assert.Null(store.TryReadSave("a/b"));
        }
        finally { Clean(root); }
    }

    [Fact]
    public void ListSaves_lists_top_level_sav()
    {
        string root = NewTemp();
        try
        {
            var store = new SaveStore(root);
            store.SaveCurrent(new byte[] { 1 }, "a");
            store.SaveCurrent(new byte[] { 2 }, "b");
            var list = store.ListSaves();
            Assert.Contains(list, p => Path.GetFileName(p) == "a.sav");
            Assert.Contains(list, p => Path.GetFileName(p) == "b.sav");
        }
        finally { Clean(root); }
    }

    private static string NewTemp()
    {
        string d = Path.Combine(Path.GetTempPath(), "oni-ss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
    private static void Clean(string d) { try { Directory.Delete(d, true); } catch { } }
}
