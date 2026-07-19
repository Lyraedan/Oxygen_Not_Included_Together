using ONI_Together.Misc;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.Networking.Transport.Steam;
using Steamworks;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class TransportReconnectTests
	{
		[UnitTest(name: "Riptide reconnect rejects invalid endpoints", category: "Transport")]
		public static UnitTestResult RiptideRejectsInvalidEndpoints()
		{
			if (RiptideClient.IsReconnectEndpointValid(string.Empty, 7777)
			    || RiptideClient.IsReconnectEndpointValid("not a host", 7777)
			    || RiptideClient.IsReconnectEndpointValid("127.0.0.1", 0)
			    || RiptideClient.IsReconnectEndpointValid("127.0.0.1", 65536))
				return UnitTestResult.Fail("Riptide accepted an invalid reconnect endpoint");

			return RiptideClient.IsReconnectEndpointValid("127.0.0.1", 7777)
			       && RiptideClient.IsReconnectEndpointValid("oni-host.local", 7777)
				? UnitTestResult.Pass("Riptide reconnect endpoint validation is strict")
				: UnitTestResult.Fail("Riptide rejected a valid IP address or DNS hostname");
		}

		[UnitTest(name: "Steam reconnect requires host and connection handle", category: "Transport")]
		public static UnitTestResult SteamRequiresHostAndConnectionHandle()
		{
			var started = new HSteamNetConnection { m_HSteamNetConnection = 1 };
			if (SteamworksClient.CanReconnectToHost(Utils.NilUlong())
			    || SteamworksClient.IsConnectionAttemptStarted(default))
				return UnitTestResult.Fail("Steam accepted a missing host or invalid connection handle");

			return SteamworksClient.CanReconnectToHost(1)
			       && SteamworksClient.IsConnectionAttemptStarted(started)
				? UnitTestResult.Pass("Steam reconnect requires a concrete host and started connection")
				: UnitTestResult.Fail("Steam rejected a valid host or connection handle");
		}
	}
}
