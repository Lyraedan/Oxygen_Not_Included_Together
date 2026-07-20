using ONI_Together.Misc;
using ONI_Together.Networking.Transport.Steamworks;

namespace ONI_Together.DebugTools.UnitTests;

public static class LobbyAccessTests
{
	[UnitTest(name: "Steam lobby access: metadata must be explicit", category: "Networking")]
	public static UnitTestResult PasswordMetadataMustBeExplicit()
	{
		if (!SteamLobby.TryParsePasswordRequirement("1", out bool required) || !required)
			return UnitTestResult.Fail("Password-protected metadata was not recognized");
		if (!SteamLobby.TryParsePasswordRequirement("0", out required) || required)
			return UnitTestResult.Fail("Password-free metadata was not recognized");
		if (SteamLobby.TryParsePasswordRequirement("", out _))
			return UnitTestResult.Fail("Missing metadata was treated as password-free");

		return UnitTestResult.Pass("Only explicit Steam password metadata authorizes joining");
	}

	[UnitTest(name: "Steam lobby access: publication matches validation", category: "Networking")]
	public static UnitTestResult PasswordConfigurationIsConsistent()
	{
		var settings = new LobbySettings { RequirePassword = true };
		if (SteamLobby.HasConfiguredPassword(settings))
			return UnitTestResult.Fail("An empty password hash was published as protected");

		settings.PasswordHash = PasswordHelper.HashPassword("test-password");
		return SteamLobby.HasConfiguredPassword(settings)
			? UnitTestResult.Pass("Lobby publication and validation share one password predicate")
			: UnitTestResult.Fail("A configured password was ignored");
	}
}
