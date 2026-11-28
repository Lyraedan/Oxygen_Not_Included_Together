using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ONI_Together_DedicatedServer.Oni
{
    /// <summary>
    /// Launches a normal ONI Unity process headlessly (`-batchmode -nographics`).
    /// IMPORTANT: this is NOT a server-authoritative ONI simulation backend.
    /// It does not inject ONI_Together, remove master election/migration, verify Klei DLC,
    /// drive server-authoritative actions, or own world state. Current relay remains authoritative-less.
    /// Provided as a concrete runtime-integration scaffold; not claimed complete.
    /// </summary>
    public sealed class HeadlessOniSimulationBackend : IDisposable
    {
        private readonly OniRuntimeDetector _runtime;
        private Process? _process;

        public HeadlessOniSimulationBackend(OniRuntimeDetector? runtime = null)
        {
            _runtime = runtime ?? OniRuntimeDetector.Detect();
        }

        public bool CanRun => _runtime.IsOniInstallation && !string.IsNullOrWhiteSpace(_runtime.GameExecutable);
        public bool IsRunning => _process != null && !_process.HasExited;
        public string? GameExecutable => _runtime.GameExecutable;
        public string? OniRoot => _runtime.Root;
        public int? ExitCode => _process?.HasExited == true ? _process.ExitCode : null;
        public string? LogFile { get; private set; }

        /// <summary>Build ProcessStartInfo. Does NOT launch. Honest headless Unity flags, not authoritative mode.</summary>
        public ProcessStartInfo BuildStartInfo(string? logFile = null, IEnumerable<string>? extraArgs = null)
        {
            if (!CanRun || string.IsNullOrWhiteSpace(_runtime.GameExecutable))
                throw new InvalidOperationException("ONI runtime/game files not found. Set ONI_PATH/ONI_GAME_DIR or GameLibsFolder.");

            string log = logFile ?? Path.Combine(Path.GetTempPath(), $"oni-headless-{Guid.NewGuid():N}.log");
            LogFile = log;

            var psi = new ProcessStartInfo
            {
                FileName = _runtime.GameExecutable,
                WorkingDirectory = _runtime.Root ?? Path.GetDirectoryName(_runtime.GameExecutable) ?? "",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Unity headless. NOT server-authoritative. No Klei verification.
            psi.ArgumentList.Add("-batchmode");
            psi.ArgumentList.Add("-nographics");
            psi.ArgumentList.Add("-logFile");
            psi.ArgumentList.Add(log);
            if (extraArgs != null)
                foreach (var a in extraArgs) if (!string.IsNullOrWhiteSpace(a)) psi.ArgumentList.Add(a);

            return psi;
        }

        /// <summary>Launch headless ONI. NOT authoritative; caller must not treat as server-authoritative.</summary>
        public bool Start(string? logFile = null, IEnumerable<string>? extraArgs = null)
        {
            if (IsRunning) return true;
            var psi = BuildStartInfo(logFile, extraArgs);
            _process = Process.Start(psi);
            return _process != null;
        }

        public void Stop(int millisecondsTimeout = 5000)
        {
            if (_process == null) return;
            try
            {
                if (!_process.HasExited)
                {
                    try { _process.Kill(entireProcessTree: true); } catch { try { _process.Kill(); } catch { } }
                    try { _process.WaitForExit(millisecondsTimeout); } catch { }
                }
            }
            finally { _process.Dispose(); _process = null; }
        }

        public void Dispose() => Stop();
    }
}
