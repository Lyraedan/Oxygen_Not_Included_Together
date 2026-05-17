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

		public void QueueTransfer(ulong clientId, string fileName, byte[] data)
		{
			using var _ = Profiler.Scope();

			_pending[clientId] = new PendingTransfer { FileName = fileName, Data = data };
			DebugConsole.Log($"[TcpFileTransfer] Queued transfer '{fileName}' ({data.Length} bytes) for client {clientId}");
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

					byte[] idBuffer = new byte[8];
					int read = 0;
					while (read < 8)
					{
						int n = stream.Read(idBuffer, read, 8 - read);
						if (n == 0)
							throw new IOException("Client disconnected before sending ID");
						read += n;
					}

					ulong clientId = BitConverter.ToUInt64(idBuffer, 0);
					DebugConsole.Log($"[TcpFileTransfer] Client {clientId} connected for download");

					PendingTransfer transfer;
					if (!_pending.TryRemove(clientId, out transfer))
					{
						DebugConsole.LogWarning($"[TcpFileTransfer] No pending transfer for client {clientId}");
						return;
					}

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
