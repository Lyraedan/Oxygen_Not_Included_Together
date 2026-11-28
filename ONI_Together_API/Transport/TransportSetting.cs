using JetBrains.Annotations;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Describes one user-editable setting for a custom transport.
    /// Values are stored and passed to <see cref="ITransportServer.Start"/> and
    /// <see cref="ITransportClient.Connect"/> as strings.
    /// </summary>
    [PublicAPI]
    public sealed class TransportSetting
    {
        public string Key = string.Empty;
        public string Label = string.Empty;
        public string Tooltip = string.Empty;
        public string Placeholder = string.Empty;
        public TransportSettingKind Kind = TransportSettingKind.Text;
        public TransportSettingScope Scope = TransportSettingScope.Both;
        public string DefaultValue = string.Empty;
        public bool Required;
    }
}
