using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Profiling;
using UnityEngine;
using ONI_Together.Misc;
using ONI_Together.Networking.Transport.Steamworks;

namespace ONI_Together.Networking.Packets.Handshake
{
	public class GameStateRequestPacket : IPacket
	{
		internal enum ReconnectProofDecision
		{
			FreshSnapshot,
			ResumeLoading,
			Reject
		}

		internal const int MaxDlcCount = 64;
		internal const int MaxModCount = 4096;
		internal const int MaxMetadataChars = 2048;
		public GameStateRequestPacket() { }
		public GameStateRequestPacket(ulong steamID)
		{
			ClientId = steamID;
		}

		public ulong ClientId;
		public ulong ReconnectToken;
		public byte[] LobbyAccessProof = System.Array.Empty<byte>();
		public HashSet<string> ActiveDlcIds = [];
		public List<ulong> ActiveModIds = [];
		public List<string> ActiveModFingerprints = [];
		public int ProtocolVersion;
		public int PacketRegistryFingerprint;
		public string ModVersion = string.Empty;
		public int GameBuild;
		public string ModBuildFingerprint = string.Empty;
		public bool ProtocolAccepted = true;
		public string ProtocolFailureReason = string.Empty;

		public bool HasProtocolMetadata { get; private set; }

		public static GameStateRequestPacket CreateClientRequest(ulong clientId)
		{
			using var _ = Profiler.Scope();

			var packet = new GameStateRequestPacket(clientId);
			packet.ReconnectToken = ReadyManager.ReconnectToken;
			packet.LobbyAccessProof = SteamLobby.CreateCurrentLobbyAccessProof(clientId);
			packet.PopulateProtocolMetadata();
			return packet;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(ClientId);
			writer.Write(ReconnectToken);
			if (LobbyAccessProof == null
			    || LobbyAccessProof.Length != 0 && LobbyAccessProof.Length != PasswordHelper.AccessProofBytes)
				throw new InvalidDataException("Invalid lobby access proof length");
			writer.Write((byte)LobbyAccessProof.Length);
			writer.Write(LobbyAccessProof);
			writer.Write(ActiveDlcIds.Count);
			foreach (var id in ActiveDlcIds)
			{
				ValidateString(id, "DLC id");
				writer.Write(id);
			}
			writer.Write(ActiveModIds.Count);
			foreach (var id in ActiveModIds)
			{
				writer.Write(id);
			}
			writer.Write(ActiveModFingerprints.Count);
			foreach (string fingerprint in ActiveModFingerprints)
			{
				ValidateString(fingerprint, "mod fingerprint");
				writer.Write(fingerprint);
			}

			writer.Write(ProtocolVersion);
			writer.Write(PacketRegistryFingerprint);
			writer.Write(ModVersion ?? string.Empty);
			writer.Write(GameBuild);
			writer.Write(ModBuildFingerprint ?? string.Empty);
			writer.Write(ProtocolAccepted);
			writer.Write(ProtocolFailureReason ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			ClientId = reader.ReadUInt64();
			ReconnectToken = reader.ReadUInt64();
			int proofLength = reader.ReadByte();
			if (proofLength != 0 && proofLength != PasswordHelper.AccessProofBytes)
				throw new InvalidDataException("Invalid lobby access proof length");
			LobbyAccessProof = reader.ReadBytes(proofLength);
			if (LobbyAccessProof.Length != proofLength)
				throw new EndOfStreamException("Truncated lobby access proof");
			int count = reader.ReadInt32();
			ValidateCount(count, MaxDlcCount, "DLC");
			ActiveDlcIds = new HashSet<string>(count);
			for (int i = 0; i < count; i++)
			{
				ActiveDlcIds.Add(ReadBoundedString(reader, "DLC id"));
			}

			count = reader.ReadInt32();
			ValidateCount(count, MaxModCount, "mod");
			ActiveModIds = new List<ulong>(count);
			for (int i = 0; i < count; i++)
			{
				ActiveModIds.Add(reader.ReadUInt64());
			}

			count = reader.ReadInt32();
			ValidateCount(count, MaxModCount, "mod fingerprint");
			ActiveModFingerprints = new List<string>(count);
			for (int i = 0; i < count; i++)
			{
				ActiveModFingerprints.Add(ReadBoundedString(reader, "mod fingerprint"));
			}

			HasProtocolMetadata = false;
			ProtocolAccepted = true;
			ProtocolFailureReason = string.Empty;
			if (reader.BaseStream.Position >= reader.BaseStream.Length)
			{
				return;
			}

			ProtocolVersion = reader.ReadInt32();
			PacketRegistryFingerprint = reader.ReadInt32();
			ModVersion = reader.ReadString();
			if (ModVersion.Length > MaxMetadataChars)
				throw new InvalidDataException("Mod version metadata is too long");
			GameBuild = reader.ReadInt32();
			ModBuildFingerprint = reader.ReadString();
			if (ModBuildFingerprint.Length > MaxMetadataChars)
				throw new InvalidDataException("Mod build fingerprint is too long");
			ProtocolAccepted = reader.ReadBoolean();
			ProtocolFailureReason = reader.ReadString();
			if (ProtocolFailureReason.Length > MaxMetadataChars)
				throw new InvalidDataException("Protocol failure reason is too long");
			HasProtocolMetadata = true;
		}

		private static void ValidateCount(int count, int maximum, string name)
		{
			if (count < 0 || count > maximum)
				throw new InvalidDataException($"Invalid {name} count: {count}");
		}

		private static string ReadBoundedString(BinaryReader reader, string name)
		{
			string value = reader.ReadString();
			ValidateString(value, name);
			return value;
		}

		private static void ValidateString(string value, string name)
		{
			if (string.IsNullOrEmpty(value) || value.Length > MaxMetadataChars)
				throw new InvalidDataException($"Invalid {name} length");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession)
				return;

			if (MultiplayerSession.IsHost)
			{
				HandleHostRequest();
			}
			else
			{
				ConsumeStateResponse();
			}
		}

