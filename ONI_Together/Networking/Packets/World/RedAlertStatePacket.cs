using System.Collections.Generic;
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using Shared.Interfaces.Networking;
using static ONI_Together.Patches.World.MeterScreenPatches;

namespace ONI_Together.Networking.Packets.World
{
	public class RedAlertStatePacket : IPacket, IClientRelayable, IHostAuthoritativeRelay
	{
		private static readonly object RevisionLock = new();
		private static readonly Dictionary<int, long> HostRevisions = new();
		private static readonly Dictionary<int, long> AppliedRevisions = new();

		public int ActiveWorldID;
		public bool IsRedAlert;
		public long Revision;

		public static void ResetSessionState()
		{
			lock (RevisionLock)
			{
				HostRevisions.Clear();
				AppliedRevisions.Clear();
			}
		}

		public static void SubmitLocalChange(int worldId, bool isRedAlert)
		{
			if (!MultiplayerSession.InSession || worldId < 0)
				return;
			var packet = new RedAlertStatePacket
			{
				ActiveWorldID = worldId,
				IsRedAlert = isRedAlert
			};
			if (MultiplayerSession.IsHost)
				BroadcastAuthoritative(packet);
			else
				PacketSender.SendToAllOtherPeers(packet);
		}

		public void Serialize(BinaryWriter writer)
		{
			writer.Write(ActiveWorldID);
			writer.Write(IsRedAlert);
			writer.Write(Revision);
		}

		public void Deserialize(BinaryReader reader)
		{
			int worldId = reader.ReadInt32();
			bool isRedAlert = reader.ReadBoolean();
			long revision = reader.ReadInt64();
			if (worldId < 0 || revision < 0)
				throw new InvalidDataException("Invalid red-alert command");
			ActiveWorldID = worldId;
			IsRedAlert = isRedAlert;
			Revision = revision;
		}

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			if (MultiplayerSession.IsHost)
			{
				HandleClientRequest(context);
				return;
			}
			if (ResolveWorld() == null
			    || !context.SenderIsHost
			    || !TryAcceptAuthoritativeRevision(ActiveWorldID, Revision))
				return;
			ApplyState();
		}

		private void HandleClientRequest(DispatchContext context)
		{
			if (!context.IsVerifiedHostBroadcast || Revision != 0)
			{
				DebugConsole.LogWarning("[RedAlertStatePacket] Rejected non-request client command");
				return;
			}
			if (ApplyState())
				BroadcastAuthoritative(this);
		}

		private static void BroadcastAuthoritative(RedAlertStatePacket packet)
		{
			lock (RevisionLock)
			{
				HostRevisions.TryGetValue(packet.ActiveWorldID, out long revision);
				packet.Revision = revision + 1;
				HostRevisions[packet.ActiveWorldID] = packet.Revision;
			}
			PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}

		internal static bool TryAcceptAuthoritativeRevision(int worldId, long revision)
		{
			if (worldId < 0 || revision <= 0)
				return false;
			lock (RevisionLock)
			{
				AppliedRevisions.TryGetValue(worldId, out long previous);
				if (revision <= previous)
					return false;
				AppliedRevisions[worldId] = revision;
				return true;
			}
		}

		private WorldContainer ResolveWorld()
			=> ClusterManager.Instance?.GetWorld(ActiveWorldID);

		private bool ApplyState()
		{
			WorldContainer activeWorld = ResolveWorld();
			if (activeWorld == null || MeterScreen_RedAlertPatch.IsSyncing)
				return false;
			MeterScreen_RedAlertPatch.IsSyncing = true;
			try
			{
				activeWorld.AlertManager.ToggleRedAlert(IsRedAlert);
				if (ClusterManager.Instance.activeWorldId == ActiveWorldID)
					KMonoBehaviour.PlaySound(GlobalAssets.GetSound(
						IsRedAlert ? "HUD_Click_Open" : "HUD_Click_Close"));
			}
			finally
			{
				MeterScreen_RedAlertPatch.IsSyncing = false;
			}
			return true;
		}
	}
}
