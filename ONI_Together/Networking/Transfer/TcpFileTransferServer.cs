using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using System.Security.Cryptography;

namespace ONI_Together.Networking.Transfer
{
	public class TcpFileTransferServer
	{
		private TcpListener _listener;
		private Thread _acceptThread;
		private volatile bool _running;
		private const int MaxConcurrentHandlers = 16;
		private const int SocketOperationTimeoutMilliseconds = 10000;
		private static readonly TimeSpan HandshakeLifetime = TimeSpan.FromSeconds(10);
		private static readonly TimeSpan TransferLifetime = TimeSpan.FromMinutes(5);
		private int _activeHandlers;
		private static readonly TimeSpan PendingTransferLifetime = TimeSpan.FromMinutes(2);
		private readonly ConcurrentDictionary<string, PendingTransfer> _pending = new ConcurrentDictionary<string, PendingTransfer>();

		private class PendingTransfer
		{
			public ulong ClientId;
			public string FileName;
			public byte[] Data;
			public byte[] FileHash;
			public System.DateTime CreatedAtUtc;
		}

		public void Start(int riptidePort)
		{
			using var _ = Profiler.Scope();

			int tcpPort = riptidePort + 1;
			_listener = new TcpListener(IPAddress.Any, tcpPort);
			_listener.Start();
			_running = true;

			_acceptThread = new Thread(AcceptLoop)
			{
				IsBackground = true,
				Name = "TcpFileTransfer_Accept"
			};
			_acceptThread.Start();

			DebugConsole.Log($"[TcpFileTransfer] Server started on port {tcpPort}");
		}

		public string QueueTransfer(ulong clientId, string fileName, byte[] data)
		{
			using var _ = Profiler.Scope();
			if (string.IsNullOrEmpty(fileName) || fileName.Length > SaveFileChunkPacket.MaxFileNameChars
			    || data == null || data.Length <= 0 || data.Length > SaveFileChunkPacket.MaxSaveBytes)
				throw new InvalidDataException("Invalid TCP save transfer");

			PurgeExpiredTransfers();
			string token = Guid.NewGuid().ToString("N");
			byte[] hash;
			using (SHA256 sha = SHA256.Create())
				hash = sha.ComputeHash(data);
			_pending[BuildPendingKey(clientId, token)] = new PendingTransfer
			{
				ClientId = clientId,
				FileName = fileName,
				Data = data,
				FileHash = hash,
				CreatedAtUtc = System.DateTime.UtcNow
			};
			DebugConsole.Log($"[TcpFileTransfer] Queued transfer '{fileName}' ({data.Length} bytes) for client {clientId}");
			return token;
		}

		private static string BuildPendingKey(ulong clientId, string token) => clientId + ":" + token;

		public bool CancelTransfer(ulong clientId, string token)
		{
			return clientId != 0 && !string.IsNullOrEmpty(token)
			       && _pending.TryRemove(BuildPendingKey(clientId, token), out _);
		}

		public void CancelTransfers(ulong clientId)
		{
			foreach (var entry in _pending)
			{
				if (entry.Value.ClientId == clientId)
					_pending.TryRemove(entry.Key, out _);
			}
		}

		private void PurgeExpiredTransfers()
		{
			System.DateTime now = System.DateTime.UtcNow;
			foreach (var entry in _pending)
			{
				if (now - entry.Value.CreatedAtUtc > PendingTransferLifetime)
					_pending.TryRemove(entry.Key, out _);
			}
		}

		private void AcceptLoop()
		{
			while (_running)
			{
				using var _ = Profiler.Scope();

				try
				{
					TcpClient client = _listener.AcceptTcpClient();
					if (Interlocked.Increment(ref _activeHandlers) > MaxConcurrentHandlers)
					{
						Interlocked.Decrement(ref _activeHandlers);
						client.Close();
						DebugConsole.LogWarning("[TcpFileTransfer] Rejected connection: handler limit reached");
						continue;
					}

					ThreadPool.QueueUserWorkItem(_ =>
					{
						try
						{
							HandleClient(client);
						}
						finally
						{
							Interlocked.Decrement(ref _activeHandlers);
						}
					});
				}
				catch (SocketException) when (!_running)
				{
					break;
				}
				catch (Exception ex)
				{
					if (_running)
						DebugConsole.LogError($"[TcpFileTransfer] Accept error: {ex.Message}", false);
				}
			}
		}

