using System;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP
{
    class Configuration
    {
        private static string ConfigDirectory = Path.Combine(KMod.Manager.GetDirectory(), "config");
        private static readonly string ConfigPath = Path.Combine(
            ConfigDirectory,
            "multiplayer_settings.json"
        );
        private static Configuration _instance;

        public HostSettings Host { get; set; } = new HostSettings();
        public ClientSettings Client { get; set; } = new ClientSettings();

        public static Configuration Instance
        {
            get
            {
                if (_instance == null)
                    _instance = LoadOrCreate();
                return _instance;
            }
        }

        public static T GetHostProperty<T>(string propertyName)
        {
            using var _ = Profiler.Scope();

            return Instance.GetProperty<T>(Instance.Host, propertyName);
        }

        public static T GetClientProperty<T>(string propertyName)
        {
            using var _ = Profiler.Scope();

            return Instance.GetProperty<T>(Instance.Client, propertyName);
        }

        private T GetProperty<T>(object obj, string propertyName)
        {
            using var _ = Profiler.Scope();

            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

            if (prop == null)
                throw new ArgumentException($"Property '{propertyName}' not found on {obj.GetType().Name}");

            if (!typeof(T).IsAssignableFrom(prop.PropertyType))
                throw new InvalidCastException($"Property '{propertyName}' is of type {prop.PropertyType}, not {typeof(T)}");

            return (T)prop.GetValue(obj);
        }

        public static Configuration LoadOrCreate()
        {
            using var _ = Profiler.Scope();

            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            if (!File.Exists(ConfigPath))
            {
                var defaultConfig = new Configuration();
                string json = JsonConvert.SerializeObject(defaultConfig, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
                return defaultConfig;
            }

            string existingJson = File.ReadAllText(ConfigPath);
            return JsonConvert.DeserializeObject<Configuration>(existingJson);
        }

        public static void SetClientProperty<T>(string propertyName, T value)
        {
            using var _ = Profiler.Scope();

            Instance.SetProperty(Instance.Client, propertyName, value);

            Instance.Save();
        }

        public static void SetHostProperty<T>(string propertyName, T value)
        {
            using var _ = Profiler.Scope();

            Instance.SetProperty(Instance.Host, propertyName, value);
            Instance.Save();
        }

        private void SetProperty<T>(object obj, string propertyName, T value)
        {
            using var _ = Profiler.Scope();

            var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

            if (prop == null)
                throw new ArgumentException($"Property '{propertyName}' not found on {obj.GetType().Name}");

            if (!typeof(T).IsAssignableFrom(prop.PropertyType))
                throw new InvalidCastException($"Value of type {typeof(T)} cannot be assigned to property '{propertyName}' of type {prop.PropertyType}");

            prop.SetValue(obj, value);
        }

        public void Save()
        {
            using var _ = Profiler.Scope();

            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
    }

    class HostSettings
    {
        public int NetworkTransport = 0; // 0 = steam, 1 = riptide, 2 = litenetlib
        public int MaxLobbySize { get; set; } = 4;
        public int MaxMessagesPerPoll { get; set; } = 128;
        public int SaveFileTransferChunkKB { get; set; } = 256;
        public LanSettings LanSettings { get; set; } = new LanSettings();
        public LobbySettings Lobby { get; set; } = new LobbySettings();
    }

    class LobbySettings
    {
        public bool IsPrivate { get; set; } = false;
        public bool RequirePassword { get; set; } = false;
        public string PasswordHash { get; set; } = "";
        public string LobbyName { get; set; } = "";
        public string Region { get; set; } = ""; // Empty = auto-detect
    }

    class ClientSettings
    {
        public int MaxMessagesPerPoll { get; set; } = 16;
        public bool UseRandomPlayerColor { get; set; } = true;
        public ColorRGB PlayerColor { get; set; } = new ColorRGB(255, 255, 255);

        public LanSettings LanSettings { get; set; } = new LanSettings();

        public bool PuftAsLoadingIcon { get; set; } = true; // So people can disable it if they want to use the default smily face or the Pip loading mod, but for this mod. True by default
    }

    public class LanSettings
    {
        public string Ip { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8080;

        public string GetHashedAddress()
        {
            string value = $"{Ip}:{Port}";
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            return Convert.ToBase64String(bytes);
        }
    }

    public class ColorRGB
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        public ColorRGB() { }

        public ColorRGB(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public Color ToColor() => new Color(R / 255f, G / 255f, B / 255f);
        public static ColorRGB FromColor(Color color) =>
            new ColorRGB((byte)(color.r * 255), (byte)(color.g * 255), (byte)(color.b * 255));
    }
}