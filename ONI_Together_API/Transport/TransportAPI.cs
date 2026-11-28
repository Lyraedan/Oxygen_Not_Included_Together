using JetBrains.Annotations;
using Shared.Helpers;
using Shared.Profiling;
using System;
using UnityEngine;

namespace ONI_Together_API.Transport
{
    /// <summary>
    /// Registers custom transports with the ONI Together main mod.
    /// Call from OnAllModsLoaded, after the main mod type is available.
    /// </summary>
    [PublicAPI]
    public static class TransportAPI
    {
        public const int CurrentApiVersion = 1;

        private const string RegistryTypeName =
            "ONI_Together.Networking.Transport.TransportRegistry, ONI_Together";

        private delegate bool RegisterTransportDelegate(object provider);

        private static bool typesInitialized;
        private static RegisterTransportDelegate? _register;

        private static bool Init()
        {
            using var _ = Profiler.Scope();

            if (typesInitialized)
                return true;

            if (!ReflectionHelper.TryCreateDelegate<RegisterTransportDelegate>(
                    RegistryTypeName,
                    "RegisterExternalTransport_API",
                    [typeof(object)],
                    out _register))
                return false;

            typesInitialized = true;
            return true;
        }

        /// <summary>
        /// Registers a custom transport provider. The provider object must use public
        /// implicit implementations for all interface members.
        /// </summary>
        [PublicAPI]
        public static bool RegisterTransport(ITransportProvider? provider)
        {
            using var _ = Profiler.Scope();

            if (provider == null)
                return false;

            if (!Init() || _register == null)
                return false;

            try
            {
                return _register(provider);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TransportAPI] Failed to register transport '{provider.Id}': {ex.Message}");
                return false;
            }
        }
    }
}
