using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Handshake
{
	public class GameStateRequestPacket : IPacket
	{
		public GameStateRequestPacket() { }
		public GameStateRequestPacket(ulong steamID)
		{
			ClientId = steamID;
		}

		public ulong ClientId;
		public HashSet<string> ActiveDlcIds = [];
		public List<ulong> ActiveModIds = [];
		public int ProtocolVersion;
		public int PacketRegistryFingerprint;
		public string ModVersion = string.Empty;
		public bool ProtocolAccepted = true;
		public string ProtocolFailureReason = string.Empty;

		public bool HasProtocolMetadata { get; private set; }

		public static GameStateRequestPacket CreateClientRequest(ulong clientId)
		{
			using var _ = Profiler.Scope();

			var packet = new GameStateRequestPacket(clientId);
			packet.PopulateProtocolMetadata();
			return packet;
		}

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(ClientId);
			writer.Write(ActiveDlcIds.Count);
			foreach (var id in ActiveDlcIds)
			{
				writer.Write(id);
			}
			writer.Write(ActiveModIds.Count);
			foreach (var id in ActiveModIds)
			{
				writer.Write(id);
			}

			writer.Write(ProtocolVersion);
			writer.Write(PacketRegistryFingerprint);
			writer.Write(ModVersion ?? string.Empty);
			writer.Write(ProtocolAccepted);
			writer.Write(ProtocolFailureReason ?? string.Empty);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			ClientId = reader.ReadUInt64();
			int count = reader.ReadInt32();
			ActiveDlcIds = new HashSet<string>(count);
			for (int i = 0; i < count; i++)
			{
				ActiveDlcIds.Add(reader.ReadString());
			}

			count = reader.ReadInt32();
			ActiveModIds = new List<ulong>(count);
			for (int i = 0; i < count; i++)
			{
				ActiveModIds.Add(reader.ReadUInt64());
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
			ProtocolAccepted = reader.ReadBoolean();
			ProtocolFailureReason = reader.ReadString();
			HasProtocolMetadata = true;
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InActiveSession)
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

			if (!IsProtocolCompatible(out string reason))
			{
				DebugConsole.LogWarning($"[GameStateRequestPacket] Rejecting client {ClientId}: {reason}");
				RejectClient(reason);
				return;
			}

			MarkClientAsProtocolVerified();
			CreateStateResponse();
		}

		private void CreateStateResponse()
		{
			using var _ = Profiler.Scope();

			PacketSender.SendToPlayer(ClientId, AccumulateStateInfo());
		}

		private static GameStateRequestPacket AccumulateStateInfo(bool protocolAccepted = true, string protocolFailureReason = "")
		{
			using var _ = Profiler.Scope();

			var packet = new GameStateRequestPacket();
			packet.PopulateProtocolMetadata();
			packet.ProtocolAccepted = protocolAccepted;
			packet.ProtocolFailureReason = protocolFailureReason ?? string.Empty;

			if (!protocolAccepted)
			{
				return packet;
			}

			packet.ActiveDlcIds = SaveLoader.Instance.GameInfo.dlcIds.ToHashSet();
			packet.ActiveModIds.Clear();

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
			HasProtocolMetadata = true;
		}

		private bool IsProtocolCompatible(out string reason)
		{
			using var _ = Profiler.Scope();

            if (Configuration.Instance.Network.BypassProtocolCompatibilityChecks)
            {
				reason = string.Empty;
                return true;
			}

			if (!HasProtocolMetadata)
			{
				reason = ProtocolCompatibility.BuildMismatchReason(ProtocolVersion, PacketRegistryFingerprint, ModVersion, false);
				return false;
			}

			if (!ProtocolCompatibility.Matches(ProtocolVersion, PacketRegistryFingerprint))
			{
				reason = ProtocolCompatibility.BuildMismatchReason(ProtocolVersion, PacketRegistryFingerprint, ModVersion, true);
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
