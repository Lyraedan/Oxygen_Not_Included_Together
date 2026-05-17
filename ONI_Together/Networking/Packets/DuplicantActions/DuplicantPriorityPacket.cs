using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using HarmonyLib;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.DuplicantActions
{
	public class DuplicantPriorityPacket : IPacket
	{
		public int NetId;
		public string ChoreGroupId;
		public int Priority;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(ChoreGroupId ?? string.Empty);
			writer.Write(Priority);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			ChoreGroupId = reader.ReadString();
			Priority = reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
			{
				// Host receives from client, apply and broadcast
				Apply();

				// Broadcast to other clients
				PacketSender.SendToAllClients(this);
			}
			else
			{
				// Client receives from host
				Apply();
			}
		}

		private void Apply()
		{
			using var _ = Profiler.Scope();

			// First try normal registry lookup
			if (!NetworkIdentityRegistry.TryGet(NetId, out var identity) || identity == null)
			{
				// Not in registry - try to find and force-register
				identity = TryFindAndRegisterIdentity(NetId);
				if (identity == null)
				{
					DebugConsole.LogWarning($"[DuplicantPriorityPacket] NetId {NetId} not found anywhere.");
					return;
				}
			}

			var consumer = identity.GetComponent<ChoreConsumer>();
			if (consumer == null)
			{
				DebugConsole.LogWarning($"[DuplicantPriorityPacket] NetId {NetId} has no ChoreConsumer.");
				return;
			}

			// Find the ChoreGroup
			ChoreGroup targetGroup = null;
			foreach (var group in Db.Get().ChoreGroups.resources)
			{
				if (group.Id == ChoreGroupId)
				{
					targetGroup = group;
					break;
				}
			}

			if (targetGroup != null)
			{
				IsApplying = true;
				try
				{
					consumer.SetPersonalPriority(targetGroup, Priority);
					ManagementMenu.Instance.jobsScreen.MarkRowsDirty();
					DebugConsole.Log($"[DuplicantPriorityPacket] Applied {ChoreGroupId} = {Priority} to {identity.name}");
				}
				finally
				{
					IsApplying = false;
				}
			}
			else
			{
				DebugConsole.LogWarning($"[DuplicantPriorityPacket] ChoreGroup {ChoreGroupId} not found.");
			}
		}

		/// <summary>
		/// Searches all live duplicants for one with the matching NetId component,
		/// and if found, forces registration with the NetworkIdentityRegistry.
		/// </summary>
		private static NetworkIdentity TryFindAndRegisterIdentity(int netId)
		{
			using var _ = Profiler.Scope();

			// Search all live duplicants
			foreach (var minionIdentity in global::Components.LiveMinionIdentities.Items)
			{
				if (minionIdentity == null) continue;

				var identity = minionIdentity.GetComponent<NetworkIdentity>();
				if (identity != null && identity.NetId == netId)
				{
					// Found it! Force register using RegisterOverride to bypass any checks
					NetworkIdentityRegistry.RegisterOverride(identity, netId);
					DebugConsole.Log($"[DuplicantPriorityPacket] Force-registered NetId {netId} for {minionIdentity.name}");
					return identity;
				}
			}

			return null;
		}

		public static bool IsApplying = false;
	}
}
