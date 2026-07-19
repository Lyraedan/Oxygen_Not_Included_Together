#if DEBUG
using System.Collections.Generic;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using UnityEngine;

namespace ONI_Together.DebugTools
{
	internal static class SoakWorldMembershipDiagnostics
	{
		private const int MaxLoggedDrifts = 8;

		internal static void LogDrift(
			int sampleId, IEnumerable<SoakHashDomainKeyframePacket> packets,
			IReadOnlyList<SoakWorldMembershipState> actualStates)
		{
			Dictionary<int, SoakWorldMembershipState> actualByNetId = actualStates
				.ToDictionary(state => state.NetId);
			int driftCount = 0;
			foreach (SoakHashDomainKeyframePacket packet in packets.OrderBy(value => value.NetId))
			{
				if (!TryDescribeDrift(packet, actualByNetId, out string description))
					continue;
				driftCount++;
				if (driftCount <= MaxLoggedDrifts)
					DebugConsole.LogWarning(
						$"[SoakKeyframe][WORLD_DRIFT] sample={sampleId} {description}");
			}
			DebugConsole.Log($"[SoakKeyframe][WORLD_DRIFT_SUMMARY] sample={sampleId} " +
			                 $"count={driftCount}");
		}

		private static bool TryDescribeDrift(
			SoakHashDomainKeyframePacket expected,
			IReadOnlyDictionary<int, SoakWorldMembershipState> actualByNetId,
			out string description)
		{
			description = string.Empty;
			if (!actualByNetId.TryGetValue(expected.NetId, out SoakWorldMembershipState actual))
			{
				description = $"netId={expected.NetId} fields=identityMissing";
				return true;
			}

			SoakWorldMembershipState target = CaptureExpected(expected);
			string fields = DifferentFields(target, actual);
			if (fields == "none")
				return false;
			description = $"netId={expected.NetId} fields={fields} " +
			              $"hasHandler={actual.HasPositionHandler} " +
			              $"expectedHasHandler={expected.HasPosition} " +
			              $"keyframeSequence={expected.PositionSequence}";
			return true;
		}

		internal static string DifferentFields(
			SoakWorldMembershipState expected, SoakWorldMembershipState actual)
		{
			var fields = new List<string>();
			if (expected.NetId != actual.NetId) fields.Add("netId");
			if (expected.WorldId != actual.WorldId) fields.Add("worldId");
			if (expected.Cell != actual.Cell) fields.Add("cell");
			if (expected.PositionX != actual.PositionX) fields.Add("positionX");
			if (expected.PositionY != actual.PositionY) fields.Add("positionY");
			if (expected.PositionZ != actual.PositionZ) fields.Add("positionZ");
			if (expected.HasPositionHandler != actual.HasPositionHandler) fields.Add("handler");
			if (expected.FlipX != actual.FlipX) fields.Add("flipX");
			if (expected.FlipY != actual.FlipY) fields.Add("flipY");
			if (expected.NavType != actual.NavType) fields.Add("navType");
			return fields.Count == 0 ? "none" : string.Join(",", fields);
		}

		private static SoakWorldMembershipState CaptureExpected(
			SoakHashDomainKeyframePacket packet)
		{
			return CreateState(
				packet.NetId, packet.WorldId, packet.Position, packet.HasPosition,
				packet.FlipX, packet.FlipY, packet.NavType);
		}

		private static SoakWorldMembershipState CreateState(
			int netId, int worldId, Vector3 position, bool hasHandler,
			bool flipX, bool flipY, NavType navType)
		{
			return new SoakWorldMembershipState
			{
				NetId = netId,
				WorldId = worldId,
				Cell = Grid.PosToCell(position),
				PositionX = SoakStateHash.NormalizeFloatBits(position.x),
				PositionY = SoakStateHash.NormalizeFloatBits(position.y),
				PositionZ = SoakStateHash.NormalizeFloatBits(position.z),
				HasPositionHandler = hasHandler,
				FlipX = flipX,
				FlipY = flipY,
				NavType = navType,
			};
		}

	}
}
#endif
