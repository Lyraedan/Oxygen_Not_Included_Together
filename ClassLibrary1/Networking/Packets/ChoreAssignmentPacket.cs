using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.Architecture;
using ONI_MP.Patches.ChoresPatches;
using System.IO;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Misc;

namespace ONI_MP.Networking.Packets
{
public class ChoreAssignmentPacket : IPacket
{
	public int NetId;
	public string ChoreTypeId;

	public Vector3 TargetPosition;
	public int TargetCell = -1;
	public string TargetPrefabId; // optional

	public void Serialize(BinaryWriter writer)
	{
		using var _ = Profiler.Scope();

		writer.Write(NetId);
		writer.Write(ChoreTypeId ?? string.Empty);
		writer.Write(TargetPosition.x);
		writer.Write(TargetPosition.y);
		writer.Write(TargetPosition.z);
		writer.Write(TargetCell);
		writer.Write(TargetPrefabId ?? string.Empty);
	}

	public void Deserialize(BinaryReader reader)
	{
		using var _ = Profiler.Scope();

		NetId = reader.ReadInt32();
		ChoreTypeId = reader.ReadString();
		TargetPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		TargetCell = reader.ReadInt32();
		TargetPrefabId = reader.ReadString();
	}

	public void OnDispatched()
	{
		using var _ = Profiler.Scope();

		// Disabled for now
		return;

		if (!NetworkIdentityRegistry.TryGet(NetId, out var entity))
		{
			DebugConsole.LogWarning($"[ChoreAssignment] Could not find entity with NetId {NetId}");
			return;
		}

		var dupeGO = entity.gameObject;
		var consumer = dupeGO.GetComponent<ChoreConsumer>();
		var type = Db.Get().ChoreTypes.Get(ChoreTypeId);

		if (consumer == null || type == null)
		{
			DebugConsole.LogWarning($"[ChoreAssignment] Missing consumer or chore type: {ChoreTypeId}");
			return;
		}

		// Create the context to pass for precondition-aware chores
		var context = new Chore.Precondition.Context
		{
			consumerState = consumer.GetComponent<ChoreConsumerState>(),
			choreTypeForPermission = type
		};

		var chore = ChoreFactory.Create(ChoreTypeId, context, dupeGO, TargetPosition, TargetCell, TargetPrefabId);

		if (chore != null)
		{
			chore.AssignChoreToDuplicant(dupeGO);
		}
		else
		{
			DebugConsole.LogWarning($"[ChoreAssignment] Could not create chore: {ChoreTypeId} for {dupeGO.name}");
		}
	}


}

}