		private void HandleHostRequest()
		{
			using var _ = Profiler.Scope();
			DispatchContext context = PacketHandler.CurrentContext;
			if (!IsHostRequestSenderValid(ClientId, context.SenderId, context.SenderIsHost))
			{
				DebugConsole.LogWarning($"[GameStateRequestPacket] Ignoring spoofed client id {ClientId} from {context.SenderId}");
				return;
			}

			if (NetworkConfig.IsSteamConfig()
			    && !SteamLobby.ValidateCurrentLobbyAccess(ClientId, LobbyAccessProof))
			{
				DebugConsole.LogWarning($"[GameStateRequestPacket] Rejecting unauthorized lobby client {ClientId}");
				RejectClient("Steam lobby access was not authorized by the host.");
				return;
			}

			if (!IsProtocolCompatible(out string reason))
			{
				DebugConsole.LogWarning($"[GameStateRequestPacket] Rejecting client {ClientId}: {reason}");
				RejectClient(reason);
				return;
			}

			ReconnectProofStatus reconnectProofStatus = ReconnectProofStatus.Missing;
			if (ReconnectToken != 0)
			{
				if (NetworkConfig.TransportServer is ONI_Together.Networking.Transport.Lan.RiptideServer server)
				{
					reconnectProofStatus = ReadyManager.GetReconnectProofStatus(
						ClientId, ReconnectToken, requireSameCompletedClient: false);
					if (reconnectProofStatus != ReconnectProofStatus.Completed
					    && server.TryResumeLoadingClient(
						    ReconnectToken,
						    ClientId,
						    previousId => ReadyManager.TransferSyncBarrierClient(
							    previousId, ClientId, ReconnectToken),
						    out ulong resumedPreviousId))
					{
						reconnectProofStatus = ReadyManager.GetReconnectProofStatus(
							ClientId, ReconnectToken, requireSameCompletedClient: false);
					}
				}
				else
				{
					reconnectProofStatus = ReadyManager.GetReconnectProofStatus(
						ClientId, ReconnectToken, requireSameCompletedClient: true);
				}
			}

			ReconnectProofDecision reconnectDecision = EvaluateReconnectProof(
				NetworkConfig.IsSteamConfig(), ReconnectToken, reconnectProofStatus);
				if (reconnectDecision == ReconnectProofDecision.Reject)
				{
					RejectClient("Loading reconnect proof is stale or does not match this client.");
					return;
				}
				if (reconnectDecision == ReconnectProofDecision.FreshSnapshot)
					ReadyManager.PrepareFreshSnapshot(ClientId);
				if (ReconnectToken != 0 && reconnectDecision == ReconnectProofDecision.FreshSnapshot)
					DebugConsole.Log("[GameStateRequestPacket] Stale reconnect proof requires a fresh snapshot.");

			MarkClientAsProtocolVerified();
			CreateStateResponse(
				reconnectDecision == ReconnectProofDecision.ResumeLoading ? ReconnectToken : 0);
		}

