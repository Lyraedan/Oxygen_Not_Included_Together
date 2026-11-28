using JetBrains.Annotations;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Optional login state for transports that require a user account
    /// (for example, Epic Online Services).
    /// </summary>
    [PublicAPI]
    public enum TransportLoginState
    {
        /// <summary>The transport has not started its login flow.</summary>
        NotStarted = 0,

        /// <summary>The transport is waiting for the user to complete login.</summary>
        LoggingIn = 1,

        /// <summary>The user is logged in and the transport is ready for host/join.</summary>
        Ready = 2,

        /// <summary>Login failed; <see cref="TransportProviderBase.LoginMessage"/> has details.</summary>
        Failed = 3
    }
}
