using System;
using System.IO;
using ONI_Together_DedicatedServer.Oni;
using Xunit;

public class HeadlessOniSimulationBackendTests
{
    [Fact]
    public void Builds_headless_Unity_startinfo_when_ONI_runtime_present_NOT_authoritative()
    {
        string root = MakeFakeOniRoot();
        try
        {
            var runtime = new OniRuntimeDetector(root);
            var backend = new HeadlessOniSimulationBackend(runtime);

            Assert.True(backend.CanRun);
            Assert.False(backend.IsRunning); // not started
            Assert.Equal(Path.Combine(root, "OxygenNotIncluded.exe"), backend.GameExecutable);

            string log = Path.Combine(root, "oni-headless.log");
            var psi = backend.BuildStartInfo(log);

            Assert.Equal(Path.Combine(root, "OxygenNotIncluded.exe"), psi.FileName);
            Assert.Equal(root, psi.WorkingDirectory);
            Assert.False(psi.UseShellExecute);
            Assert.True(psi.CreateNoWindow);
            Assert.Contains("-batchmode", psi.ArgumentList);
            Assert.Contains("-nographics", psi.ArgumentList);
            Assert.Contains("-logFile", psi.ArgumentList);
            Assert.Contains(log, psi.ArgumentList);
            Assert.Equal(log, backend.LogFile);

            // Honesty: backend is NOT server-authoritative; no Klei verification claimed.
            // (No assertion of authoritative state because none exists.)
            backend.Stop();
            Assert.False(backend.IsRunning);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void CanRun_false_and_throws_when_ONI_runtime_missing()
    {
        var backend = new HeadlessOniSimulationBackend(new OniRuntimeDetector(""));
        Assert.False(backend.CanRun);
        Assert.Throws<InvalidOperationException>(() => backend.BuildStartInfo());
    }

    private static string MakeFakeOniRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "oni-headless-" + Guid.NewGuid().ToString("N"));
        string managed = Path.Combine(root, "OxygenNotIncluded_Data", "Managed");
        Directory.CreateDirectory(managed);
        File.WriteAllBytes(Path.Combine(managed, "Assembly-CSharp.dll"), new byte[] { 0 });
        File.WriteAllBytes(Path.Combine(root, "OxygenNotIncluded.exe"), new byte[] { 0 });
        return root;
    }
}
