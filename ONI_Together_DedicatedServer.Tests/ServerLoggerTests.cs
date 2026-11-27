using System;
using System.IO;
using ONI_Together_DedicatedServer.Logging;
using Xunit;

public class ServerLoggerTests
{
    [Fact]
    public void Writes_to_console_and_file_with_level()
    {
        string dir = Path.Combine(Path.GetTempPath(), "oni-log-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(dir, "server.log");
        try
        {
            var logger = new ServerLogger(file, ServerLogger.Level.Info);
            logger.Debug("hidden");
            logger.Info("hello");
            logger.Error("oops");

            Assert.True(File.Exists(file));
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("hidden", text);
            Assert.Contains("[INFO] hello", text);
            Assert.Contains("[ERROR] oops", text);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ParseLevel_works()
    {
        Assert.Equal(ServerLogger.Level.Warn, ServerLogger.ParseLevel("warn"));
        Assert.Equal(ServerLogger.Level.Info, ServerLogger.ParseLevel("bogus"));
        Assert.Equal(ServerLogger.Level.Info, ServerLogger.ParseLevel(null));
    }

    [Fact]
    public void Logging_never_crashes_on_bad_file()
    {
        var logger = new ServerLogger(Path.Combine(Path.GetTempPath(), "no", "such", "\0bad", "x.log"), ServerLogger.Level.Info);
        logger.Info("x"); // should not throw
    }
}
