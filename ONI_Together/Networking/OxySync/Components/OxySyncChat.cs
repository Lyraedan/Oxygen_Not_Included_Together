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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using ONI_Together.DebugTools;
using Shared;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components;

[SkipSaveFileSerialization]
[FixedInterestGroup]
public class OxySyncChat : NetworkBehaviour
{
    public const int MAX_MESSAGE_LENGTH = 256;

    public struct PendingMessage
    {
        public string sender;
        public long timestamp;
        public string message;
        public Color color;
    }
    
    public static OxySyncChat? Instance { get; private set; }

    private static List<PendingMessage> _chatHistory = new();
    private List<long> _timestampCache = new ();
    
    public static IReadOnlyList<PendingMessage> ChatHistory => _chatHistory;

    public override void OnSpawn()
    {
        base.OnSpawn();
        Instance = this;
        NetId = nameof(OxySyncChat).GetHashCode();
        InterestGroup = -1;
        
        if (Game.Instance != null)
        {
            Game.Instance.Subscribe(MP_HASHES.OnPlayerJoined, OnPlayerJoined);
        }
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

        ulong playerId = Boxed<ulong>.Unbox(obj);
        //SendHistoryToPlayer(playerId);
        //StartCoroutine(DelayedSendChatHistory(playerId));
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
        CallCommand(CmdSendMessage, playerId, playerName, playerColor, text, timestamp);
    }

    [Command]
    public void CmdSendMessage(ulong playerId, string playerName, Color playerColor, string message, long timestamp)
    {
        if (string.IsNullOrEmpty(message)) return;

        string colorHex = ColorUtility.ToHtmlStringRGB(playerColor);
        string formatted = $"{playerName}: {message}";

        // Add it to the server chat history
        _chatHistory.Add(new PendingMessage
        {
            sender = playerName,
            timestamp = timestamp,
            message = formatted,
            color = playerColor            
        });
        
        CallClientRpc(RpcBroadcastMessage, playerId, playerName, playerColor, message, timestamp);
    }

    [ClientRpc(IncludeHost = true)]
    public void RpcBroadcastMessage(ulong playerId, string playerName, Color playerColor, string message, long timestamp)
    {
        if (playerId == MultiplayerSession.LocalUserID)
            return;

        string senderName = playerName;
        if (NetworkConfig.IsSteamConfig())
        {
            CSteamID cSteamId = playerId.AsCSteamID();
            if (SteamFriends.HasFriend(cSteamId, EFriendFlags.k_EFriendFlagImmediate))
                senderName = SteamFriends.GetFriendPersonaName(cSteamId);
        }

        AddMessageToChatbox(senderName, message, timestamp, playerColor);
    }

    private void SendHistoryToPlayer(ulong playerId, int maxMessages = 10)
    {
        if (_chatHistory.Count == 0) return;

        int count = Math.Min(maxMessages, _chatHistory.Count);
        int startIndex = _chatHistory.Count - count;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        writer.Write(count);
        for (int i = startIndex; i < _chatHistory.Count; i++)
        {
            var msg = _chatHistory[i];
            writer.Write(Utils.CompressString(msg.sender));
            writer.Write(msg.timestamp);
            writer.Write(Utils.CompressString(msg.message));
            writer.Write(msg.color);
        }

        DebugConsole.Log($"Sending chat history: {count} messages to {playerId}");
        CallTargetRpc(playerId, nameof(TargetRpcReceiveHistory), ms.ToArray());
    }

    [TargetRpc]
    public void TargetRpcReceiveHistory(byte[] data)
    {
        DebugConsole.Log("Chat history: Target RPC triggered!");
        // Really what we should do is add a history list to UnityChatBox and process it after Init
        AddSystemMessage(STRINGS.UI.MP_CHATWINDOW.CHAT_INITIALIZED);

        using var reader = new BinaryReader(new MemoryStream(data));
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string sender = Utils.DecompressString(reader.ReadString());
            long timestamp = reader.ReadInt64();
            string message = Utils.DecompressString(reader.ReadString());
            Color color = reader.ReadColor();
            AddMessageToChatbox(sender, message, timestamp, color);
        }
    }

    IEnumerator DelayedSendChatHistory(ulong playerId)
    {
        yield return new WaitForSecondsRealtime(3f);
        SendHistoryToPlayer(playerId);
    }
    
    public void AddMessageToChatbox(string sender, string message, long timestamp, Color color)
    {
        // Only add messages to the chatbox if they don't already exist. Protects against retransmissions duplicating messages
        if (!_timestampCache.Contains(timestamp))
        {
            string timestampString = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            UnityChatBoxUI.Instance.SendNewChatMessage(sender, timestampString, message, color);
            _timestampCache.Add(timestamp);
        }
    }

    public void RequestChatHistory()
    {
        CallCommand(CmdRequestChatHistory, MultiplayerSession.LocalUserID);
    }
    
    [Command]
    public void CmdRequestChatHistory(ulong playerId)
    {
        SendHistoryToPlayer(playerId);
    }
}
