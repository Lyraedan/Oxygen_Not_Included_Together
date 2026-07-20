using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ConnectionTraceLogTests
	{
		[UnitTest(name: "Connection traces roll over and respect hard caps", category: "Diagnostics")]
		public static UnitTestResult RolloverAndRetentionCaps()
		{
			string directory = Path.Combine(Path.GetTempPath(), "oni-trace-" + Guid.NewGuid().ToString("N"));
			try
			{
				using var writer = new ConnectionTraceWriter(4096, 2, 16384);
				for (int session = 0; session < 4; session++)
				{
					writer.Begin(new ConnectionTraceContext
					{
						DirectoryPath = directory,
						Role = "client",
						Transport = "test",
						Target = "session-" + session,
						Metadata = new Dictionary<string, string> { ["machine"] = "test" },
					});
					for (int entry = 0; entry < 40; entry++)
						writer.Append("LOG", new string('x', 500), string.Empty);
					writer.End("test-complete");
				}

				FileInfo[] files = Directory.EnumerateFiles(directory, "connection-*.log")
					.Select(path => new FileInfo(path)).ToArray();
				if (files.Length == 0 || files.Any(file => file.Length > 4096))
					return UnitTestResult.Fail("Trace part exceeded the 4096-byte test cap");
				if (files.Sum(file => file.Length) > 16384)
					return UnitTestResult.Fail("Trace directory exceeded the 16384-byte test cap");
				if (!files.Any(file => File.ReadAllText(file.FullName).Contains("TRACE_TRUNCATED")))
					return UnitTestResult.Fail("A capped session did not record truncation");
				return UnitTestResult.Pass("Trace rollover, truncation, and total retention caps held");
			}
			finally
			{
				if (Directory.Exists(directory))
					Directory.Delete(directory, true);
			}
		}
	}
}
