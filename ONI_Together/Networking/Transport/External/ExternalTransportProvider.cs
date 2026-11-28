using ONI_Together.DebugTools;
using System;
using System.Collections.Generic;
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

        public bool IsDirect => (Capabilities & ExternalTransportCapabilities.DirectConnect) != 0;
        public bool IsSteam => (Capabilities & ExternalTransportCapabilities.Steam) != 0;

        private ExternalTransportProvider(object provider, string id, string displayName, int apiVersion,
            ExternalTransportCapabilities capabilities, MethodInfo createServer, MethodInfo createClient,
            MethodInfo getSettings)
        {
            ProviderObject = provider;
            Id = id;
            DisplayName = displayName;
            ApiVersion = apiVersion;
            Capabilities = capabilities;
            _createServer = createServer;
            _createClient = createClient;
            _getSettings = getSettings;
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

                result = new ExternalTransportProvider(provider, id, displayName, apiVersion, capabilities,
                    createServer, createClient, getSettings);
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
