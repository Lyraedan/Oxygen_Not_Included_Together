using System;
using System.IO;
using ONI_Together_DedicatedServer.Oni;
using Xunit;

public class OniRuntimeDetectorTests
{
    [Fact]
    public void Detects_ONI_layout_when_assembly_and_executable_present()
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-runtime-" + Guid.NewGuid().ToString("N"));
        string managed = Path.Combine(root, "OxygenNotIncluded_Data", "Managed");
        Directory.CreateDirectory(managed);
        File.WriteAllBytes(Path.Combine(managed, "Assembly-CSharp.dll"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(root, "OxygenNotIncluded.exe"), new byte[] { 0 });
        try
        {
            var d = new OniRuntimeDetector(root);
            Assert.True(d.HasAssemblyCSharp);
            Assert.True(d.HasGameExecutable);
            Assert.True(d.IsOniInstallation);
            Assert.Contains("ONI runtime found", d.Report());
            Assert.Contains("not headless-authoritative", d.Report(), StringComparison.OrdinalIgnoreCase);
            // honesty: does not claim Klei ownership
            Assert.Contains("does not verify Klei ownership", d.Report(), StringComparison.OrdinalIgnoreCase);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Not_installation_when_missing_assembly_or_executable()
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var d = new OniRuntimeDetector(root);
            Assert.False(d.IsOniInstallation);
            Assert.False(d.HasAssemblyCSharp);
            Assert.False(d.HasGameExecutable);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Empty_root_not_installation()
    {
        var d = new OniRuntimeDetector("");
        Assert.False(d.IsOniInstallation);
        Assert.Null(d.Root);
    }
}
