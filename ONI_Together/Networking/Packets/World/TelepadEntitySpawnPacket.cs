using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Social;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using static STRINGS.UI.CLUSTERMAP;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	/// <summary>
	/// Packet to spawn entities (duplicants or items) on clients with matching NetIds.
	/// Sent from host when an entity is spawned (e.g., from Telepad).
	/// </summary>
	public class TelepadEntitySpawnPacket : IPacket, IHostOnlyPacket
	{
		private const float MaxCoordinate = 1_000_000f;

		public ImmigrantOptionEntry EntityData;
		public int NetId;
		public ulong Revision;

		// Position
		public Vector3 Pos;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid telepad entity spawn state");

			writer.Write(NetId);
			writer.Write(Revision);
			EntityData.Serialize(writer);
			writer.Write(Pos);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			Revision = reader.ReadUInt64();
			EntityData = ImmigrantOptionEntry.Deserialize(reader);
			Pos = reader.ReadVector3();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid telepad entity spawn state");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"[EntitySpawnPacket] OnDispatched called - NetId {NetId}, IsDuplicant={EntityData.IsDuplicant}, IsHost={MultiplayerSession.IsHost}");

			bool exists = NetworkIdentityRegistry.TryGet(NetId, out NetworkIdentity existing);
			ulong currentRevision = NetworkIdentityRegistry.GetLastLifecycleRevision(NetId);
			if (!ShouldMaterialize(
				    MultiplayerSession.IsHost,
				    PacketHandler.CurrentContext.SenderIsHost,
				    currentRevision,
				    NetworkIdentityRegistry.IsLifecycleTombstoned(NetId),
				    Revision,
				    exists))
			{
				return;
			}

			DebugConsole.Log($"[EntitySpawnPacket] Client: Received spawn for NetId {NetId}, IsDuplicant={EntityData.IsDuplicant}, ItemID: {EntityData.GetId()}");

			GameObject entity = null;
			try
			{
				if (exists)
				{
					NetworkIdentityRegistry.Unregister(existing, NetId);
					Util.KDestroyGameObject(existing.gameObject);
				}
				var deliverable = EntityData.ToGameDeliverable();
				if (deliverable == null)
					return;
				Vector3 position = Pos;
				if (deliverable is not MinionStartingStats)
				{
					///move care packages a bit to the left to be centered
					position.x -= 0.5f;
				}
				entity = deliverable.Deliver(position);
				if (entity == null)
					return;

				///duplicants from the printer are assigned an extra skill point, this is skipped over with a direct delivery
				if (entity.TryGetComponent<MinionResume>(out var res))
					res.ForceAddSkillPoint();

				if (!NetworkIdentityRegistry.TryBindAuthoritativeLifecycle(entity, NetId, Revision))
				{
					Util.KDestroyGameObject(entity);
					entity = null;
				}
			}
			catch (System.Exception ex)
			{
				if (entity != null && !entity.IsNullOrDestroyed())
					Util.KDestroyGameObject(entity);
				DebugConsole.LogError($"[EntitySpawnPacket] Failed to spawn: {ex}");
			}
		}

		internal bool IsWireValid()
		{
			return NetId != 0 && Revision != 0 && IsFiniteCoordinate(Pos.x)
			       && IsFiniteCoordinate(Pos.y) && IsFiniteCoordinate(Pos.z)
			       && IsValidEntityData(EntityData);
		}

		internal static bool ShouldMaterialize(
			bool localIsHost,
			bool senderIsHost,
			ulong currentRevision,
			bool tombstoned,
			ulong incomingRevision,
			bool entityExists)
		{
			if (localIsHost || !senderIsHost || incomingRevision == 0 || currentRevision > incomingRevision)
				return false;
			if (currentRevision == incomingRevision)
				return !tombstoned && !entityExists;
			return true;
		}

		private static bool IsValidEntityData(ImmigrantOptionEntry data)
		{
			if (!data.IsValid)
				return false;
			if (data.IsDuplicant)
			{
				return !string.IsNullOrEmpty(data.Name) && !string.IsNullOrEmpty(data.PersonalityId)
				       && data.TraitIds != null && data.SkillAptitudes != null
				       && data.StartingLevels != null;
			}
			return data.EntryType == 1 && !string.IsNullOrEmpty(data.CarePackageId)
			       && IsFinite(data.Quantity) && data.Quantity > 0f;
		}

		private static bool IsFiniteCoordinate(float value)
			=> IsFinite(value) && System.Math.Abs(value) <= MaxCoordinate;

		private static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
