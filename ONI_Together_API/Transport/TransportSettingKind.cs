namespace ONI_Together_API.Transport
{
    /// <summary>
    /// The kind of control the main mod should use to edit a transport setting.
    /// The value passed to the transport is always a string.
    /// </summary>
    public enum TransportSettingKind
    {
        Text,
        Integer,
        Boolean,
        Password
    }
}
