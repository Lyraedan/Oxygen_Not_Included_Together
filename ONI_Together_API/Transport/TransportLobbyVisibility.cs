using JetBrains.Annotations;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Visibility of a transport-provided lobby.
    /// </summary>
    [PublicAPI]
    public enum TransportLobbyVisibility
    {
        Public = 0,
        FriendsOnly = 1,
        Private = 2
    }
}
