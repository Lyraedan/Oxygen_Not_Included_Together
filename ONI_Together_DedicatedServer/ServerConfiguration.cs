using System;
using System.IO;
using Newtonsoft.Json;

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
            return result;
        }

        public void Save()
        {
            if (!Directory.Exists(ConfigDirectory))
                Directory.CreateDirectory(ConfigDirectory);

            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
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
        }

        public static string? ResolveSaveFilePath(string? saveFileName)
        {
            if (string.IsNullOrWhiteSpace(saveFileName))
                return null;

            string candidate = saveFileName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)
                ? saveFileName
                : $"{saveFileName}.sav";

            if (Path.IsPathRooted(candidate))
                return candidate;

            return Path.Combine(SavesDirectory, candidate);
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
            if (!TryGetEnv(name, out string? raw))
                return false;
            if (raw == "1") { value = true; return true; }
            if (raw == "0") { value = false; return true; }
            return bool.TryParse(raw, out value);
        }
    }

    public class DedicatedConfig
    {
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
    }
}
