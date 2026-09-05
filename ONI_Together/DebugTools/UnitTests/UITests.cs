using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using ONI_Together.UI;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class UITests
    {
        [UnitTest(name: "Chat window exists and is active", category: "UI")]
        public static UnitTestResult ChatWindowExistsAndActive()
        {
            if (UnityChatBoxUI.Instance == null)
                return UnitTestResult.Fail("UnityChatBoxUI instance is null");

            bool isActive = UnityChatBoxUI.Instance.gameObject.activeSelf;
            if (!isActive)
                return UnitTestResult.Fail("UnityChatBoxUI is not active");
            return UnitTestResult.Pass("UnityChatBoxUI exists and is active");
        }

        [UnitTest(name: "Ping & Trail Initialized", category: "UI")]
        public static UnitTestResult PingAndTrailSystemInitialized()
        {
            if (PingManager.Instance == null)
                return UnitTestResult.Fail("PingManager instance is null");
            return UnitTestResult.Pass("PingManager instance exists");
        }

        [UnitTest(name: "No ghost cursors present", category: "UI")]
        public static UnitTestResult NoGhostCursorsPresent()
        {
            if (!MultiplayerSession.IsHost && !MultiplayerSession.IsClient)
                return UnitTestResult.Fail("Not connected to a multiplayer session");

            var clients = NetworkConfig.GetConnectedClients().Count;
            var cursors = MultiplayerSession.PlayerCursors.Count;

            // - 1 to remove local client
            if(cursors > clients - 1)
                return UnitTestResult.Fail($"Number of player cursors ({cursors}) exceeds number of clients ({clients})");

            if (clients - 1 != cursors)
                return UnitTestResult.Fail($"Number of player cursors ({cursors}) does not match number of clients ({clients})");

            bool cursorSyncRunning = CursorManager.Instance != null && Utils.IsInGame() && MultiplayerSession.InActiveSession && MultiplayerSession.LocalUserID.IsValid();
            if(!cursorSyncRunning)
                return UnitTestResult.Fail("Cursor synchronization does not appear to be running (CursorManager instance missing or not in game session)");

            return UnitTestResult.Pass("Number of player cursors matches number of clients");
        }

        [UnitTest(name: "Brush cursor preview packet round-trip", category: "UI")]
        public static UnitTestResult BrushCursorPreviewPacketRoundTrip()
        {
            var original = new PlayerCursorPacket
            {
                PlayerID = 42,
                Position = new Vector3(12.5f, 7.25f, 0f),
                Color = Color.cyan,
                CursorState = CursorState.SANDBOX_BRUSH,
                BuildingPrefabId = string.Empty,
                HasBrushPreview = true,
                BrushRadius = 6
            };

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                original.Serialize(writer);

            stream.Position = 0;
            var copy = new PlayerCursorPacket();
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                copy.Deserialize(reader);

            if (!copy.HasBrushPreview || copy.BrushRadius != original.BrushRadius)
                return UnitTestResult.Fail("Brush preview flag or radius was not preserved");

            if (copy.CursorState != original.CursorState || copy.Position != original.Position)
                return UnitTestResult.Fail("Cursor state or position changed during serialization");

            return UnitTestResult.Pass("Brush cursor preview survives packet serialization");
        }
    }
}
