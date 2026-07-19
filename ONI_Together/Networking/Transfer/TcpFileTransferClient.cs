using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking.Components;
using Shared.Profiling;
using System.Security.Cryptography;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Networking.Transfer
{
	public static class TcpFileTransferClient
	{
		public static CancellationTokenSource Download(
			string hostIp,
			int tcpPort,
			ulong clientId,
			string transferToken,
			string expectedFileName,
			int expectedFileSize,
			byte[] expectedFileHash,
			Action<string, byte[]> onComplete,
			Action<string> onError)
		{
			using var _ = Profiler.Scope();
			var cancellation = new CancellationTokenSource();

			Thread thread = new Thread(() => DownloadThread(
				hostIp, tcpPort, clientId, transferToken, expectedFileName,
				expectedFileSize, expectedFileHash, onComplete, onError, cancellation.Token))
			{
				IsBackground = true,
				Name = "TcpFileTransfer_Download"
			};
			thread.Start();
			return cancellation;
		}

		private static void DownloadThread(
			string hostIp,
			int tcpPort,
			ulong clientId,
			string transferToken,
			string expectedFileName,
			int expectedFileSize,
			byte[] expectedFileHash,
			Action<string, byte[]> onComplete,
			Action<string> onError,
			CancellationToken cancellation)
		{
			using var _ = Profiler.Scope();

			try
			{
				(string fileName, byte[] data) = DownloadFile(hostIp, tcpPort, clientId,
					transferToken, expectedFileName, expectedFileSize, expectedFileHash, cancellation);
				if (!cancellation.IsCancellationRequested)
					MainThreadExecutor.dispatcher.QueueEvent(() => onComplete(fileName, data));
			}
			catch (Exception ex)
			{
				if (cancellation.IsCancellationRequested)
					return;
				DebugConsole.LogError($"[TcpFileTransfer] Download failed: {ex.Message}", false);
				MainThreadExecutor.dispatcher.QueueEvent(() => onError(ex.Message));
			}
		}

		private static (string FileName, byte[] Data) DownloadFile(
			string hostIp, int tcpPort, ulong clientId, string transferToken,
			string expectedFileName, int expectedFileSize, byte[] expectedFileHash,
			CancellationToken cancellation)
		{
			using var client = Connect(hostIp, tcpPort, cancellation);
			using CancellationTokenRegistration registration = cancellation.Register(client.Close);
			NetworkStream stream = client.GetStream();
			stream.ReadTimeout = Configuration.Instance.Client.TimeoutSeconds * 1000;
			WriteHandshake(stream, clientId, transferToken);
			(string fileName, int fileSize) = ReadAndValidateMetadata(
				stream, expectedFileName, expectedFileSize, expectedFileHash);
			byte[] data = ReadFile(stream, fileSize, cancellation);
			using SHA256 sha = SHA256.Create();
			if (!HashesEqual(sha.ComputeHash(data), expectedFileHash))
				throw new InvalidDataException("TCP save SHA-256 mismatch");
			DebugConsole.Log($"[TcpFileTransfer] Download complete: '{fileName}' ({data.Length} bytes)");
			return (fileName, data);
		}

		private static TcpClient Connect(string hostIp, int tcpPort, CancellationToken cancellation)
		{
			var client = new TcpClient { ReceiveBufferSize = 65536 };
			try
			{
				IAsyncResult result = client.BeginConnect(hostIp, tcpPort, null, null);
				int signaled = WaitHandle.WaitAny(
					new[] { result.AsyncWaitHandle, cancellation.WaitHandle },
					TimeSpan.FromSeconds(Configuration.Instance.Client.TimeoutSeconds));
				if (signaled == WaitHandle.WaitTimeout)
					throw new TimeoutException("TCP connection timed out");
				cancellation.ThrowIfCancellationRequested();
				client.EndConnect(result);
				return client;
			}
			catch
			{
				client.Dispose();
				throw;
			}
		}

		private static void WriteHandshake(NetworkStream stream, ulong clientId, string transferToken)
		{
			byte[] tokenBytes = Encoding.UTF8.GetBytes(transferToken ?? string.Empty);
			if (tokenBytes.Length == 0 || tokenBytes.Length > SecureTransferPacket.MaxTransferIdChars)
				throw new InvalidDataException("Invalid TCP transfer token");
			byte[] idBytes = BitConverter.GetBytes(clientId);
			stream.Write(idBytes, 0, idBytes.Length);
			byte[] tokenLength = BitConverter.GetBytes(tokenBytes.Length);
			stream.Write(tokenLength, 0, tokenLength.Length);
			stream.Write(tokenBytes, 0, tokenBytes.Length);
			stream.Flush();
		}

		private static (string FileName, int FileSize) ReadAndValidateMetadata(
			NetworkStream stream, string expectedFileName, int expectedFileSize, byte[] expectedFileHash)
		{
			int fileNameLength = BitConverter.ToInt32(ReadExact(stream, 4), 0);
			if (fileNameLength <= 0 || fileNameLength > SaveFileChunkPacket.MaxFileNameChars * 4)
				throw new InvalidDataException($"Invalid TCP filename length: {fileNameLength}");
			string fileName = Encoding.UTF8.GetString(ReadExact(stream, fileNameLength));
			int fileSize = BitConverter.ToInt32(ReadExact(stream, 4), 0);
			if (fileSize <= 0 || fileSize > SaveFileChunkPacket.MaxSaveBytes)
				throw new InvalidDataException($"Invalid TCP save size: {fileSize}");
			byte[] fileHash = ReadExact(stream, 32);
			if (!string.Equals(fileName, expectedFileName, StringComparison.Ordinal)
			    || fileSize != expectedFileSize || !HashesEqual(fileHash, expectedFileHash))
				throw new InvalidDataException("TCP transfer metadata does not match authenticated start packet");
			return (fileName, fileSize);
		}

		private static byte[] ReadFile(NetworkStream stream, int fileSize, CancellationToken cancellation)
		{
			DebugConsole.Log($"[TcpFileTransfer] Downloading {fileSize} bytes");
			byte[] data = new byte[fileSize];
			int received = 0;
			var stopwatch = System.Diagnostics.Stopwatch.StartNew();
			long lastBytes = 0;
			double lastUpdate = 0;
			while (received < fileSize)
			{
				cancellation.ThrowIfCancellationRequested();
				int count = stream.Read(data, received, Math.Min(65536, fileSize - received));
				if (count == 0)
					throw new IOException("Connection closed during transfer");
				received += count;
				QueueProgress(fileSize, received, stopwatch.Elapsed.TotalSeconds,
					ref lastBytes, ref lastUpdate);
			}
			return data;
		}

		private static void QueueProgress(
			int fileSize, int received, double elapsed, ref long lastBytes, ref double lastUpdate)
		{
			if (elapsed - lastUpdate < 0.5 && received != fileSize)
				return;
			double deltaTime = elapsed - lastUpdate;
			double speed = deltaTime > 0 ? (received - lastBytes) / deltaTime : 0;
			double remaining = speed > 0 ? (fileSize - received) / speed : 0;
			lastBytes = received;
			lastUpdate = elapsed;
			int percent = (int)((double)received * 100 / fileSize);
			MainThreadExecutor.dispatcher.QueueEvent(() =>
			{
				string message = string.Format(STRINGS.UI.MP_OVERLAY.CLIENT.TCP_DOWNLOADING_SAVE_FILE,
					CreateClientProgressBar(percent), percent, FormatTime(remaining));
				MultiplayerOverlay.Show(message);
			});
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

		private static bool HashesEqual(byte[] left, byte[] right)
		{
			if (left == null || right == null || left.Length != right.Length)
				return false;
			int difference = 0;
			for (int i = 0; i < left.Length; i++)
				difference |= left[i] ^ right[i];
			return difference == 0;
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
