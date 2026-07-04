using Sentry;
using System;
using System.Reflection;
using ONI_Together.DebugTools;

namespace ONI_Together
{
    internal static class Telemetry
    {
        private const string SentryDsn = "https://o4510339747872768.ingest.de.sentry.io/4511677430956112";

        private static readonly string ModVersion = typeof(Telemetry).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        public static void Init()
        {
            try
            {
                SentrySdk.Init(o =>
                {
                    o.Dsn = SentryDsn;
                    o.Environment =
#if DEBUG
                        "development";
#else
                        "production";
#endif
                    o.Release = $"ONI_Together@{ModVersion}";
                    o.SendDefaultPii = false;
                    o.MaxBreadcrumbs = 200;
                    o.Debug = false;
                    o.SetBeforeSend(sentryEvent =>
                        Configuration.Instance.Telemetry.EnableTelemetry ? sentryEvent : null);
                });

                if (SentrySdk.IsEnabled)
                {
                    DebugConsole.Log("[Telemetry] Sentry SDK initialized successfully.");
#if DEBUG
                    SentrySdk.CaptureMessage("[Telemetry] Test event — Sentry integration is working!");
#endif
                }
                else
                    DebugConsole.LogWarning("[Telemetry] Sentry SDK failed to initialize.");
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[Telemetry] Init failed: {ex.Message}");
            }
        }

        public static void CaptureException(Exception ex)
        {
            if (SentrySdk.IsEnabled)
                SentrySdk.CaptureException(ex);
        }

        public static void CaptureMessage(string message, SentryLevel level = SentryLevel.Error)
        {
            if (SentrySdk.IsEnabled)
                SentrySdk.CaptureMessage(message, level);
        }

        public static void Shutdown()
        {
            if (SentrySdk.IsEnabled)
                SentrySdk.Close();
        }
    }
}
