using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ONI_Together.DebugTools
{
	internal sealed class ConnectionTraceContext
	{
		internal string DirectoryPath { get; set; }
		internal string Role { get; set; }
		internal string Transport { get; set; }
		internal string Target { get; set; }
		internal IReadOnlyDictionary<string, string> Metadata { get; set; }
	}

	internal static class ConnectionTraceLog
	{
		private const long MaxPartBytes = 8L * 1024 * 1024;
		private const int MaxParts = 4;
		private const long MaxTotalBytes = 64L * 1024 * 1024;
		private static readonly ConnectionTraceWriter Writer =
			new ConnectionTraceWriter(MaxPartBytes, MaxParts, MaxTotalBytes);

		internal static string Begin(ConnectionTraceContext context)
		{
			try
			{
				return Writer.Begin(context);
			}
			catch
			{
				Writer.Abort();
				return string.Empty;
			}
		}

		internal static void Append(string level, string message, string stack)
		{
			try
			{
				Writer.Append(level, message, stack);
			}
			catch
			{
				Writer.Abort();
				// Diagnostics must never break the game or recurse through DebugConsole.
			}
		}

		internal static void End(string reason)
		{
			try
			{
				Writer.End(reason);
			}
			catch
			{
				Writer.Abort();
				// The Player.log path remains available when trace storage is unavailable.
			}
		}
	}

	internal sealed class ConnectionTraceWriter : IDisposable
	{
		private const string FilePattern = "connection-*.log";
		private const int MaxMetadataChars = 512;
		private static readonly Encoding Utf8 = new UTF8Encoding(false);
		private readonly object _sync = new object();
		private readonly long _maxPartBytes;
		private readonly int _maxParts;
		private readonly long _maxTotalBytes;
		private readonly long _maxSessionBytes;
		private readonly long _maxEntryBytes;

		private ConnectionTraceContext _context;
		private StreamWriter _writer;
		private Stopwatch _elapsed;
		private System.DateTime _startedUtc;
		private string _traceId;
		private string _currentPath;
		private long _partBytes;
		private int _partNumber;
		private bool _active;
		private bool _accepting;

		internal ConnectionTraceWriter(long maxPartBytes, int maxParts, long maxTotalBytes)
		{
			if (maxPartBytes < 1024 || maxParts < 1 || maxTotalBytes < maxPartBytes * maxParts)
				throw new ArgumentOutOfRangeException(nameof(maxPartBytes));
			_maxPartBytes = maxPartBytes;
			_maxParts = maxParts;
			_maxTotalBytes = maxTotalBytes;
			_maxSessionBytes = maxPartBytes * maxParts;
			_maxEntryBytes = Math.Min(256L * 1024, maxPartBytes / 2);
		}

		internal string Begin(ConnectionTraceContext context)
		{
			if (context == null || string.IsNullOrWhiteSpace(context.DirectoryPath))
				throw new ArgumentException("A trace directory is required.", nameof(context));

			lock (_sync)
			{
				EndLocked("superseded-by-new-session");
				Directory.CreateDirectory(context.DirectoryPath);
				PruneDirectory(context.DirectoryPath, _maxTotalBytes - _maxSessionBytes);
				InitializeSession(context);
				OpenPartLocked();
				return _currentPath;
			}
		}

		internal void Append(string level, string message, string stack)
		{
			lock (_sync)
			{
				if (!_active || !_accepting)
					return;
				WriteEntryLocked(level, message, stack);
			}
		}

		internal void End(string reason)
		{
			lock (_sync)
			{
				EndLocked(reason);
			}
		}

		internal void Abort()
		{
			lock (_sync)
			{
				try { _writer?.Dispose(); }
				catch { }
				_writer = null;
				_active = false;
				_accepting = false;
			}
		}

		public void Dispose()
		{
			lock (_sync)
			{
				EndLocked("writer-disposed");
			}
		}

		private void InitializeSession(ConnectionTraceContext context)
		{
			_context = context;
			_startedUtc = System.DateTime.UtcNow;
			_elapsed = Stopwatch.StartNew();
			_traceId = _startedUtc.ToString("yyyyMMdd'T'HHmmss.fff'Z'", CultureInfo.InvariantCulture)
			           + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
			_partNumber = 1;
			_active = true;
			_accepting = true;
		}

		private void EndLocked(string reason)
		{
			if (!_active)
				return;
			if (_accepting)
				WriteEntryLocked("TRACE", "TRACE_END reason=" + (reason ?? string.Empty), string.Empty);
			CloseWriterLocked();
			_active = false;
			_accepting = false;
			PruneDirectory(_context.DirectoryPath, _maxTotalBytes);
		}

		private void WriteEntryLocked(string level, string message, string stack)
		{
			System.DateTime now = System.DateTime.UtcNow;
			string line = string.Join("\t",
				now.ToString("O", CultureInfo.InvariantCulture),
				_elapsed.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
				Escape(level), Escape(message), Escape(stack));
			line = FitLine(line, _maxEntryBytes);
			long lineBytes = LineBytes(line);
			long limit = CurrentDataLimit();

			if (_partBytes + lineBytes > limit && _partNumber < _maxParts)
			{
				_partNumber++;
				OpenPartLocked();
				limit = CurrentDataLimit();
			}
			if (_partBytes + lineBytes > limit)
			{
				WriteTruncatedMarkerLocked();
				return;
			}
			WriteRawLineLocked(line);
		}

		private long CurrentDataLimit()
		{
			if (_partNumber < _maxParts)
				return _maxPartBytes;
			return _maxPartBytes - LineBytes(TruncatedMarker());
		}

		private void WriteTruncatedMarkerLocked()
		{
			string marker = TruncatedMarker();
			if (_partBytes + LineBytes(marker) <= _maxPartBytes)
				WriteRawLineLocked(marker);
			_accepting = false;
		}

		private string TruncatedMarker()
			=> "# TRACE_TRUNCATED max_session_bytes="
			   + _maxSessionBytes.ToString(CultureInfo.InvariantCulture);

		private void OpenPartLocked()
		{
			CloseWriterLocked();
			string fileName = string.Format(
				CultureInfo.InvariantCulture,
				"connection-{0}-{1}-{2}-{3}-part{4:D2}.log",
				_startedUtc.ToString("yyyyMMdd'T'HHmmss.fff'Z'", CultureInfo.InvariantCulture),
				FileSegment(_context.Role), FileSegment(_context.Transport),
				_traceId.Substring(_traceId.Length - 8), _partNumber);
			_currentPath = Path.Combine(_context.DirectoryPath, fileName);
			var stream = new FileStream(_currentPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			_writer = new StreamWriter(stream, Utf8) { AutoFlush = true, NewLine = "\n" };
			_partBytes = 0;
			WriteHeaderLocked();
		}

		private void WriteHeaderLocked()
		{
			WriteHeaderValue("oni_together_connection_trace", "1");
			WriteHeaderValue("trace_id", _traceId);
			WriteHeaderValue("started_utc", _startedUtc.ToString("O", CultureInfo.InvariantCulture));
			WriteHeaderValue("part", $"{_partNumber}/{_maxParts}");
			WriteHeaderValue("role", _context.Role);
			WriteHeaderValue("transport", _context.Transport);
			WriteHeaderValue("target", _context.Target);
			foreach (var pair in (_context.Metadata ?? new Dictionary<string, string>())
			         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
				WriteHeaderValue(pair.Key, pair.Value);
			WriteHeaderValue("limits", $"part_bytes={_maxPartBytes},session_bytes={_maxSessionBytes},total_bytes={_maxTotalBytes}");
			WriteRawLineLocked("timestamp_utc\telapsed_ms\tlevel\tmessage\tstack");
		}

		private void WriteHeaderValue(string key, string value)
		{
			string safeValue = Escape(value);
			if (safeValue.Length > MaxMetadataChars)
				safeValue = safeValue.Substring(0, MaxMetadataChars) + "[TRUNCATED]";
			WriteRawLineLocked("# " + key + "=" + safeValue);
		}

		private void WriteRawLineLocked(string line)
		{
			_writer.WriteLine(line);
			_partBytes += LineBytes(line);
		}

		private void CloseWriterLocked()
		{
			_writer?.Dispose();
			_writer = null;
		}

		private string FitLine(string line, long maxBytes)
		{
			if (LineBytes(line) <= maxBytes)
				return line;
			const string suffix = "[ENTRY_TRUNCATED]";
			int low = 0;
			int high = line.Length;
			while (low < high)
			{
				int middle = low + (high - low + 1) / 2;
				if (LineBytes(line.Substring(0, middle) + suffix) <= maxBytes)
					low = middle;
				else
					high = middle - 1;
			}
			return line.Substring(0, low) + suffix;
		}

		private static string Escape(string value)
			=> (value ?? string.Empty)
				.Replace("\\", "\\\\")
				.Replace("\r", "\\r")
				.Replace("\n", "\\n")
				.Replace("\t", "\\t");

		private static string FileSegment(string value)
		{
			string safe = new string((value ?? "unknown").ToLowerInvariant()
				.Select(character => char.IsLetterOrDigit(character) ? character : '-')
				.ToArray()).Trim('-');
			return string.IsNullOrEmpty(safe) ? "unknown" : safe;
		}

		private static long LineBytes(string line) => Utf8.GetByteCount(line) + 1L;

		private static void PruneDirectory(string directory, long byteLimit)
		{
			if (!Directory.Exists(directory))
				return;
			FileInfo[] files = Directory.EnumerateFiles(directory, FilePattern, SearchOption.TopDirectoryOnly)
				.Select(path => new FileInfo(path))
				.OrderBy(file => file.LastWriteTimeUtc)
				.ThenBy(file => file.Name, StringComparer.Ordinal)
				.ToArray();
			long total = files.Sum(file => file.Length);
			foreach (FileInfo file in files)
			{
				if (total <= byteLimit)
					break;
				long length = file.Length;
				file.Delete();
				total -= length;
			}
		}
	}
}
