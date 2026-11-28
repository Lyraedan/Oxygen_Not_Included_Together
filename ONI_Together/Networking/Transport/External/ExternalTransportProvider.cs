using ONI_Together.DebugTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ONI_Together.Networking.Transport.External
{
    /// <summary>
    /// Main-side wrapper around a provider object from another mod's copy of the
    /// ONI_Together_API assembly.
    /// </summary>
    public sealed class ExternalTransportProvider
    {
        public const int SupportedApiVersion = 1;

        public string Id { get; }
        public string DisplayName { get; }
        public int ApiVersion { get; }
        public ExternalTransportCapabilities Capabilities { get; }
        public object ProviderObject { get; }

        private readonly MethodInfo _createServer;
        private readonly MethodInfo _createClient;
        private readonly MethodInfo _getSettings;
        private readonly Func<bool> _requiresLogin;
        private readonly Func<string> _loginMessage;
        private readonly MethodInfo _beginLogin;
        private readonly PropertyInfo _loginState;
        private readonly Func<bool> _supportsLobbyBrowser;
        private readonly Func<bool> _supportsLobbyVisibility;
        private readonly MethodInfo _setLobbyAdvertisement;
        private readonly MethodInfo _setLobbyVisibility;
        private readonly MethodInfo _refreshLobbyList;
        private readonly MethodInfo _getLobbyList;

        public bool IsDirect => (Capabilities & ExternalTransportCapabilities.DirectConnect) != 0;
        public bool IsSteam => (Capabilities & ExternalTransportCapabilities.Steam) != 0;

        private ExternalTransportProvider(object provider, string id, string displayName, int apiVersion,
            ExternalTransportCapabilities capabilities, MethodInfo createServer, MethodInfo createClient,
            MethodInfo getSettings, Func<bool> requiresLogin, Func<string> loginMessage,
            MethodInfo beginLogin, PropertyInfo loginState, Func<bool> supportsLobbyBrowser,
            Func<bool> supportsLobbyVisibility, MethodInfo setLobbyAdvertisement,
            MethodInfo setLobbyVisibility, MethodInfo refreshLobbyList, MethodInfo getLobbyList)
        {
            ProviderObject = provider;
            Id = id;
            DisplayName = displayName;
            ApiVersion = apiVersion;
            Capabilities = capabilities;
            _createServer = createServer;
            _createClient = createClient;
            _getSettings = getSettings;
            _requiresLogin = requiresLogin;
            _loginMessage = loginMessage;
            _beginLogin = beginLogin;
            _loginState = loginState;
            _supportsLobbyBrowser = supportsLobbyBrowser;
            _supportsLobbyVisibility = supportsLobbyVisibility;
            _setLobbyAdvertisement = setLobbyAdvertisement;
            _setLobbyVisibility = setLobbyVisibility;
            _refreshLobbyList = refreshLobbyList;
            _getLobbyList = getLobbyList;
        }

        public static bool TryCreate(object provider, out ExternalTransportProvider result, out string error)
        {
            result = null;
            error = string.Empty;

            if (provider == null)
            {
                error = "Provider object is null.";
                return false;
            }

            try
            {
                string id = (ExternalApiBinder.GetPropertyValue(provider, "Id") as string)?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    error = "Provider Id is empty.";
                    return false;
                }

                if (TransportRegistry.IsReservedTransportId(id))
                {
                    error = $"Transport id '{id}' is reserved by a built-in transport.";
                    return false;
                }

                string displayName = (ExternalApiBinder.GetPropertyValue(provider, "DisplayName") as string)?.Trim();
                if (string.IsNullOrEmpty(displayName))
                    displayName = id;

                int apiVersion = Convert.ToInt32(ExternalApiBinder.GetPropertyValue(provider, "ApiVersion") ?? SupportedApiVersion);
                if (apiVersion != SupportedApiVersion)
                {
                    error = $"Transport '{id}' uses API version {apiVersion}; this mod supports {SupportedApiVersion}.";
                    return false;
                }

                int capabilityValue = Convert.ToInt32(ExternalApiBinder.GetPropertyValue(provider, "Capabilities") ?? 0);
                var capabilities = (ExternalTransportCapabilities)capabilityValue;

                if (!ExternalApiBinder.TryGetMethod(provider, "CreateServer", Type.EmptyTypes, out var createServer))
                {
                    error = "Provider is missing public CreateServer().";
                    return false;
                }

                if (!ExternalApiBinder.TryGetMethod(provider, "CreateClient", Type.EmptyTypes, out var createClient))
                {
                    error = "Provider is missing public CreateClient().";
                    return false;
                }

                ExternalApiBinder.TryGetMethod(provider, "GetSettings", Type.EmptyTypes, out var getSettings);

                ExternalApiBinder.TryGetPropertyGetter(provider, "RequiresLogin", out Func<bool> requiresLogin);
                ExternalApiBinder.TryGetPropertyGetter(provider, "LoginMessage", out Func<string> loginMessage);
                ExternalApiBinder.TryGetMethod(provider, "BeginLogin", Type.EmptyTypes, out MethodInfo beginLogin);
                ExternalApiBinder.TryGetProperty(provider, "LoginState", out PropertyInfo loginState);
                ExternalApiBinder.TryGetPropertyGetter(provider, "SupportsLobbyBrowser", out Func<bool> supportsLobbyBrowser);
                ExternalApiBinder.TryGetPropertyGetter(provider, "SupportsLobbyVisibility", out Func<bool> supportsLobbyVisibility);
                MethodInfo setLobbyAdvertisement = FindMethod(provider, "SetLobbyAdvertisement", 1);
                MethodInfo setLobbyVisibility = FindMethod(provider, "SetLobbyVisibility", 1);
                MethodInfo refreshLobbyList = FindMethod(provider, "RefreshLobbyList", 0);
                MethodInfo getLobbyList = FindMethod(provider, "GetLobbyList", 0);

                result = new ExternalTransportProvider(provider, id, displayName, apiVersion, capabilities,
                    createServer, createClient, getSettings, requiresLogin, loginMessage, beginLogin, loginState,
                    supportsLobbyBrowser, supportsLobbyVisibility, setLobbyAdvertisement, setLobbyVisibility,
                    refreshLobbyList, getLobbyList);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public ExternalTransportServer CreateServer()
        {
            try
            {
                object instance = _createServer.Invoke(ProviderObject, null);
                if (instance == null)
                {
                    DebugConsole.LogError($"[Transport-API] Provider '{Id}' CreateServer() returned null.");
                    return null;
                }

                return new ExternalTransportServer(this, instance);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] Provider '{Id}' failed to create server: {ex}");
                return null;
            }
        }

        public ExternalTransportClient CreateClient()
        {
            try
            {
                object instance = _createClient.Invoke(ProviderObject, null);
                if (instance == null)
                {
                    DebugConsole.LogError($"[Transport-API] Provider '{Id}' CreateClient() returned null.");
                    return null;
                }

                return new ExternalTransportClient(this, instance);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] Provider '{Id}' failed to create client: {ex}");
                return null;
            }
        }

        public bool RequiresLogin
        {
            get
            {
                try
                {
                    return _requiresLogin != null && _requiresLogin();
                }
                catch
                {
                    return false;
                }
            }
        }

        public ExternalTransportLoginState LoginState
        {
            get
            {
                if (_loginState == null)
                    return ExternalTransportLoginState.Ready;

                try
                {
                    object value = _loginState.GetValue(ProviderObject, null);
                    return (ExternalTransportLoginState)Convert.ToInt32(value);
                }
                catch
                {
                    return ExternalTransportLoginState.Failed;
                }
            }
        }

        public string LoginMessage
        {
            get
            {
                try
                {
                    return _loginMessage?.Invoke() ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        public void BeginLogin()
        {
            try
            {
                _beginLogin?.Invoke(ProviderObject, null);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] Provider '{Id}' BeginLogin failed: {ex.Message}");
            }
        }

        public bool SupportsLobbyBrowser
        {
            get
            {
                try
                {
                    return _supportsLobbyBrowser != null && _supportsLobbyBrowser();
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool SupportsLobbyVisibility
        {
            get
            {
                try
                {
                    return _supportsLobbyVisibility != null && _supportsLobbyVisibility();
                }
                catch
                {
                    return false;
                }
            }
        }

        public void SetLobbyAdvertisement(ExternalTransportLobbyAdvertisement advertisement)
        {
            if (_setLobbyAdvertisement == null || advertisement == null)
                return;

            try
            {
                ParameterInfo parameter = _setLobbyAdvertisement.GetParameters()[0];
                object native = Activator.CreateInstance(parameter.ParameterType);
                SetMember(native, "Name", advertisement.Name);
                SetMember(native, "HostName", advertisement.HostName);
                SetEnumMember(native, "Visibility", (int)advertisement.Visibility);
                SetMember(native, "HasPassword", advertisement.HasPassword);
                SetMember(native, "PasswordHash", advertisement.PasswordHash);
                SetMember(native, "MaxPlayers", advertisement.MaxPlayers);
                SetMember(native, "Data", new Dictionary<string, string>(advertisement.Data));
                _setLobbyAdvertisement.Invoke(ProviderObject, new[] { native });
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] Provider '{Id}' SetLobbyAdvertisement failed: {ex.Message}");
            }
        }

        public void SetLobbyVisibility(ExternalTransportLobbyVisibility visibility)
        {
            if (_setLobbyVisibility == null)
                return;

            try
            {
                ParameterInfo parameter = _setLobbyVisibility.GetParameters()[0];
                object value = Enum.ToObject(parameter.ParameterType, (int)visibility);
                _setLobbyVisibility.Invoke(ProviderObject, new[] { value });
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] Provider '{Id}' SetLobbyVisibility failed: {ex.Message}");
            }
        }

        public void RefreshLobbyList()
        {
            try
            {
                _refreshLobbyList?.Invoke(ProviderObject, null);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] Provider '{Id}' RefreshLobbyList failed: {ex.Message}");
            }
        }

        public List<ExternalTransportLobbyInfo> GetLobbyList()
        {
            var result = new List<ExternalTransportLobbyInfo>();
            if (_getLobbyList == null)
                return result;

            try
            {
                object raw = _getLobbyList.Invoke(ProviderObject, null);
                if (raw is not Array array)
                    return result;

                foreach (object item in array)
                {
                    if (item == null)
                        continue;

                    string transportId = GetString(item, "TransportId");
                    result.Add(new ExternalTransportLobbyInfo
                    {
                        TransportId = string.IsNullOrEmpty(transportId) ? Id : transportId,
                        LobbyId = GetString(item, "LobbyId"),
                        Name = GetString(item, "Name"),
                        HostName = GetString(item, "HostName"),
                        ColonyName = GetString(item, "ColonyName"),
                        PlayerCount = GetInt(item, "PlayerCount"),
                        MaxPlayers = GetInt(item, "MaxPlayers"),
                        PingMs = GetInt(item, "PingMs", -1),
                        Cycle = GetInt(item, "Cycle"),
                        DuplicantAlive = GetInt(item, "DuplicantAlive"),
                        DuplicantTotal = GetInt(item, "DuplicantTotal"),
                        HasPassword = GetBool(item, "HasPassword"),
                        Visibility = (ExternalTransportLobbyVisibility)GetInt(item, "Visibility"),
                        JoinSettings = GetMember(item, "JoinSettings") as Dictionary<string, string>
                            ?? new Dictionary<string, string>()
                    });
                }
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] Provider '{Id}' GetLobbyList failed: {ex.Message}");
            }

            return result;
        }

        private static MethodInfo FindMethod(object instance, string name, int parameterCount)
        {
            try
            {
                return instance.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount);
            }
            catch
            {
                return null;
            }
        }

        private static object GetMember(object instance, string name)
        {
            return ExternalApiBinder.GetMemberValue(instance, name);
        }

        private static string GetString(object instance, string name)
        {
            return GetMember(instance, name) as string ?? string.Empty;
        }

        private static int GetInt(object instance, string name, int fallback = 0)
        {
            object value = GetMember(instance, name);
            return value == null ? fallback : Convert.ToInt32(value);
        }

        private static bool GetBool(object instance, string name)
        {
            object value = GetMember(instance, name);
            return value != null && Convert.ToBoolean(value);
        }

        private static void SetMember(object instance, string name, object value)
        {
            if (instance == null)
                return;

            Type type = instance.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }

            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
                property.SetValue(instance, value, null);
        }

        private static void SetEnumMember(object instance, string name, int value)
        {
            if (instance == null)
                return;

            Type type = instance.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(instance, Enum.ToObject(field.FieldType, value));
                return;
            }

            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
                property.SetValue(instance, Enum.ToObject(property.PropertyType, value), null);
        }

        public List<ExternalTransportSetting> GetSettings()
        {
            var result = new List<ExternalTransportSetting>();
            if (_getSettings == null)
                return result;

            try
            {
                object raw = _getSettings.Invoke(ProviderObject, null);
                if (raw is not Array array)
                    return result;

                foreach (object item in array)
                {
                    if (item == null)
                        continue;

                    string key = (ExternalApiBinder.GetMemberValue(item, "Key") as string)?.Trim();
                    if (string.IsNullOrEmpty(key))
                        continue;

                    var setting = new ExternalTransportSetting
                    {
                        Key = key,
                        Label = (ExternalApiBinder.GetMemberValue(item, "Label") as string) ?? key,
                        Tooltip = (ExternalApiBinder.GetMemberValue(item, "Tooltip") as string) ?? string.Empty,
                        Placeholder = (ExternalApiBinder.GetMemberValue(item, "Placeholder") as string) ?? string.Empty,
                        Kind = (ExternalTransportSettingKind)Convert.ToInt32(
                            ExternalApiBinder.GetMemberValue(item, "Kind") ?? 0),
                        Scope = (ExternalTransportSettingScope)Convert.ToInt32(
                            ExternalApiBinder.GetMemberValue(item, "Scope") ?? (int)ExternalTransportSettingScope.Both),
                        DefaultValue = (ExternalApiBinder.GetMemberValue(item, "DefaultValue") as string) ?? string.Empty,
                        Required = Convert.ToBoolean(ExternalApiBinder.GetMemberValue(item, "Required") ?? false)
                    };

                    result.Add(setting);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Transport-API] Provider '{Id}' failed to return settings: {ex}");
            }

            return result;
        }
    }
}