		private void HandleClient(TcpClient client)
		{
			using var _ = Profiler.Scope();

			try
			{
				using (client)
					ServeClient(client);
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[TcpFileTransfer] Handler error: {ex.Message}", false);
			}
		}

		private void ServeClient(TcpClient client)
		{
			client.SendBufferSize = 65536;
			client.NoDelay = true;
			NetworkStream stream = client.GetStream();
			Stopwatch handshakeClock = Stopwatch.StartNew();
			(ulong clientId, string token) = ReadHandshake(stream, handshakeClock);
			DebugConsole.Log($"[TcpFileTransfer] Client {clientId} connected for download");
			PurgeExpiredTransfers();
			if (!_pending.TryRemove(BuildPendingKey(clientId, token), out PendingTransfer transfer))
			{
				DebugConsole.LogWarning($"[TcpFileTransfer] No pending transfer for client {clientId}");
				return;
			}
			SendTransfer(stream, transfer, Stopwatch.StartNew());
			DebugConsole.Log(
				$"[TcpFileTransfer] Sent '{transfer.FileName}' ({transfer.Data.Length} bytes) to client {clientId}");
		}

		private static (ulong ClientId, string Token) ReadHandshake(
			NetworkStream stream, Stopwatch clock)
		{
			ulong clientId = BitConverter.ToUInt64(
				ReadExact(stream, sizeof(ulong), clock, HandshakeLifetime), 0);
			int tokenLength = BitConverter.ToInt32(
				ReadExact(stream, sizeof(int), clock, HandshakeLifetime), 0);
			if (tokenLength <= 0 || tokenLength > SecureTransferPacket.MaxTransferIdChars)
				throw new InvalidDataException("Invalid TCP transfer token length");
			return (clientId, Encoding.UTF8.GetString(
				ReadExact(stream, tokenLength, clock, HandshakeLifetime)));
		}

		private static void SendTransfer(
			NetworkStream stream, PendingTransfer transfer, Stopwatch clock)
		{
			byte[] fileNameBytes = Encoding.UTF8.GetBytes(transfer.FileName);
			WriteBounded(stream, BitConverter.GetBytes(fileNameBytes.Length), clock);
			WriteBounded(stream, fileNameBytes, clock);
			WriteBounded(stream, BitConverter.GetBytes(transfer.Data.Length), clock);
			WriteBounded(stream, transfer.FileHash, clock);
			WriteBounded(stream, transfer.Data, clock);
			stream.Flush();
		}

		private static byte[] ReadExact(
			NetworkStream stream, int count, Stopwatch clock, TimeSpan lifetime)
		{
			byte[] buffer = new byte[count];
			int read = 0;
			while (read < count)
			{
				stream.ReadTimeout = RequireRemainingTimeout(clock.Elapsed, lifetime);
				int current = stream.Read(buffer, read, count - read);
				if (current == 0)
					throw new IOException("Client disconnected during TCP transfer handshake");
				read += current;
			}
			return buffer;
		}

		private static void WriteBounded(
			NetworkStream stream, byte[] data, Stopwatch clock)
		{
			const int chunkSize = 64 * 1024;
			for (int offset = 0; offset < data.Length; offset += chunkSize)
			{
				stream.WriteTimeout = RequireRemainingTimeout(clock.Elapsed, TransferLifetime);
				int count = Math.Min(chunkSize, data.Length - offset);
				stream.Write(data, offset, count);
			}
		}

		private static int RequireRemainingTimeout(TimeSpan elapsed, TimeSpan lifetime)
		{
			int timeout = CalculateTimeoutMilliseconds(elapsed, lifetime);
			if (timeout <= 0)
				throw new TimeoutException("TCP transfer exceeded its absolute deadline");
			return timeout;
		}

		internal static int CalculateTimeoutMilliseconds(TimeSpan elapsed, TimeSpan lifetime)
		{
			TimeSpan remaining = lifetime - elapsed;
			if (remaining <= TimeSpan.Zero)
				return 0;
			return Math.Min(SocketOperationTimeoutMilliseconds,
				Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds)));
		}

		public void Stop()
		{
			using var _ = Profiler.Scope();

			_running = false;

			try
			{
				_listener?.Stop();
			}
			catch (Exception) { }

			_pending.Clear();
			DebugConsole.Log("[TcpFileTransfer] Server stopped");
		}
	}
}
