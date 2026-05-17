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

namespace ONI_Together.Networking.Packets.World
{
	/// <summary>
	/// Packet to spawn entities (duplicants or items) on clients with matching NetIds.
	/// Sent from host when an entity is spawned (e.g., from Telepad).
	/// </summary>
	public class TelepadEntitySpawnPacket : IPacket
	{
		public ImmigrantOptionEntry EntityData;
		public int NetId;

		// Position
		public Vector3 Pos;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			EntityData.Serialize(writer);
			writer.Write(Pos);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			EntityData = ImmigrantOptionEntry.Deserialize(reader);
			Pos = reader.ReadVector3();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"[EntitySpawnPacket] OnDispatched called - NetId {NetId}, IsDuplicant={EntityData.IsDuplicant}, IsHost={MultiplayerSession.IsHost}");

			// Only clients should process this
			if (MultiplayerSession.IsHost) return;

			DebugConsole.Log($"[EntitySpawnPacket] Client: Received spawn for NetId {NetId}, IsDuplicant={EntityData.IsDuplicant}, ItemID: {EntityData.GetId()}");

			try
			{
				var deliverable = EntityData.ToGameDeliverable();
				if (deliverable is not MinionStartingStats)
				{
					///move care packages a bit to the left to be centered
					Pos.x -= 0.5f;
				}
				GameObject entity = deliverable.Deliver(Pos);

				///duplicants from the printer are assigned an extra skill point, this is skipped over with a direct delivery
				if (entity.TryGetComponent<MinionResume>(out var res))
					res.ForceAddSkillPoint();

				NetworkIdentity identity = entity.AddOrGet<NetworkIdentity>();
				identity.OverrideNetId(NetId);
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[EntitySpawnPacket] Failed to spawn: {ex}");
			}
		}
	}
}
