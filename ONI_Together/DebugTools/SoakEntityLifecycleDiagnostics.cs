#if DEBUG
using System.Collections.Generic;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools
{
	internal static class SoakEntityLifecycleDiagnostics
	{
		private const int MaxLoggedDrifts = 8;

		internal static void LogDrift(
			int sampleId, IEnumerable<SoakHashDomainKeyframePacket> packets,
			IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> baseline,
			IReadOnlyList<SoakEntityState> actualStates)
		{
			Dictionary<int, SpawnPrefabPacket> live = packets.ToDictionary(
				packet => packet.NetId, packet => packet.LifecycleSnapshot);
			Dictionary<int, SoakEntityState> actual = actualStates.ToDictionary(
				state => state.NetId);
			int driftCount = 0;
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry
			         in baseline.OrderBy(value => value.NetId))
			{
				SoakEntityState expected = CreateExpected(entry, live);
				string fields = actual.TryGetValue(entry.NetId, out SoakEntityState observed)
					? DifferentFields(expected, observed)
					: "recordMissing";
				if (fields == "none")
					continue;
				driftCount++;
				if (driftCount <= MaxLoggedDrifts)
				{
					live.TryGetValue(entry.NetId, out SpawnPrefabPacket descriptor);
					DebugConsole.LogWarning(
						$"[SoakKeyframe][ENTITY_DRIFT] sample={sampleId} " +
						$"netId={entry.NetId} fields={fields} " +
						$"hash={descriptor?.Hash ?? 0} element={descriptor?.HasElementData ?? false} " +
						$"bindExisting={descriptor?.BindExistingOnly ?? false} " +
						$"apply={SpawnPrefabPacket.GetSnapshotDiagnostic(entry.NetId)}");
				}
			}
			DebugConsole.Log($"[SoakKeyframe][ENTITY_DRIFT_SUMMARY] sample={sampleId} " +
			                 $"count={driftCount}");
		}

		private static SoakEntityState CreateExpected(
			NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry,
			IReadOnlyDictionary<int, SpawnPrefabPacket> live)
		{
			if (entry.Tombstoned)
				return new SoakEntityState
				{
					NetId = entry.NetId,
					Revision = entry.Revision,
					Tombstoned = true,
				};
			SpawnPrefabPacket descriptor = live[entry.NetId];
			bool duplicant = descriptor.HasDuplicantState;
			bool dead = duplicant && descriptor.IsDuplicantDead;
			return new SoakEntityState
			{
				NetId = entry.NetId,
				PrefabHash = descriptor.Hash,
				Active = descriptor.IsActive,
				Revision = entry.Revision,
				IsDuplicant = duplicant,
				IsDead = dead,
				HasDeadTag = dead,
				MonitorIsDead = dead,
				IsCorpse = dead,
				IsInLiveRoster = duplicant && !dead,
				IsInLiveRosterByModel = duplicant && !dead,
				DeathId = dead ? descriptor.DuplicantDeathId : string.Empty,
			};
		}

		internal static string DifferentFields(
			SoakEntityState expected, SoakEntityState actual)
		{
			var fields = new List<string>();
			if (expected.NetId != actual.NetId) fields.Add("netId");
			if (expected.PrefabHash != actual.PrefabHash) fields.Add("prefabHash");
			if (expected.Active != actual.Active) fields.Add("active");
			if (expected.Revision != actual.Revision) fields.Add("revision");
			if (expected.Tombstoned != actual.Tombstoned) fields.Add("tombstoned");
			if (expected.IsDuplicant != actual.IsDuplicant) fields.Add("duplicant");
			if (expected.IsDead != actual.IsDead) fields.Add("dead");
			if (expected.HasDeadTag != actual.HasDeadTag) fields.Add("deadTag");
			if (expected.MonitorIsDead != actual.MonitorIsDead) fields.Add("deathMonitor");
			if (expected.IsCorpse != actual.IsCorpse) fields.Add("corpse");
			if (expected.IsInLiveRoster != actual.IsInLiveRoster) fields.Add("liveRoster");
			if (expected.IsInLiveRosterByModel != actual.IsInLiveRosterByModel)
				fields.Add("modelRoster");
			if ((expected.DeathId ?? string.Empty) != (actual.DeathId ?? string.Empty))
				fields.Add("deathId");
			return fields.Count == 0 ? "none" : string.Join(",", fields);
		}
	}
}
#endif
