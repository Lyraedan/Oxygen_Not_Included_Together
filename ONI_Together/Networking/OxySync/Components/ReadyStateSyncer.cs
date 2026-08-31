using KSerialization;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.States;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
	[SkipSaveFileSerialization]
	[FixedInterestGroup]
	public class ReadyStateSyncer : NetworkBehaviour
	{
		public const float HEARTBEAT_INTERVAL = 2f;

		public static ReadyStateSyncer? Instance { get; private set; }

		[SyncVar(Hook = nameof(OnReadyStatusTextChanged), SendMode = (int)PacketSendMode.ReliableImmediate)]
		private string _readyStatusText = string.Empty;

		private ClientReadyState _localState = ClientReadyState.Unready;
		private float _lastHeartbeatSendTime;

		public override void OnSpawn()
		{
			base.OnSpawn();
			Instance = this;
			SyncInterval = 0.5f;
			NetId = nameof(ReadyStateSyncer).GetHashCode();
			InterestGroup = -1;
		}

		public override void OnCleanUp()
		{
			if (Instance == this)
				Instance = null;
			base.OnCleanUp();
		}

		/// <summary>
		/// Client-side: records the local ready state and immediately reports it to the host.
		/// </summary>
		[Client]
		public void RequestSetReadyState(ClientReadyState state)
		{
			_localState = state;
			if (!inSession)
				return;
			CallCommand(CmdSetReadyState, NetworkConfig.GetLocalID(), (int)state, Utils.GetLocalPlayerName());
		}

		/// <summary>
		/// Host-side: pushes the aggregated ready status to all clients via the SyncVar.
		/// </summary>
		[Server]
		public void PushReadyStatusText(string text)
		{
			if (!isServer)
				return;
			
			_readyStatusText = text;
		}

		/// <summary>
		/// Host-side: broadcasts the all-ready signal to clients (closes their overlays).
		/// </summary>
		[Server]
		public void BroadcastAllReady()
		{
			if (!isServer)
				return;
			CallClientRpc(RpcAllReady);
		}

		[Command]
		private void CmdSetReadyState(ulong senderId, int state, string playerName)
		{
			if (!isServer)
				return;

			if (senderId == MultiplayerSession.HostUserID)
				return;

			var readyState = (ClientReadyState)state;

			if (readyState == ClientReadyState.Loading)
			{
				ReadyManager.MarkClientLoading(senderId);
				return;
			}

			if (!MultiplayerSession.ConnectedPlayers.TryGetValue(senderId, out var player))
				return;

			bool firstHeartbeat = ReadyManager.RegisterHeartbeat(senderId);
			ReadyManager.ReportPlayerIdentity(player, playerName, firstHeartbeat);

			bool stateChanged = player.readyState != readyState;
			if (stateChanged)
				ReadyManager.SetPlayerReadyState(player, readyState);

			ReadyManager.CompleteLoading(senderId);

			if (stateChanged || firstHeartbeat)
				ReadyManager.RefreshReadyState();
		}

		[ClientRpc]
		private void RpcAllReady()
		{
			AllClientsReadyPacket.ProcessAllReady();
		}

		private void OnReadyStatusTextChanged(string oldValue, string newValue)
		{
			if (string.IsNullOrEmpty(newValue))
				return;
			MultiplayerOverlay.Show(newValue);
		}

		/// <summary>
		/// Client-side continuous heartbeat. Re-sends the current local ready state on an
		/// interval so the host always has a fresh "last seen" timestamp.
		/// </summary>
		private void Update()
		{
			if (isServer)
				return;
			if (!inSession)
				return;

			var state = GameClient.State;
			if (state != ClientState.Connected && state != ClientState.InGame)
				return;

			if (Time.unscaledTime - _lastHeartbeatSendTime < HEARTBEAT_INTERVAL)
				return;
			_lastHeartbeatSendTime = Time.unscaledTime;
			CallCommand(CmdSetReadyState, NetworkConfig.GetLocalID(), (int)_localState, Utils.GetLocalPlayerName());
		}
	}
}