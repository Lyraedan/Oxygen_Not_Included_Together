using KSerialization;
using ONI_Together;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.UI;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using ONI_Together.DebugTools;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components;

[SkipSaveFileSerialization]
[FixedInterestGroup]
public class OxySyncChat : NetworkBehaviour
{
    public const int MAX_MESSAGE_LENGTH = 256;

    public struct PendingMessage
    {
        public long timestamp;
        public string message;
        public string colorHex;
    }
    
    public static OxySyncChat? Instance { get; private set; }

    private static List<PendingMessage> _chatHistory = new();
    private HashSet<ulong> _knownPlayers = new();

    public static IReadOnlyList<PendingMessage> ChatHistory => _chatHistory;

    public override void OnSpawn()
    {
        base.OnSpawn();
        Instance = this;
        NetId = nameof(OxySyncChat).GetHashCode();
        InterestGroup = -1;

        _knownPlayers = new HashSet<ulong>(MultiplayerSession.ConnectedPlayers.Keys);

        if (Game.Instance != null)
        {
            Game.Instance.Subscribe(MP_HASHES.OnPlayerJoined, OnPlayerJoined);
        }
        DebugConsole.Log("Spawned OxySync Chat!");
    }

    //public void Update()
    //{
    //    UnityChatBoxUI.Instance?.Show(MultiplayerSession.InSession);
    //}

    public override void OnCleanUp()
    {
        if (Game.Instance != null)
        {
            Game.Instance.Unsubscribe(MP_HASHES.OnPlayerJoined, OnPlayerJoined);
        }

        if (Instance == this)
            Instance = null;
        base.OnCleanUp();
    }

    private void OnPlayerJoined(object obj)
    {
        if (!isServer) return;

        /*
        foreach (var kvp in MultiplayerSession.ConnectedPlayers)
        {
            if (!_knownPlayers.Contains(kvp.Key))
            {
                _knownPlayers.Add(kvp.Key);
                SendHistoryToPlayer(kvp.Key);
            }
        }*/
    }

    public static void AddSystemMessage(string message)
    {
        UnityChatBoxUI.AddSystemMessage(message);
    }

    public void SendMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (text.Length > MAX_MESSAGE_LENGTH)
            text = text[..MAX_MESSAGE_LENGTH];

        ulong playerId = MultiplayerSession.LocalUserID;
        string playerName = Utils.GetLocalPlayerName();
        Color playerColor = CursorManager.Instance.color;
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        AddMessageToChatbox(playerName, text, timestamp, playerColor);

        string compressed = CompressString(text);
        CallCommand(nameof(ClientSendMessage), playerId, playerName, playerColor, compressed, timestamp);
    }

    [Command]
    public void ClientSendMessage(ulong playerId, string playerName, Color playerColor, string compressed, long timestamp)
    {
        if (string.IsNullOrEmpty(compressed)) return;

        string? message = DecompressString(compressed);
        if (string.IsNullOrEmpty(message)) return;

        string colorHex = ColorUtility.ToHtmlStringRGB(playerColor);
        string formatted = $"{playerName}: {message}";

        // Add it to the server chat history
        _chatHistory.Add(new PendingMessage
        {
            timestamp = timestamp,
            message = formatted,
            colorHex = colorHex            
        });

        // Add the message locally to the server's chat box. (because the server is also a client)
        string senderName = playerName;
        if (NetworkConfig.IsSteamConfig())
        {
            CSteamID cSteamId = playerId.AsCSteamID();
            if (SteamFriends.HasFriend(cSteamId, EFriendFlags.k_EFriendFlagImmediate))
                senderName = SteamFriends.GetFriendPersonaName(cSteamId);
        }

        // The command was called from the server and thus has already added the message to the chatbox
        if (playerId != MultiplayerSession.LocalUserID)
        {
            AddMessageToChatbox(senderName, message, timestamp, playerColor);
        }

        CallClientRpc(nameof(BroadcastMessage), playerId, playerName, playerColor, compressed, timestamp);
    }

    [ClientRpc]
    public void BroadcastMessage(ulong playerId, string playerName, Color playerColor, string compressed, long timestamp)
    {
        if (playerId == MultiplayerSession.LocalUserID)
            return;

        string? message = DecompressString(compressed);
        if (string.IsNullOrEmpty(message)) return;

        string senderName = playerName;
        if (NetworkConfig.IsSteamConfig())
        {
            CSteamID cSteamId = playerId.AsCSteamID();
            if (SteamFriends.HasFriend(cSteamId, EFriendFlags.k_EFriendFlagImmediate))
                senderName = SteamFriends.GetFriendPersonaName(cSteamId);
        }

        AddMessageToChatbox(senderName, message, timestamp, playerColor);
    }

	/* WIP
    private void SendHistoryToPlayer(ulong playerId)
    {
        if (_chatHistory.Count == 0) return;

        long[] timestamps = new long[_chatHistory.Count];
        string[] messages = new string[_chatHistory.Count];
        for (int i = 0; i < _chatHistory.Count; i++)
        {
            timestamps[i] = _chatHistory[i].timestamp;
            messages[i] = CompressString(_chatHistory[i].message);
        }

        CallTargetRpc(playerId, nameof(TargetRpcReceiveHistory), timestamps, messages);
    }

    [TargetRpc]
    public void TargetRpcReceiveHistory(long[] timestamps, string[] messages)
    {
        AddSystemMessage(STRINGS.UI.MP_CHATWINDOW.CHAT_INITIALIZED);

        for (int i = 0; i < timestamps.Length; i++)
        {
            string ts = DateTimeOffset.FromUnixTimeMilliseconds(timestamps[i]).DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            UnityChatBoxUI.Instance?.SendNewNewMessage("System", ts, DecompressString(messages[i]),messages[i].colorhex.ToColor);
        }
    }*/

	public static string CompressString(string text)
    {
        byte[] buffer = Encoding.UTF8.GetBytes(text);
        var memoryStream = new MemoryStream();
        using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
        {
            gZipStream.Write(buffer, 0, buffer.Length);
        }

        memoryStream.Position = 0;

        var compressedData = new byte[memoryStream.Length];
        memoryStream.Read(compressedData, 0, compressedData.Length);

        var gZipBuffer = new byte[compressedData.Length + 4];
        Buffer.BlockCopy(compressedData, 0, gZipBuffer, 4, compressedData.Length);
        Buffer.BlockCopy(BitConverter.GetBytes(buffer.Length), 0, gZipBuffer, 0, 4);
        return Convert.ToBase64String(gZipBuffer);
    }
    
    public static string DecompressString(string compressedText)
    {
        try
        {
            //return compressedText.Trim('`');
            byte[] gZipBuffer = Convert.FromBase64String(compressedText);
            using (var memoryStream = new MemoryStream())
            {
                int dataLength = BitConverter.ToInt32(gZipBuffer, 0);
                memoryStream.Write(gZipBuffer, 4, gZipBuffer.Length - 4);

                var buffer = new byte[dataLength];

                memoryStream.Position = 0;
                using (var gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                {
                    gZipStream.Read(buffer, 0, buffer.Length);
                }

                return Encoding.UTF8.GetString(buffer);
            }
        }
        catch (Exception ex) 
        {
            return string.Empty;
        }
    }

    public void AddMessageToChatbox(string sender, string message, long timestamp, Color color)
    {
        string timestampString = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        UnityChatBoxUI.Instance.SendNewChatMessage(sender, timestampString, message, color);
    }
}
