#if DEBUG
using System.Collections.Generic;
using ONI_Together.Networking.Components;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public partial class SpawnPrefabPacket
	{
		private static readonly Dictionary<int, string> SnapshotDiagnostics = new();

		internal static void ResetSnapshotDiagnostics() => SnapshotDiagnostics.Clear();

		internal static string GetSnapshotDiagnostic(int netId)
			=> SnapshotDiagnostics.TryGetValue(netId, out string diagnostic)
				? diagnostic
				: "none";

		internal static void RecordCleanupDiagnostic(
			NetworkIdentity identity, string stackTrace)
		{
			if (identity == null || !SnapshotDiagnostics.ContainsKey(identity.NetId))
				return;
			string existing = SnapshotDiagnostics[identity.NetId];
			SnapshotDiagnostics[identity.NetId] = existing +
				$"; cleanup={DescribeObject(identity.gameObject)}; stack={stackTrace}";
		}

		private void RecordSnapshotDiagnostic(string outcome, GameObject gameObject)
		{
			SnapshotDiagnostics[NetId] = $"{outcome}; expected={DescribeExpected()}; " +
			                             $"object={DescribeObject(gameObject)}";
		}

		private string DescribeExpected()
		{
			string element = HasElementData
				? $"mass:{Mass:R}, temp:{Temperature:R}, disease:{DiseaseIndex}/{DiseaseCount}"
				: "none";
			return $"hash:{Hash}, position:{DescribeVector(Position)}, world:{WorldId}, " +
			       $"active:{IsActive}, element:{element}";
		}

		private static string DescribeObject(GameObject gameObject)
		{
			if (gameObject == null || gameObject.IsNullOrDestroyed())
				return "destroyed";
			Transform parent = gameObject.transform.parent;
			NetworkIdentity parentIdentity = parent?.GetComponent<NetworkIdentity>();
			Storage storage = gameObject.GetComponent<Pickupable>()?.storage;
			NetworkIdentity storageIdentity = storage?.GetComponent<NetworkIdentity>();
			string element = DescribePrimaryElement(gameObject);
			return $"name:{gameObject.name}, prefab:{gameObject.PrefabID()}, " +
			       $"position:{DescribeVector(gameObject.transform.position)}, " +
			       $"world:{gameObject.GetMyWorldId()}, active:{gameObject.activeSelf}, " +
			       $"parent:{parent?.name ?? "none"}, parentNetId:{parentIdentity?.NetId ?? 0}, " +
			       $"storage:{storage?.name ?? "none"}, storageNetId:{storageIdentity?.NetId ?? 0}, " +
			       $"element:{element}";
		}

		private static string DescribePrimaryElement(GameObject gameObject)
		{
			if (!gameObject.TryGetComponent<PrimaryElement>(out var primary))
				return "none";
			return $"mass:{primary.Mass:R}, temp:{primary.Temperature:R}, " +
			       $"disease:{primary.DiseaseIdx}/{primary.DiseaseCount}";
		}

		private static string DescribeVector(Vector3 value)
			=> $"({value.x:R},{value.y:R},{value.z:R})";
	}
}
#endif
