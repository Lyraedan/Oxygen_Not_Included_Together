using ONI_MP.Networking.Packets.Architecture;
using Shared.Profiling;

namespace ONI_MP.Networking
{
	internal static class ProtocolCompatibility
	{

		public static bool BypassChecks = false;
		public const int CurrentProtocolVersion = 1;

		private static int? _packetFingerprint;
		private static string _modVersion;

		public static int PacketFingerprint
		{
			get
			{
				using var _ = Profiler.Scope();

				return _packetFingerprint ??= PacketRegistry.GetRegisteredPacketFingerprint();
			}
		}

		public static string ModVersion
		{
			get
			{
				using var _ = Profiler.Scope();

				return _modVersion ??= ONI_MP.Updater.Updater.GetVersion();
			}
		}

		public static bool Matches(int protocolVersion, int packetFingerprint)
		{
			using var _ = Profiler.Scope();

			if (BypassChecks)
				return true;

			return protocolVersion == CurrentProtocolVersion
				&& packetFingerprint == PacketFingerprint;
		}

		public static string BuildMismatchReason(int remoteProtocolVersion, int remotePacketFingerprint, string remoteModVersion, bool hasMetadata)
		{
			using var _ = Profiler.Scope();

            if (!hasMetadata)
            {
                return STRINGS.UI.PROTOCOL.NO_METADATA;
            }

            if (remoteProtocolVersion != CurrentProtocolVersion)
            {
                return string.Format(STRINGS.UI.PROTOCOL.PROTOCOL_MISMATCH, CurrentProtocolVersion, remoteProtocolVersion);
            }

            if (remotePacketFingerprint != PacketFingerprint)
            {
                return string.Format(STRINGS.UI.PROTOCOL.PACKET_REGISTRY_MISMATCH, PacketFingerprint, remotePacketFingerprint);
            }

            if (!string.IsNullOrEmpty(remoteModVersion) && remoteModVersion != ModVersion)
            {
                return string.Format(STRINGS.UI.PROTOCOL.MOD_VERSION_MISMATCH, ModVersion, remoteModVersion);
            }

            return STRINGS.UI.PROTOCOL.INCOMPATIBLE;
        }
	}
}
