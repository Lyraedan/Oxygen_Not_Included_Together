using System;
using System.Collections.Generic;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public partial class SpawnPrefabPacket
	{
		private const int MaxClientReplayExpectations = 4096;
		private const float ClientReplayLifetimeSeconds = 10f;
		private static readonly Dictionary<int, (SpawnPrefabPacket Descriptor, float CreatedAt)>
			ClientReplayExpectations = new();

		private void RecordClientReplayExpectation()
		{
			if (!MultiplayerSession.IsClient || !HasElementData
			    || !NetworkIdentityRegistry.Exists(NetId))
				return;
			float now = Time.unscaledTime;
			PruneClientReplayExpectations(now);
			if (ClientReplayExpectations.Count >= MaxClientReplayExpectations)
				RemoveOldestClientReplayExpectation();
			ClientReplayExpectations[NetId] = (CopyReplayDescriptor(this), now);
		}

		internal static bool TryConsumeClientReplay(
			GameObject spawned, out GameObject authoritative)
		{
			authoritative = null;
			if (!CanConsumeReplay(
				    MultiplayerSession.IsClient,
				    MultiplayerSession.InSession,
				    PacketHandler.CurrentContext.SenderIsHost)
			    || !TryDescribeElementSpawn(spawned, out SpawnPrefabPacket actual))
				return false;
			PruneClientReplayExpectations(Time.unscaledTime);
			int netId = FindMatchingClientReplay(actual);
			if (netId == 0 || !NetworkIdentityRegistry.TryGet(
				    netId, out NetworkIdentity identity))
				return false;
			ClientReplayExpectations.Remove(netId);
			authoritative = identity.gameObject;
			if (ReferenceEquals(spawned, authoritative))
				return true;
			NetworkIdentity speculative = spawned.GetComponent<NetworkIdentity>();
			if (!ReferenceEquals(speculative, null))
			{
				NetworkIdentityRegistry.Unregister(speculative, speculative.NetId);
				NetworkIdentityRegistry.UntrackUnassigned(speculative);
			}
			Util.KDestroyGameObject(spawned);
			return true;
		}

		internal static bool CanConsumeReplay(
			bool isClient, bool inSession, bool senderIsHost)
			=> isClient && inSession && !senderIsHost;

		internal static bool ReplayMatches(
			SpawnPrefabPacket expected, SpawnPrefabPacket actual)
		{
			return expected != null && actual != null && expected.HasElementData
			       && actual.HasElementData && expected.Hash == actual.Hash
			       && expected.WorldId == actual.WorldId
			       && expected.Position.Equals(actual.Position)
			       && expected.Mass.Equals(actual.Mass)
			       && expected.Temperature.Equals(actual.Temperature)
			       && expected.DiseaseCount == actual.DiseaseCount
			       && (expected.DiseaseCount == 0
			           || expected.DiseaseIndex == actual.DiseaseIndex);
		}

		private static int FindMatchingClientReplay(SpawnPrefabPacket actual)
			=> FindUniqueReplayMatch(actual, ClientReplayExpectations);

		internal static int FindUniqueReplayMatch(
			SpawnPrefabPacket actual,
			IEnumerable<KeyValuePair<int, (SpawnPrefabPacket Descriptor, float CreatedAt)>> candidates)
		{
			int selected = 0;
			foreach (var entry in candidates)
			{
				if (!ReplayMatches(entry.Value.Descriptor, actual))
					continue;
				if (selected != 0)
					return 0;
				selected = entry.Key;
			}
			return selected;
		}

		private static bool TryDescribeElementSpawn(
			GameObject gameObject, out SpawnPrefabPacket descriptor)
		{
			descriptor = null;
			if (gameObject == null || gameObject.IsNullOrDestroyed()
			    || !gameObject.TryGetComponent<PrimaryElement>(out var primary))
				return false;
			descriptor = new SpawnPrefabPacket
			{
				Hash = gameObject.PrefabID().GetHashCode(),
				Position = gameObject.transform.position,
				WorldId = gameObject.GetMyWorld()?.id ?? -1,
				HasElementData = true,
				Mass = primary.Mass,
				Temperature = primary.Temperature,
				DiseaseIndex = primary.DiseaseIdx,
				DiseaseCount = primary.DiseaseCount,
			};
			return true;
		}

		private static SpawnPrefabPacket CopyReplayDescriptor(SpawnPrefabPacket source)
		{
			return new SpawnPrefabPacket
			{
				Hash = source.Hash,
				Position = source.Position,
				WorldId = source.WorldId,
				HasElementData = true,
				Mass = source.Mass,
				Temperature = source.Temperature,
				DiseaseIndex = source.DiseaseIndex,
				DiseaseCount = source.DiseaseCount,
			};
		}

		private static void PruneClientReplayExpectations(float now)
		{
			var snapshot = new List<KeyValuePair<int, (SpawnPrefabPacket Descriptor, float CreatedAt)>>(
				ClientReplayExpectations);
			foreach (var entry in snapshot)
				if (now - entry.Value.CreatedAt > ClientReplayLifetimeSeconds)
					ClientReplayExpectations.Remove(entry.Key);
		}

		private static void RemoveOldestClientReplayExpectation()
		{
			int oldestNetId = 0;
			float oldestTime = float.MaxValue;
			foreach (var entry in ClientReplayExpectations)
			{
				if (entry.Value.CreatedAt >= oldestTime)
					continue;
				oldestNetId = entry.Key;
				oldestTime = entry.Value.CreatedAt;
			}
			if (oldestNetId != 0)
				ClientReplayExpectations.Remove(oldestNetId);
		}

		private static void ClearClientReplayExpectations()
			=> ClientReplayExpectations.Clear();
	}
}
