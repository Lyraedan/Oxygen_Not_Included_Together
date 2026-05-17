using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Components;
using Shared.Profiling;

namespace ONI_Together.Networking.Transfer
{
	public static class TcpFileTransferClient
	{
		public static void Download(string hostIp, int tcpPort, ulong clientId, Action<string, byte[]> onComplete, Action<string> onError)
		{
			using var _ = Profiler.Scope();

			Thread thread = new Thread(() => DownloadThread(hostIp, tcpPort, clientId, onComplete, onError))
			{
				IsBackground = true,
				Name = "TcpFileTransfer_Download"
			};
			thread.Start();
		}

		private static void DownloadThread(string hostIp, int tcpPort, ulong clientId, Action<string, byte[]> onComplete, Action<string> onError)
		{
			using var _ = Profiler.Scope();

			try
			{
				using (TcpClient client = new TcpClient())
				{
					client.ReceiveBufferSize = 65536;
					IAsyncResult ar = client.BeginConnect(hostIp, tcpPort, null, null);
					if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(Configuration.Instance.Client.TimeoutSeconds)))
					{
						throw new TimeoutException("TCP connection timed out");
					}
					client.EndConnect(ar);

					NetworkStream stream = client.GetStream();
					stream.ReadTimeout = Configuration.Instance.Client.TimeoutSeconds * 1000;

					byte[] idBytes = BitConverter.GetBytes(clientId);
					stream.Write(idBytes, 0, 8);
					stream.Flush();

					byte[] fileNameLenBuf = ReadExact(stream, 4);
					int fileNameLen = BitConverter.ToInt32(fileNameLenBuf, 0);
					byte[] fileNameBuf = ReadExact(stream, fileNameLen);
					string fileName = Encoding.UTF8.GetString(fileNameBuf);

					byte[] fileSizeBuf = ReadExact(stream, 4);
					int fileSize = BitConverter.ToInt32(fileSizeBuf, 0);

					DebugConsole.Log($"[TcpFileTransfer] Downloading '{fileName}' ({fileSize} bytes)");

					byte[] data = new byte[fileSize];
					int received = 0;

					var stopwatch = System.Diagnostics.Stopwatch.StartNew();
					long lastBytes = 0;
					double lastUpdate = 0;

                    while (received < fileSize)
                    {
                        int toRead = Math.Min(65536, fileSize - received);
                        int n = stream.Read(data, received, toRead);
                        if (n == 0) throw new IOException("Connection closed during transfer");
                        received += n;

                        double elapsed = stopwatch.Elapsed.TotalSeconds;
                        if (elapsed - lastUpdate >= 0.5 || received == fileSize) // update every 0.5s or at end
                        {
                            double deltaTime = elapsed - lastUpdate;
                            long bytesDelta = received - lastBytes;
                            double speed = deltaTime > 0 ? bytesDelta / deltaTime : 0;
                            double timeRemaining = speed > 0 ? (fileSize - received) / speed : 0;

                            lastBytes = received;
                            lastUpdate = elapsed;

                            int percent = (int)((double)received * 100 / fileSize);
                            MainThreadExecutor.dispatcher.QueueEvent(() =>
                            {
                                var bar = CreateClientProgressBar(percent);
                                string message = string.Format(
                                    STRINGS.UI.MP_OVERLAY.CLIENT.TCP_DOWNLOADING_SAVE_FILE,
                                    bar,
                                    percent,
                                    FormatTime(timeRemaining)
                                );
                                MultiplayerOverlay.Show(message);
                            });
                        }
                    }

                    stopwatch.Stop();

                    DebugConsole.Log($"[TcpFileTransfer] Download complete: '{fileName}' ({received} bytes)");

					MainThreadExecutor.dispatcher.QueueEvent(() =>
					{
						onComplete(fileName, data);
					});
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[TcpFileTransfer] Download failed: {ex.Message}", false);
				MainThreadExecutor.dispatcher.QueueEvent(() =>
				{
					onError(ex.Message);
				});
			}
		}

		private static byte[] ReadExact(NetworkStream stream, int count)
		{
			using var _ = Profiler.Scope();

			byte[] buf = new byte[count];
			int read = 0;
			while (read < count)
			{
				int n = stream.Read(buf, read, count - read);
				if (n == 0)
					throw new IOException("Connection closed while reading");
				read += n;
			}
			return buf;
		}

        private static string CreateClientProgressBar(int percent)
        {
	        using var _ = Profiler.Scope();

            int barLength = 30;  // Larger bar for the client
            int filled = (percent * barLength) / 100;
            string bar = "";

            for (int i = 0; i < barLength; i++)
            {
                if (i < filled)
                    bar += STRINGS.UI.MP_OVERLAY.SYNC.PROGRESS_BAR_FILLED;  // Filled
                else
                    bar += STRINGS.UI.MP_OVERLAY.SYNC.PROGRESS_BAR_EMPTY;  // Empty
            }

            return string.Format(STRINGS.UI.MP_OVERLAY.SYNC.PROGRESS_BAR, bar);
        }

        private static string FormatTime(double seconds)
        {
	        using var _ = Profiler.Scope();

            if (double.IsInfinity(seconds) || seconds < 0)
                return "--";

			int roundedSeconds = (int)Math.Ceiling(seconds);
            TimeSpan t = TimeSpan.FromSeconds(roundedSeconds);

			//if (seconds < 1)
			//	return $"{seconds:0.00}s"; // milliseconds (replaced by rounding up to the next second)


            if (t.TotalHours >= 1)
                return $"{(int)t.TotalHours}h {t.Minutes}m";
            if (t.TotalMinutes >= 1)
                return $"{t.Minutes}m {t.Seconds}s";

            return $"{t.Seconds}s";
        }
    }
}
