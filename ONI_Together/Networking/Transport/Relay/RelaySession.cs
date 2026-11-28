using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using LiteNetLib;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Misc;
using ONI_Together.Misc.World;
using ONI_Together.Networking.States;
using ONI_Together.Networking.Transport.Lan;
using ONI_Together.Patches.ToolPatches;
using Shared;
using Shared.Networking.Dedicated;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Transport.Relay
{
	public static class RelaySession
	{
		private const string HostedSaveSuffix = " (Dedicated)";

		private const int MaxSaveBytes = 512 * 1024 * 1024;
		private const float HostStartDelay = 1f;

		private static readonly ulong InstanceToken = CreateInstanceToken();

		private static RelaySaveAssembler _download;

		public static bool IsMaster { get; private set; }
		public static bool IsHosting { get; private set; }

		public static bool HostAfterLoad { get; private set; }

		internal static void ForgetPendingHost() => HostAfterLoad = false;

		private static ulong CreateInstanceToken()
		{
			byte[] bytes = Guid.NewGuid().ToByteArray();
			return BitConverter.ToUInt64(bytes, 0) | 1UL;
		}

		public static byte[] BuildHello()
		{
			using var _ = Profiler.Scope();

			var hello = new RelayProtocol.Hello { InstanceToken = InstanceToken };
			hello.ActiveDlcIds.AddRange(DlcManager.GetActiveDLCIds());
			hello.OwnedDlcIds.AddRange(DlcManager.GetOwnedDLCIds());
			return hello.ToBytes();
		}

		public static void Reset()
		{
			IsMaster = false;
			IsHosting = false;
			_download = null;
		}

		public static void OnConnectionLost()
		{
			using var _ = Profiler.Scope();

			bool wasHosting = IsHosting;
			Reset();

			if (!wasHosting || !Utils.IsInGame())
				return;

			try
			{
				SaveHelper.CaptureWorldSnapshot();
				DebugConsole.LogWarning("[Relay] Lost the dedicated server while hosting. The colony was saved locally.");
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[Relay] Lost the dedicated server and the local save failed: {ex}");
			}
		}

		public static bool SendFrame(byte[] frame, byte channel = 0, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)
		{
			NetPeer relay = LiteNetLibClient.ServerPeer;
			if (relay == null || relay.ConnectionState != ConnectionState.Connected)
				return false;

			if (deliveryMethod == DeliveryMethod.Unreliable && frame.Length > relay.GetMaxSinglePacketSize(DeliveryMethod.Unreliable))
				deliveryMethod = DeliveryMethod.ReliableUnordered;

			try
			{
				relay.Send(frame, channel, deliveryMethod);
				return true;
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[Relay] Failed to send {RelayProtocol.TypeOf(frame)} ({frame.Length} bytes): {ex.Message}");
				return false;
			}
		}

		public static void HandleFrame(byte[] frame)
		{
			using var _ = Profiler.Scope();

			var host = NetworkConfig.TransportServer as RelayHostTransport;

			switch (RelayProtocol.TypeOf(frame))
			{
				case RelayFrameType.MasterAssigned:
					OnMasterAssigned(frame);
					break;

				case RelayFrameType.SaveBegin:
				case RelayFrameType.SaveData:
					if (_download != null && _download.Accept(frame, out string name, out byte[] data))
					{
						_download = null;
						DebugConsole.Log($"[Relay] Received the server's colony '{name}' ({Utils.FormatBytes(data.Length)}). Loading it.");

						if (!SaveHelper.SavegameDlcListValid(data, out string dlcError))
						{
							DebugConsole.LogWarning($"[Relay] This game cannot load the server's colony: {dlcError}");
							IsMaster = false;
							LiteNetLibClient.ServerPeer?.Disconnect(RelayProtocol.Signal(RelayFrameType.HostAborted));
							LeaveWithMessage(dlcError);
							break;
						}

						HostAfterLoad = true;
						MultiplayerSession.InActiveSession = false;
						SaveHelper.RequestWorldLoad(new WorldSave(HostedSaveName(name) + ".sav", data));
					}
					break;

				case RelayFrameType.PeerJoined:
				case RelayFrameType.PeerLeft:
					if (host != null && frame.Length >= RelayProtocol.PeerHeaderSize)
					{
						ulong clientId = BitConverter.ToUInt64(frame, RelayProtocol.HeaderSize);
						if (RelayProtocol.TypeOf(frame) == RelayFrameType.PeerJoined)
							host.PeerJoined(clientId);
						else
							host.PeerLeft(clientId);
					}
					break;

				case RelayFrameType.FromPeer:
					if (host != null && RelayProtocol.TryReadPeerData(frame, out ulong _, out byte[] payload))
						Packets.Architecture.PacketHandler.HandleIncoming(payload);
					break;

				default:
					DebugConsole.LogWarning($"[Relay] Unexpected frame: {RelayProtocol.TypeOf(frame)}");
					break;
			}
		}

		private static void OnMasterAssigned(byte[] frame)
		{
			int version;
			bool hasSave;
			using (var reader = RelayProtocol.ReadBody(frame))
			{
				version = reader.ReadInt32();
				hasSave = reader.ReadBoolean();
			}

			MP_Timer.Instance.Abort();

			if (version != RelayProtocol.Version)
			{
				LeaveWithMessage(string.Format(STRINGS.UI.MP_OVERLAY.CLIENT.RELAY.VERSION_MISMATCH, version, RelayProtocol.Version));
				return;
			}

			IsMaster = true;
			DebugConsole.Log($"[Relay] This game hosts for the dedicated server. In game: {Utils.IsInGame()}, server has a colony: {hasSave}.");

			if (Utils.IsInGame() && HostAfterLoad)
			{
				CoroutineRunner.RunOne(StartHosting());
			}
			else if (hasSave)
			{
				if (Utils.IsInGame())
					DebugConsole.Log("[Relay] This world was not loaded to host; loading the server's colony instead.");

				MultiplayerOverlay.Show(STRINGS.UI.MP_OVERLAY.CLIENT.RELAY.LOADING_COLONY);
				_download = new RelaySaveAssembler(MaxSaveBytes);
				SendFrame(RelayProtocol.Signal(RelayFrameType.SaveRequest));
			}
			else
			{
				LeaveWithMessage(STRINGS.UI.MP_OVERLAY.CLIENT.RELAY.NO_COLONY);
			}
		}

		private static void LeaveWithMessage(string message)
		{
			TransportClient link = NetworkConfig.TransportClient;
			link.Disconnect();
			link.OnReturnToMenu?.Invoke(string.Empty, message);
		}

		private static IEnumerator StartHosting()
		{
			yield return new WaitForSecondsRealtime(HostStartDelay);

			if (!IsMaster || IsHosting || !Utils.IsInGame())
				yield break;

			TransportClient link = NetworkConfig.TransportClient;

			GameClient.ClearCachedConnection();
			GameClient.ResetReconnectState();
			GameClient.SetState(ClientState.Disconnected);
			MultiplayerSession.Clear();

			NetworkConfig.UseRelayHost(link);
			GameServer.Start();
			IsHosting = true;
			HostAfterLoad = false;

			SelectToolPatch.UpdateColor();
			Game.Instance.Trigger(MP_HASHES.OnMultiplayerGameSessionInitialized);
			SpeedControlScreen.Instance.Pause(true);
			MultiplayerOverlay.Close();

			SendFrame(RelayProtocol.Signal(RelayFrameType.HostReady));

			SaveHelper.CaptureWorldSnapshot();
		}

		private static string HostedSaveName(string colonyName)
		{
			return colonyName.EndsWith(HostedSaveSuffix, StringComparison.Ordinal)
				? colonyName
				: colonyName + HostedSaveSuffix;
		}

		private static void UploadSave(string path)
		{
			using var _ = Profiler.Scope();

			byte[] data = File.ReadAllBytes(path);
			string colonyName = SaveGame.Instance != null ? SaveGame.Instance.BaseName : Path.GetFileNameWithoutExtension(path);

			foreach (byte[] frame in RelayProtocol.SaveFrames(colonyName, data))
			{
				if (!SendFrame(frame, RelayProtocol.SaveChannel))
					return;
			}

			DebugConsole.Log($"[Relay] Sent the colony '{colonyName}' to the dedicated server ({Utils.FormatBytes(data.Length)}).");
		}

		[HarmonyPatch(typeof(SaveLoader), nameof(SaveLoader.Save), new[] { typeof(string), typeof(bool), typeof(bool) })]
		private static class SaveLoader_Save_Patch
		{
			private static void Postfix(string filename)
			{
				if (!IsHosting)
					return;

				try
				{
					UploadSave(filename);
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[Relay] Could not send the save to the dedicated server: {ex}");
				}
			}
		}
	}
}
