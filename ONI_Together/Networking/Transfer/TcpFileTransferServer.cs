using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Transfer
{
	public class TcpFileTransferServer
	{
		private TcpListener _listener;
		private Thread _acceptThread;
		private volatile bool _running;
		private readonly ConcurrentDictionary<ulong, PendingTransfer> _pending = new ConcurrentDictionary<ulong, PendingTransfer>();

		private class PendingTransfer
		{
			public string FileName;
			public byte[] Data;
			public string Token;
		}

		/// <summary>The TCP port the server is listening on, or 0 when stopped.</summary>
		public int Port { get; private set; }

		/// <summary>Starts the server on <paramref name="basePort"/> + 1, matching the LAN convention.</summary>
		public void Start(int basePort)
		{
			using var _ = Profiler.Scope();

			StartOnPort(basePort + 1);
		}

		/// <summary>Starts the server on an explicit TCP port.</summary>
		public void StartOnPort(int port)
		{
			using var _ = Profiler.Scope();

			if (port <= 0 || port > ushort.MaxValue)
				throw new ArgumentOutOfRangeException(nameof(port), port, "TCP file transfer port is out of range.");

			Stop();

			Port = port;
			_listener = new TcpListener(IPAddress.Any, port);
			_listener.Start();
			_running = true;

			_acceptThread = new Thread(AcceptLoop)
			{
				IsBackground = true,
				Name = "TcpFileTransfer_Accept"
			};
			_acceptThread.Start();

			DebugConsole.Log($"[TcpFileTransfer] Server started on port {port}");
		}

		/// <summary>
		/// Queues a save for one client and returns the one-time token the client must
		/// present when it connects.
		/// </summary>
		public string QueueTransfer(ulong clientId, string fileName, byte[] data)
		{
			using var _ = Profiler.Scope();

			string token = Guid.NewGuid().ToString("N");
			_pending[clientId] = new PendingTransfer { FileName = fileName, Data = data, Token = token };
			DebugConsole.Log($"[TcpFileTransfer] Queued transfer '{fileName}' ({data.Length} bytes) for client {clientId}");
			return token;
		}

		/// <summary>Drops a queued transfer, for example when the start packet could not be sent.</summary>
		public void CancelTransfer(ulong clientId)
		{
			using var _ = Profiler.Scope();

			_pending.TryRemove(clientId, out var _removed);
		}

		private void AcceptLoop()
		{
			while (_running)
			{
				using var _ = Profiler.Scope();

				try
				{
					TcpClient client = _listener.AcceptTcpClient();
					Thread handler = new Thread(() => HandleClient(client))
					{
						IsBackground = true,
						Name = "TcpFileTransfer_Handler"
					};
					handler.Start();
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
				{
					client.SendBufferSize = 65536;
					NetworkStream stream = client.GetStream();
					stream.ReadTimeout = 10000;

					byte[] idBuffer = ReadExact(stream, 8);
					ulong clientId = BitConverter.ToUInt64(idBuffer, 0);

					byte[] tokenLengthBuffer = ReadExact(stream, 4);
					int tokenLength = BitConverter.ToInt32(tokenLengthBuffer, 0);
					if (tokenLength < 0 || tokenLength > 256)
						throw new IOException("Invalid transfer token length.");

					string token = Encoding.UTF8.GetString(ReadExact(stream, tokenLength));

					DebugConsole.Log($"[TcpFileTransfer] Client {clientId} connected for download");

					PendingTransfer transfer;
					if (!_pending.TryGetValue(clientId, out transfer))
					{
						DebugConsole.LogWarning($"[TcpFileTransfer] No pending transfer for client {clientId}");
						return;
					}

					if (!string.Equals(transfer.Token, token, StringComparison.Ordinal))
					{
						// Do not consume the real transfer when a peer guesses the id.
						DebugConsole.LogWarning($"[TcpFileTransfer] Invalid transfer token for client {clientId}");
						return;
					}

					_pending.TryRemove(clientId, out var _removed);

					byte[] fileNameBytes = Encoding.UTF8.GetBytes(transfer.FileName);
					byte[] fileNameLenBytes = BitConverter.GetBytes(fileNameBytes.Length);
					byte[] fileSizeBytes = BitConverter.GetBytes(transfer.Data.Length);

					stream.Write(fileNameLenBytes, 0, 4);
					stream.Write(fileNameBytes, 0, fileNameBytes.Length);
					stream.Write(fileSizeBytes, 0, 4);
					stream.Write(transfer.Data, 0, transfer.Data.Length);
					stream.Flush();

					DebugConsole.Log($"[TcpFileTransfer] Sent '{transfer.FileName}' ({transfer.Data.Length} bytes) to client {clientId}");
				}
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[TcpFileTransfer] Handler error: {ex.Message}", false);
			}
		}

		private static byte[] ReadExact(NetworkStream stream, int count)
		{
			byte[] buffer = new byte[count];
			int read = 0;
			while (read < count)
			{
				int n = stream.Read(buffer, read, count - read);
				if (n == 0)
					throw new IOException("Connection closed while reading");
				read += n;
			}
			return buffer;
		}

		public void Stop()
		{
			using var _ = Profiler.Scope();

			if (!_running && _listener == null)
				return;

			_running = false;
			Port = 0;

			try
			{
				_listener?.Stop();
			}
			catch (Exception) { }

			_listener = null;
			_pending.Clear();
			DebugConsole.Log("[TcpFileTransfer] Server stopped");
		}
	}
}