		internal static ReconnectProofDecision EvaluateReconnectProof(
			bool isSteam,
			ulong reconnectToken,
			ReconnectProofStatus proofStatus)
		{
			if (reconnectToken == 0)
				return ReconnectProofDecision.FreshSnapshot;
			if (proofStatus == ReconnectProofStatus.Active)
				return ReconnectProofDecision.ResumeLoading;
				if (proofStatus == ReconnectProofStatus.Completed)
					return ReconnectProofDecision.FreshSnapshot;
				return ReconnectProofDecision.FreshSnapshot;
		}

		internal static bool IsHostRequestSenderValid(ulong clientId, ulong senderId, bool senderIsHost)
		{
			return !senderIsHost && clientId == senderId;
		}

		private void CreateStateResponse(ulong acceptedReconnectToken)
		{
			using var _ = Profiler.Scope();

			PacketSender.SendToPlayer(
				ClientId,
				AccumulateStateInfo(acceptedReconnectToken: acceptedReconnectToken));
		}

		private static GameStateRequestPacket AccumulateStateInfo(
			bool protocolAccepted = true,
			string protocolFailureReason = "",
			ulong acceptedReconnectToken = 0)
		{
			using var _ = Profiler.Scope();

			var packet = new GameStateRequestPacket();
			packet.PopulateProtocolMetadata();
			packet.ProtocolAccepted = protocolAccepted;
			packet.ProtocolFailureReason = protocolFailureReason ?? string.Empty;
			packet.ReconnectToken = acceptedReconnectToken;

			if (!protocolAccepted)
			{
				return packet;
			}

			packet.ActiveDlcIds = SaveLoader.Instance.GameInfo.dlcIds.ToHashSet();
			packet.ActiveModIds.Clear();
			packet.ActiveModFingerprints = SaveHelper.GetActiveModFingerprints();

			KMod.Manager modManager = Global.Instance.modManager;
			foreach (var mod in modManager.mods)
			{
				if (mod.IsEnabledForActiveDlc() && mod.label.distribution_platform == KMod.Label.DistributionPlatform.Steam && ulong.TryParse(mod.label.id, out var steamId))
				{
					packet.ActiveModIds.Add(steamId);
				}
			}
			return packet;
		}

		private void ConsumeStateResponse()
		{
			using var _ = Profiler.Scope();

			GameClient.OnHostResponseReceived(this);
		}

		private void PopulateProtocolMetadata()
		{
			using var _ = Profiler.Scope();

			ProtocolVersion = ProtocolCompatibility.CurrentProtocolVersion;
			PacketRegistryFingerprint = ProtocolCompatibility.PacketFingerprint;
			ModVersion = ProtocolCompatibility.ModVersion;
			GameBuild = ProtocolCompatibility.GameBuild;
			ModBuildFingerprint = ProtocolCompatibility.ModBuildFingerprint;
			HasProtocolMetadata = true;
		}

		private bool IsProtocolCompatible(out string reason)
		{
			using var _ = Profiler.Scope();

			if (!HasProtocolMetadata)
			{
				reason = ProtocolCompatibility.BuildMismatchReason(
					ProtocolVersion, PacketRegistryFingerprint, ModVersion,
					GameBuild, ModBuildFingerprint, false);
				return false;
			}

			if (!ProtocolCompatibility.Matches(
				    ProtocolVersion, PacketRegistryFingerprint, ModVersion,
				    GameBuild, ModBuildFingerprint))
			{
				reason = ProtocolCompatibility.BuildMismatchReason(
					ProtocolVersion, PacketRegistryFingerprint, ModVersion,
					GameBuild, ModBuildFingerprint, true);
				return false;
			}

			reason = string.Empty;
			return true;
		}

		private void MarkClientAsProtocolVerified()
		{
			using var _ = Profiler.Scope();

			var player = MultiplayerSession.GetPlayer(ClientId);
			if (player != null)
			{
				player.ProtocolVerified = true;
			}
		}

		private void RejectClient(string reason)
		{
			using var _ = Profiler.Scope();

			var player = MultiplayerSession.GetPlayer(ClientId);
			if (player != null)
			{
				player.ProtocolVerified = false;
			}

			if (HasProtocolMetadata)
			{
				PacketSender.SendToPlayer(ClientId, AccumulateStateInfo(protocolAccepted: false, protocolFailureReason: reason), PacketSendMode.ReliableImmediate);
			}

			if (Game.Instance != null)
			{
				Game.Instance.StartCoroutine(DelayedKick(ClientId, HasProtocolMetadata ? 0.25f : 0f));
				return;
			}

			NetworkConfig.TransportServer?.KickClient(ClientId);
		}

		private static IEnumerator DelayedKick(ulong clientId, float delay)
		{
			if (delay > 0f)
			{
				yield return new WaitForSecondsRealtime(delay);
			}

			NetworkConfig.TransportServer?.KickClient(clientId);
		}
	}
}
