using System;

namespace ONI_Together.Networking.Transport.External
{
    public enum ExternalTransportSettingKind
    {
        Text = 0,
        Integer = 1,
        Boolean = 2,
        Password = 3
    }

    [Flags]
    public enum ExternalTransportSettingScope
    {
        Host = 1,
        Client = 2,
        Both = Host | Client
    }

    [Flags]
    public enum ExternalTransportCapabilities
    {
        None = 0,
        NativeFragmentation = 1,
        NatTraversal = 2,
        DirectConnect = 4,
        Steam = 8,
        Custom = 16
    }

    public enum ExternalTransportLoginState
    {
        NotStarted = 0,
        LoggingIn = 1,
        Ready = 2,
        Failed = 3
    }

    public enum ExternalTransportLobbyVisibility
    {
        Public = 0,
        FriendsOnly = 1,
        Private = 2
    }

    public sealed class ExternalTransportSetting
    {
        public string Key = string.Empty;
        public string Label = string.Empty;
        public string Tooltip = string.Empty;
        public string Placeholder = string.Empty;
        public ExternalTransportSettingKind Kind;
        public ExternalTransportSettingScope Scope = ExternalTransportSettingScope.Both;
        public string DefaultValue = string.Empty;
        public bool Required;
    }
}
