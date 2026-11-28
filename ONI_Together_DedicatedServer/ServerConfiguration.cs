using System;
using System.IO;
using Newtonsoft.Json;
using ONI_Together_DedicatedServer.Security;

namespace ONI_Together_DedicatedServer
{
    public class ServerConfiguration
    {
        private static ServerConfiguration? _instance;
        private static string? _configDirOverride;
        private static string? _saveDirOverride;

        public static ServerConfiguration Instance => _instance ??= LoadOrCreate();

        public DedicatedConfig Config { get; private set; } = new DedicatedConfig();

        /// <summary>
        /// Persistent configuration directory resolved outside the executable/mod folder
        /// so updates never overwrite server configuration or save data.
        /// </summary>
        public static string ConfigDirectory
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_configDirOverride))
                    return Path.GetFullPath(_configDirOverride);

                string? envDir = Environment.GetEnvironmentVariable("ONI_SERVER_CONFIG_DIR")
                                 ?? Environment.GetEnvironmentVariable("ONI_DATA_DIR");
                if (!string.IsNullOrWhiteSpace(envDir))
                    return Path.GetFullPath(envDir);

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrWhiteSpace(appData))
                    return Path.Combine(appData, "ONI_Together_DedicatedServer");

                return Path.Combine(AppContext.BaseDirectory, "dedicated_server_config");
            }
        }

        public static string ConfigPath => Path.Combine(ConfigDirectory, "multiplayer_settings.json");

        public static string SavesDirectory
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_saveDirOverride))
                    return Path.GetFullPath(_saveDirOverride);

                string? envDir = Environment.GetEnvironmentVariable("ONI_SERVER_SAVE_DIR");
                if (!string.IsNullOrWhiteSpace(envDir))
                    return Path.GetFullPath(envDir);

                if (_instance != null && !string.IsNullOrWhiteSpace(_instance.Config.SavesDirectory))
                    return Path.GetFullPath(_instance.Config.SavesDirectory);

                return Path.Combine(ConfigDirectory, "saves");
            }
        }

        public static void InitializePaths(string? configDirOverride = null, string? saveDirOverride = null)
        {
            _configDirOverride = string.IsNullOrWhiteSpace(configDirOverride) ? null : configDirOverride;
            _saveDirOverride = string.IsNullOrWhiteSpace(saveDirOverride) ? null : saveDirOverride;
            _instance = null;
        }

        public static ServerConfiguration LoadOrCreate()
        {
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);

            ServerConfiguration result;
            if (!File.Exists(ConfigPath))
            {
                result = new ServerConfiguration();
                result.Save();
            }
            else
            {
                string existingJson = File.ReadAllText(ConfigPath);
                result = JsonConvert.DeserializeObject<ServerConfiguration>(existingJson) ?? new ServerConfiguration();
                result.Config ??= new DedicatedConfig();
            }

            if (!Directory.Exists(SavesDirectory))
                Directory.CreateDirectory(SavesDirectory);

            result.ApplyEnvironmentOverrides();
            result.Config.Sanitize();
            return result;
        }

        public void Save()
        {
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);

            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            // Atomic write to avoid corrupting config on crash.
            string tmp = ConfigPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(tmp, json);
            if (File.Exists(ConfigPath))
            {
                try { File.Replace(tmp, ConfigPath, null, ignoreMetadataErrors: true); }
                catch (PlatformNotSupportedException) { File.Delete(ConfigPath); File.Move(tmp, ConfigPath); }
                catch (IOException) { File.Delete(ConfigPath); File.Move(tmp, ConfigPath); }
            }
            else
            {
                File.Move(tmp, ConfigPath);
            }
        }

        public void Reload()
        {
            if (!File.Exists(ConfigPath))
                throw new FileNotFoundException("Configuration file not found.", ConfigPath);

            string json = File.ReadAllText(ConfigPath);
            var loaded = JsonConvert.DeserializeObject<ServerConfiguration>(json)
                         ?? throw new InvalidOperationException("Failed to deserialize configuration.");

            Config = loaded.Config ?? new DedicatedConfig();
            ApplyEnvironmentOverrides();
            Config.Sanitize();
        }

        public void ApplyEnvironmentOverrides()
        {
            if (TryGetEnv("ONI_IP", out string? ip))
                Config.Ip = ip!;
            if (TryGetEnvInt("ONI_PORT", out int port))
                Config.Port = port;
            if (TryGetEnvInt("ONI_MAX_PLAYERS", out int maxPlayers))
                Config.MaxLobbySize = maxPlayers;
            if (TryGetEnvInt("ONI_TIMEOUT_SECONDS", out int timeout))
                Config.TimeoutSeconds = timeout;
            if (TryGetEnvInt("ONI_TICK_RATE", out int tickRate))
                Config.TickRate = tickRate;
            if (TryGetEnv("ONI_SERVER_NAME", out string? serverName))
                Config.ServerName = serverName!;
            if (TryGetEnv("ONI_SAVE_FILE", out string? saveFile))
                Config.SaveFile = saveFile!;
            if (TryGetEnvBool("ONI_VERBOSE_PACKETS", out bool verbose))
                Config.VerbosePacketLogging = verbose;

            // --- New env overrides (Docker/ZimaOS friendly) ---
            if (TryGetEnvBool("ONI_NON_INTERACTIVE", out bool nonInteractive))
                Config.NonInteractive = nonInteractive;
            if (TryGetEnvInt("ONI_MAX_MESSAGES_PER_POLL", out int maxMessagesPerPoll))
                Config.MaxMessagesPerPoll = maxMessagesPerPoll;
            if (TryGetEnvInt("ONI_MAX_PACKET_SIZE", out int maxPacketSize))
                Config.MaxPacketSize = maxPacketSize;
            if (TryGetEnvInt("ONI_MAX_MESSAGE_SIZE", out int maxMessageSize))
                Config.MaxMessageSize = maxMessageSize;
            if (TryGetEnvInt("ONI_RATE_LIMIT_MESSAGES_PER_SECOND", out int rl))
                Config.RateLimitMessagesPerSecond = rl;
            if (TryGetEnvInt("ONI_SAVE_AUTOSAVE_SECONDS", out int autosave))
                Config.SaveAutosaveSeconds = autosave;
            if (TryGetEnvInt("ONI_SAVE_BACKUP_COUNT", out int backupCount))
                Config.SaveBackupCount = backupCount;
            if (TryGetEnvInt("ONI_MAX_SAVE_BYTES", out int maxSaveBytes))
                Config.MaxSaveBytes = maxSaveBytes;
            if (TryGetEnvBool("ONI_ALLOW_EXTERNAL_SAVE_PATHS", out bool allowExternal))
                Config.AllowExternalSavePaths = allowExternal;
            if (TryGetEnvBool("ONI_PAUSE_WHEN_EMPTY", out bool pauseWhenEmpty))
                Config.PauseWhenEmpty = pauseWhenEmpty;
            if (TryGetEnvBool("ONI_DISCOVERY_ENABLED", out bool discovery))
                Config.DiscoveryEnabled = discovery;
            if (TryGetEnv("ONI_DISCOVERY_TOKEN", out string? discoveryToken))
                Config.DiscoveryToken = discoveryToken!;
            if (TryGetEnv("ONI_DLC_CHECK_MODE", out string? dlcMode) &&
                Enum.TryParse(dlcMode, ignoreCase: true, out DlcCheckMode mode))
                Config.DlcCheckMode = mode;
            if (TryGetEnv("ONI_REQUIRED_DLC_IDS", out string? reqDlc))
                Config.RequiredDlcIds = SplitCsv(reqDlc);
            if (TryGetEnv("ONI_FORBIDDEN_DLC_IDS", out string? forbDlc))
                Config.ForbiddenDlcIds = SplitCsv(forbDlc);
            if (TryGetEnv("ONI_SERVER_ACTIVE_DLC_IDS", out string? serverDlc))
                Config.ServerActiveDlcIds = SplitCsv(serverDlc);
            if (TryGetEnvBool("ONI_REQUIRE_DLC_DECLARATION", out bool requireDlc))
                Config.RequireDlcDeclaration = requireDlc;
            if (TryGetEnvBool("ONI_MATCH_COLONY_DLC", out bool matchColonyDlc))
                Config.MatchColonyDlc = matchColonyDlc;
            if (TryGetEnvBool("ONI_LOG_TO_FILE", out bool logToFile))
                Config.LogToFile = logToFile;
            if (TryGetEnv("ONI_LOG_LEVEL", out string? logLevel))
                Config.LogLevel = logLevel!;
        }

        /// <summary>Resolve a configured save file to an absolute path, applying sandboxing. Returns null when unsafe/not set.</summary>
        public static string? ResolveSaveFilePath(string? saveFileName)
        {
            if (string.IsNullOrWhiteSpace(saveFileName))
                return null;

            string candidate = saveFileName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)
                ? saveFileName
                : $"{saveFileName}.sav";

            bool allowExternal = _instance?.Config.AllowExternalSavePaths ?? false;

            if (Path.IsPathRooted(candidate))
            {
                string full = Path.GetFullPath(candidate);
                if (allowExternal)
                    return full;
                // Confine to saves directory.
                return PathSandbox.ResolveInsideRoot(SavesDirectory, full);
            }

            // Reject traversal in relative names.
            if (candidate.Contains("..", StringComparison.Ordinal))
                return null;

            return PathSandbox.ResolveInsideRoot(SavesDirectory, Path.Combine(SavesDirectory, candidate));
        }

        public static string[] SplitCsv(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
            return value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static bool TryGetEnv(string name, out string? value)
        {
            value = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool TryGetEnvInt(string name, out int value)
        {
            value = 0;
            return TryGetEnv(name, out string? raw) && int.TryParse(raw, out value);
        }

        private static bool TryGetEnvBool(string name, out bool value)
        {
            value = false;
            if (!TryGetEnv(name, out string? raw)) return false;
            if (raw == "1") { value = true; return true; }
            if (raw == "0") { value = false; return true; }
            return bool.TryParse(raw, out value);
        }
    }

    public class DedicatedConfig
    {
        // --- Existing ---
        public int Transport { get; set; } = 0;
        public string ServerName { get; set; } = "ONI Together Dedicated Server";
        public string Ip { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 7777;
        public int MaxLobbySize { get; set; } = 4;
        public int TimeoutSeconds { get; set; } = 30;
        public int TickRate { get; set; } = 60;
        public int MaxMessagesPerPoll { get; set; } = 128;
        public string SaveFile { get; set; } = "";
        public string SavesDirectory { get; set; } = "";
        public bool VerbosePacketLogging { get; set; } = false;

        // --- New: operational ---
        public bool NonInteractive { get; set; } = false;
        public bool PauseWhenEmpty { get; set; } = true;
        public bool LogToFile { get; set; } = false;
        public string LogLevel { get; set; } = "Info";

        // --- New: security / resource limits ---
        public int MaxPacketSize { get; set; } = 256 * 1024;
        public int RateLimitMessagesPerSecond { get; set; } = 600;
        public int MaxMessageSize { get; set; } = 256 * 1024;
        public long MaxSaveBytes { get; set; } = 256L * 1024 * 1024;
        public int SaveBackupCount { get; set; } = 5;
        public bool AllowExternalSavePaths { get; set; } = false;

        // --- New: save persistence ---
        public int SaveAutosaveSeconds { get; set; } = 0; // 0 = disabled
        public string SaveFileName { get; set; } = "current";

        // --- New: discovery ---
        public bool DiscoveryEnabled { get; set; } = true;
        public string DiscoveryToken { get; set; } = ""; // optional; empty = unauthenticated

        // --- New: DLC ownership / compatibility ---
        public DlcCheckMode DlcCheckMode { get; set; } = DlcCheckMode.Off;
        public string[] RequiredDlcIds { get; set; } = Array.Empty<string>();
        public string[] ForbiddenDlcIds { get; set; } = Array.Empty<string>();
        public string[] ServerActiveDlcIds { get; set; } = Array.Empty<string>();
        public bool RequireDlcDeclaration { get; set; } = false;

        public bool MatchColonyDlc { get; set; } = true;

        /// <summary>Clamp/validate values into safe ranges. Call after load + env overrides.</summary>
        public void Sanitize()
        {
            // Server name: trim, cap length, strip control chars.
            ServerName = SanitizeString(ServerName, "ONI Together Dedicated Server", maxLength: 128);
            DiscoveryToken = DiscoveryToken?.Trim() ?? "";
            SaveFile = SaveFile?.Trim() ?? "";
            SavesDirectory = SavesDirectory?.Trim() ?? "";
            SaveFileName = string.IsNullOrWhiteSpace(SaveFileName) ? "current" : SaveFileName.Trim();
            LogLevel = string.IsNullOrWhiteSpace(LogLevel) ? "Info" : LogLevel.Trim();

            Ip = (Ip?.Trim() ?? "0.0.0.0");
            if (string.IsNullOrEmpty(Ip)) Ip = "0.0.0.0";

            Port = Clamp(Port, 1, 65535, 7777);
            MaxLobbySize = Clamp(MaxLobbySize, 1, 1000, 4);
            TimeoutSeconds = Clamp(TimeoutSeconds, 5, 3600, 30);
            TickRate = Clamp(TickRate, 10, 240, 60);
            MaxMessagesPerPoll = Clamp(MaxMessagesPerPoll, 1, 100_000, 128);
            MaxPacketSize = Clamp(MaxPacketSize, 1024, 16 * 1024 * 1024, 256 * 1024);
            MaxMessageSize = Clamp(MaxMessageSize, 1024, 16 * 1024 * 1024, 256 * 1024);
            RateLimitMessagesPerSecond = Clamp(RateLimitMessagesPerSecond, 1, 1_000_000, 600);
            MaxSaveBytes = MaxSaveBytes < 1024 ? 256L * 1024 * 1024 : Math.Min(MaxSaveBytes, 4L * 1024 * 1024 * 1024);
            SaveBackupCount = Clamp(SaveBackupCount, 0, 100, 5);
            SaveAutosaveSeconds = Clamp(SaveAutosaveSeconds, 0, 86400, 0);

            RequiredDlcIds = CleanIds(RequiredDlcIds);
            ForbiddenDlcIds = CleanIds(ForbiddenDlcIds);
            ServerActiveDlcIds = CleanIds(ServerActiveDlcIds);
            // Required + forbidden overlap would make everyone unjoinable; prefer forbidden removal.
            var forb = new System.Collections.Generic.HashSet<string>(ForbiddenDlcIds, StringComparer.OrdinalIgnoreCase);
            RequiredDlcIds = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(RequiredDlcIds, r => !forb.Contains(r)));
        }

        private static int Clamp(int v, int min, int max, int fallback) => v < min ? fallback : Math.Min(v, max);

        private static string SanitizeString(string? value, string fallback, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var sb = new System.Text.StringBuilder(value.Trim().Length);
            foreach (char c in value.Trim())
            {
                if (!char.IsControl(c)) sb.Append(c);
                if (sb.Length >= maxLength) break;
            }
            return sb.Length == 0 ? fallback : sb.ToString();
        }

        private static string[] CleanIds(string[]? ids)
        {
            if (ids == null || ids.Length == 0) return Array.Empty<string>();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new System.Collections.Generic.List<string>();
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                var t = id.Trim();
                if (t.Length > 128) t = t.Substring(0, 128);
                if (seen.Add(t)) list.Add(t);
            }
            return list.ToArray();
        }
    }
}
