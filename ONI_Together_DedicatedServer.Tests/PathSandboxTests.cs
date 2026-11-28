using System;
using System.IO;
using ONI_Together_DedicatedServer.Security;
using Xunit;

public class PathSandboxTests
{
    [Fact]
    public void ResolveInsideRoot_allows_file_inside_root()
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string? resolved = PathSandbox.ResolveInsideRoot(root, Path.Combine(root, "sub", "save.sav"));
            Assert.NotNull(resolved);
            Assert.StartsWith(Path.GetFullPath(root), resolved!, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escape.sav")]
    [InlineData("..\\escape.sav")]
    [InlineData("a/../../escape.sav")]
    public void ResolveInsideRoot_rejects_traversal(string candidate)
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string? resolved = PathSandbox.ResolveInsideRoot(root, candidate);
            Assert.Null(resolved);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void ResolveInsideRoot_rejects_sibling_root_prefix()
    {
        string root = Path.Combine(Path.GetTempPath(), "root");
        string sibling = Path.Combine(Path.GetTempPath(), "root2");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(PathSandbox.ResolveInsideRoot(root, Path.Combine(sibling, "x.sav")));
        }
        finally { try { Directory.Delete(root, true); } catch { } try { Directory.Delete(sibling, true); } catch { } }
    }

    [Theory]
    [InlineData("save.sav", true)]
    [InlineData("current.sav", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("a/b.sav", false)]
    [InlineData("a\\b.sav", false)]
    [InlineData("..", false)]
    [InlineData("a..b.sav", false)]
    [InlineData("save\0.sav", false)]
    public void IsSafeFileName_works(string name, bool expected)
    {
        Assert.Equal(expected, PathSandbox.IsSafeFileName(name));
    }
}
