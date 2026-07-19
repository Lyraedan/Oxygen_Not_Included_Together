using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.States;
using System.Linq;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests;

public static class WorldLoadAuthorityTests
{
	[UnitTest(name: "World load keeps the client role across transport restart", category: "Networking")]
	public static UnitTestResult ClientRoleSurvivesWorldLoadDisconnect()
	{
		if (!MultiplayerSession.ResolveLogicalSession(
			    transportConnected: false, retainClientWorldLoad: true)
		    || MultiplayerSession.ResolveLogicalSession(
			    transportConnected: false, retainClientWorldLoad: false)
		    || !MultiplayerSession.ResolveClientRole(
			    isHost: false, logicalSession: true)
		    || MultiplayerSession.ResolveClientRole(
			    isHost: true, logicalSession: true))
		{
			return UnitTestResult.Fail(
				"A snapshot-loading client regained standalone authority while transport restarted");
		}

		return UnitTestResult.Pass(
			"Logical client authority survives the intentional world-load disconnect");
	}

	[UnitTest(name: "World load role releases only on Ready or final error", category: "Networking")]
	public static UnitTestResult WorldLoadRoleLifecycle()
	{
		bool previousTransport = MultiplayerSession.IsTransportConnected;
		bool previousHost = MultiplayerSession.IsHost;
		bool previousRetention = MultiplayerSession.IsClientWorldLoadRetained;
		try
		{
			MultiplayerSession.ReleaseClientWorldLoad();
			MultiplayerSession.IsHost = false;
			MultiplayerSession.InSession = true;
			if (!MultiplayerSession.TryRetainClientWorldLoad())
				return UnitTestResult.Fail("Connected client could not retain its world-load role");

			MultiplayerSession.InSession = false;
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsClient
			    || !MultiplayerSession.IsClientWorldLoadRetained)
				return UnitTestResult.Fail("Transport disconnect released logical client authority");
			if (GameClient.ShouldTransitionToDisconnected(ClientState.LoadingWorld)
			    || GameClient.ShouldTransitionToDisconnected(ClientState.Error)
			    || !GameClient.ShouldTransitionToDisconnected(ClientState.Connected))
				return UnitTestResult.Fail("Disconnect callback overwrote a terminal client state");

			MultiplayerSession.ReleaseClientWorldLoad();
			if (MultiplayerSession.InSession || MultiplayerSession.IsClient)
				return UnitTestResult.Fail("Final world-load exit retained client authority");
			return UnitTestResult.Pass("World-load authority spans transport restart and has an explicit exit");
		}
		finally
		{
			MultiplayerSession.ReleaseClientWorldLoad();
			MultiplayerSession.IsHost = previousHost;
			MultiplayerSession.InSession = previousTransport || previousRetention;
			if (previousRetention)
				MultiplayerSession.TryRetainClientWorldLoad();
			MultiplayerSession.InSession = previousTransport;
		}
	}

	[UnitTest(name: "Authority NetId overrides always enter the lifecycle journal", category: "Networking")]
	public static UnitTestResult AuthorityOverrideOwnsLifecycle()
	{
		if (!NetworkIdentity.ShouldBeginLifecycleForOverride(
			    inSession: false, isHost: false)
		    || !NetworkIdentity.ShouldBeginLifecycleForOverride(
			    inSession: true, isHost: true)
		    || NetworkIdentity.ShouldBeginLifecycleForOverride(
			    inSession: true, isHost: false))
		{
			return UnitTestResult.Fail(
				"Lifecycle ownership did not match standalone, host, and client authority");
		}

		return UnitTestResult.Pass(
			"Only standalone and host NetId overrides create lifecycle revisions");
	}

	[UnitTest(name: "Authority NetId remap journals new and tombstones old", category: "Networking")]
	public static UnitTestResult AuthorityOverrideRemapLifecycle()
	{
		const int oldId = -1_912_345_601;
		const int newId = -1_912_345_602;
		const int clientId = -1_912_345_603;
		var originalLifecycle = NetworkIdentityRegistry.GetLifecycleRevisionSnapshot().ToArray();
		ulong originalAuthorityRevision = NetworkIdentityRegistry.AuthorityRevisionForTests;
		bool previousTransport = MultiplayerSession.IsTransportConnected;
		bool previousHost = MultiplayerSession.IsHost;
		var authorityObject = new GameObject("AuthorityOverrideLifecycleTest");
		var clientObject = new GameObject("ClientOverrideLifecycleTest");
		NetworkIdentity authority = authorityObject.AddComponent<NetworkIdentity>();
		NetworkIdentity client = clientObject.AddComponent<NetworkIdentity>();
		try
		{
			MultiplayerSession.IsHost = false;
			MultiplayerSession.InSession = false;
			if (!authority.OverrideNetId(oldId))
				return UnitTestResult.Fail("Standalone identity override failed");
			ulong oldRevision = NetworkIdentityRegistry.GetLastLifecycleRevision(oldId);
			if (oldRevision == 0 || !authority.OverrideNetId(newId))
				return UnitTestResult.Fail("Authority identity remap had no initial lifecycle");
			ulong newRevision = NetworkIdentityRegistry.GetLastLifecycleRevision(newId);
			if (!NetworkIdentityRegistry.IsLifecycleTombstoned(oldId)
			    || newRevision <= oldRevision || NetworkIdentityRegistry.Exists(oldId)
			    || !NetworkIdentityRegistry.Exists(newId))
				return UnitTestResult.Fail("Authority remap did not tombstone old and begin new lifecycle");

			MultiplayerSession.InSession = true;
			if (!client.OverrideNetId(clientId)
			    || NetworkIdentityRegistry.GetLastLifecycleRevision(clientId) != 0)
				return UnitTestResult.Fail("Active client override forged an authority revision");
			return UnitTestResult.Pass("Authority remap advances lifecycle while clients await host revisions");
		}
		finally
		{
			NetworkIdentityRegistry.Unregister(authority, oldId);
			NetworkIdentityRegistry.Unregister(authority, newId);
			NetworkIdentityRegistry.Unregister(client, clientId);
			Object.DestroyImmediate(authorityObject);
			Object.DestroyImmediate(clientObject);
			NetworkIdentityRegistry.RestoreLifecycleRevisionStateForTests(
				originalLifecycle, originalAuthorityRevision);
			MultiplayerSession.IsHost = previousHost;
			MultiplayerSession.InSession = previousTransport;
		}
	}
}
