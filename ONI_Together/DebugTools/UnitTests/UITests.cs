using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class UITests
    {
        [UnitTest(name: "Chat window exists and is active", category: "UI", liveSafe: true)]
        public static UnitTestResult ChatWindowExistsAndActive()
        {
            GameObject chatScreen = GameObject.Find("ChatScreen");
            if(chatScreen == null)
                return Game.Instance == null
                    ? UnitTestResult.Skip("Requires a loaded colony")
                    : UnitTestResult.Fail("ChatScreen object not found in scene");

            bool isActive = chatScreen.activeSelf;
            if (!isActive)
                return UnitTestResult.Fail("ChatScreen object is not active");
            return UnitTestResult.Pass("ChatScreen object exists and is active");
        }

        [UnitTest(name: "Ping & Trail Initialized", category: "UI", liveSafe: true)]
        public static UnitTestResult PingAndTrailSystemInitialized()
        {
            if (PingManager.Instance == null)
                return UnitTestResult.Fail("PingManager instance is null");
            return UnitTestResult.Pass("PingManager instance exists");
        }

        [UnitTest(name: "No ghost cursors present", category: "UI", liveSafe: true)]
        public static UnitTestResult NoGhostCursorsPresent()
        {
            if (!MultiplayerSession.IsHost && !MultiplayerSession.IsClient)
                return UnitTestResult.Skip("Requires an active multiplayer session");

            HashSet<ulong> expectedRemotePlayers = MultiplayerSession.GetConnectedRemotePlayerIds();
            HashSet<ulong> actualCursors = MultiplayerSession.PlayerCursors.Keys.ToHashSet();

            int ghostCursorCount = actualCursors.Except(expectedRemotePlayers).Count();
            if (ghostCursorCount > 0)
                return UnitTestResult.Fail($"Found {ghostCursorCount} cursor(s) without a connected remote player");

            int missingCursorCount = expectedRemotePlayers.Except(actualCursors).Count();
            if (missingCursorCount > 0)
                return UnitTestResult.Fail($"Found {missingCursorCount} connected remote player(s) without a cursor");

            bool cursorSyncRunning = CursorManager.Instance != null && Utils.IsInGame() && MultiplayerSession.InSession && MultiplayerSession.LocalUserID.IsValid();
            if(!cursorSyncRunning)
                return UnitTestResult.Fail("Cursor synchronization does not appear to be running (CursorManager instance missing or not in game session)");

            return UnitTestResult.Pass("Player cursor membership matches connected remote players");
        }

        [UnitTest(name: "Cursor membership includes client host", category: "UI")]
        public static UnitTestResult CursorMembershipIncludesClientHost()
        {
            HashSet<ulong> remotes = MultiplayerSession.ResolveConnectedRemotePlayerIds(
                new ulong[] { 2, 3 },
                new ulong[] { 1 },
                localUserId: 2,
                transportIsConnected: true);

            if (!remotes.SetEquals(new ulong[] { 1, 3 }))
                return UnitTestResult.Fail("Client host was omitted from remote cursor membership");
            if (MultiplayerSession.ResolveConnectedRemotePlayerIds(
                    new ulong[] { 2, 3 }, new ulong[] { 1 }, 2, transportIsConnected: false).Count != 0)
                return UnitTestResult.Fail("Disconnected session retained remote cursor membership");

            return UnitTestResult.Pass("Client host and peer identities are remote only while connected");
        }
    }
}
