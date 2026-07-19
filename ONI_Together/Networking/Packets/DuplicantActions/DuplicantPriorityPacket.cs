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
		internal const int MaxChoreGroupIdChars = 128;
		public int NetId;
		public string ChoreGroupId;
		public int Priority;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (!IsValidRequest(NetId, ChoreGroupId, Priority))
				throw new InvalidDataException("Invalid duplicant priority request");

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
			if (!IsValidRequest(NetId, ChoreGroupId, Priority))
				throw new InvalidDataException("Invalid duplicant priority request");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
			{
				if (PacketHandler.CurrentContext.SenderIsHost || !Apply())
					return;
				PacketSender.SendToAllClients(this);
			}
			else
			{
				if (!PacketHandler.CurrentContext.SenderIsHost)
					return;
				Apply();
			}
		}

		private bool Apply()
		{
			using var _ = Profiler.Scope();

			if (!NetworkIdentityRegistry.TryGet(NetId, out var identity) || identity == null)
			{
				DebugConsole.LogWarning($"[DuplicantPriorityPacket] Signed NetId {NetId} is not registered.");
				return false;
			}

			var consumer = identity.GetComponent<ChoreConsumer>();
			if (consumer == null)
			{
				DebugConsole.LogWarning($"[DuplicantPriorityPacket] NetId {NetId} has no ChoreConsumer.");
				return false;
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
				if (consumer.IsChoreGroupDisabled(targetGroup))
					return false;
				IsApplying = true;
				try
				{
					consumer.SetPersonalPriority(targetGroup, Priority);
					ManagementMenu.Instance?.jobsScreen?.MarkRowsDirty();
					DebugConsole.Log($"[DuplicantPriorityPacket] Applied {ChoreGroupId} = {Priority} to {identity.name}");
					return true;
				}
				finally
				{
					IsApplying = false;
				}
			}
			else
			{
				DebugConsole.LogWarning($"[DuplicantPriorityPacket] ChoreGroup {ChoreGroupId} not found.");
				return false;
			}
		}

		internal static bool IsValidRequest(int netId, string choreGroupId, int priority)
			=> netId != 0
			   && !string.IsNullOrEmpty(choreGroupId)
			   && choreGroupId.Length <= MaxChoreGroupIdChars
			   && priority >= 0 && priority <= 5;

		public static bool IsApplying = false;

		internal static void ResetSessionState() => IsApplying = false;
	}
}
