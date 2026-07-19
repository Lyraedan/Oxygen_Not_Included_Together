using System;
using System.Collections.Generic;
using ONI_Together.Networking.Components;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Build
{
	internal static partial class BuildAuthority
	{
		internal static bool TryExecuteHost(
			BuildPacket request,
			bool instantBuild,
			Action<BuildStatePacket> publish,
			out string error)
		{
			if (!TryResolve(request, out BuildingDef def, out List<Tag> materials,
				    out PrioritySetting priority, out error))
				return false;
			return TryExecute(request, def, materials, priority, instantBuild, publish, out error);
		}

		internal static bool TryApply(BuildStatePacket state)
		{
			BuildPacket request = state.ToRequest();
			if (!TryResolve(request, out BuildingDef def, out List<Tag> materials,
				    out PrioritySetting priority, out _))
				return false;
			if (TryFindMatchingOutcome(def, state.Cell, state.Outcome, out GameObject existing))
			{
				SetPriority(existing, priority);
				return TryApplyIdentity(existing, state.NetId, state.LifecycleRevision);
			}
			bool identityApplied = false;
			bool placed = TryPlaceExact(request, def, materials, priority, state.Outcome,
				gameObject => identityApplied = TryApplyIdentity(
					gameObject, state.NetId, state.LifecycleRevision), out _);
			return placed && identityApplied;
		}

		internal static bool TryApplyIdentity(
			GameObject gameObject, int netId, ulong lifecycleRevision)
		{
			return netId == 0 && lifecycleRevision == 0
			       || NetworkIdentityRegistry.TryBindAuthoritativeLifecycle(
				       gameObject, netId, lifecycleRevision);
		}

		private static bool TryExecute(
			BuildPacket request,
			BuildingDef def,
			List<Tag> materials,
			PrioritySetting priority,
			bool instantBuild,
			Action<BuildStatePacket> publish,
			out string error)
		{
			BuildPlacementKind normal = instantBuild ? BuildPlacementKind.Completed : BuildPlacementKind.Queued;
			if (TryPlaceExact(request, def, materials, priority, normal,
				    gameObject => PublishState(
					    request, normal, instantBuild, gameObject, publish), out error))
				return true;

			BuildPlacementKind replacement = instantBuild
				? BuildPlacementKind.CompletedReplacement
				: BuildPlacementKind.QueuedReplacement;
			return TryPlaceExact(request, def, materials, priority, replacement,
				gameObject => PublishState(
					request, replacement, instantBuild, gameObject, publish), out error);
		}

		private static void PublishState(
			BuildPacket request,
			BuildPlacementKind kind,
			bool instantBuild,
			GameObject gameObject,
			Action<BuildStatePacket> publish)
		{
			BuildStatePacket state = BuildStatePacket.FromRequest(request, kind, instantBuild);
			state.CaptureIdentity(gameObject);
			publish(state);
		}

		private static bool TryPlaceExact(
			BuildPacket request,
			BuildingDef def,
			List<Tag> materials,
			PrioritySetting priority,
			BuildPlacementKind kind,
			Action<GameObject> completed,
			out string error)
		{
			error = "build location rejected";
			bool replacement = kind is BuildPlacementKind.QueuedReplacement or BuildPlacementKind.CompletedReplacement;
			bool complete = kind is BuildPlacementKind.Completed or BuildPlacementKind.CompletedReplacement;
			return complete
				? TryPlaceCompleted(request, def, materials, priority, replacement, completed)
				: TryPlaceQueued(request, def, materials, priority, replacement, completed);
		}

		private static bool TryPlaceQueued(
			BuildPacket request,
			BuildingDef def,
			List<Tag> materials,
			PrioritySetting priority,
			bool replacement,
			Action<GameObject> completed)
		{
			Vector3 position = Grid.CellToPosCBC(request.Cell, Grid.SceneLayer.Building);
			if (replacement && !TryGetReplacement(
				    def, request.Cell, request.Orientation, materials, out _))
				return false;
			GameObject placed = replacement
				? def.TryReplaceTile(null, position, request.Orientation, materials, NormalizeFacade(request.FacadeID))
				: def.TryPlace(null, position, request.Orientation, materials, NormalizeFacade(request.FacadeID));
			if (placed == null)
				return false;
			if (replacement)
				Grid.Objects[request.Cell, (int)def.ReplacementLayer] = placed;
			SetPriority(placed, priority);
			completed(placed);
			return true;
		}

		private static bool TryPlaceCompleted(
			BuildPacket request,
			BuildingDef def,
			List<Tag> materials,
			PrioritySetting priority,
			bool replacement,
			Action<GameObject> completed)
		{
			Vector3 position = Grid.CellToPosCBC(request.Cell, Grid.SceneLayer.Building);
			GameObject candidate = null;
			if (replacement && !TryGetReplacement(
				    def, request.Cell, request.Orientation, materials, out candidate))
				return false;
			if (!def.IsValidBuildLocation(null, position, request.Orientation, replacement) ||
			    !def.IsValidPlaceLocation(null, position, request.Orientation, replacement, out _))
				return false;
			if (!replacement)
			{
				PrepareInstantBuild(def, request.Cell, request.Orientation);
				GameObject built = Complete(def, request, materials);
				SetPriority(built, priority);
				completed(built);
				return built != null;
			}

			void FinishReplacement()
			{
				if (candidate != null)
					UnityEngine.Object.Destroy(candidate);
				GameObject built = Complete(def, request, materials);
				SetPriority(built, priority);
				completed(built);
			}

			SimCellOccupier occupier = candidate.GetComponent<SimCellOccupier>();
			if (occupier != null)
				occupier.DestroySelf(FinishReplacement);
			else
				FinishReplacement();
			return true;
		}

		private static GameObject Complete(BuildingDef def, BuildPacket request, IList<Tag> materials)
		{
			float temperature = Mathf.Min(
				def.Temperature,
				ElementLoader.GetMinMeltingPointAmongElements(materials) - 10f);
			return def.Build(
				request.Cell,
				request.Orientation,
				null,
				materials,
				temperature,
				NormalizeFacade(request.FacadeID),
				playsound: false,
				GameClock.Instance.GetTime());
		}

		private static void PrepareInstantBuild(BuildingDef def, int cell, Orientation orientation)
		{
			if (def.ObjectLayer == global::ObjectLayer.Building)
			{
				def.RunOnArea(cell, orientation, offset =>
				{
					if (Uprootable.CanUproot(Grid.Objects[offset, (int)def.ObjectLayer], out Uprootable uprootable))
						uprootable.CompleteWork(null);
				});
			}
			else if (def.ObjectLayer == global::ObjectLayer.Backwall)
			{
				def.RunOnArea(cell, orientation, offset =>
				{
					if (BackwallManager.HasBackwall(offset))
						SimMessages.Dig(offset, -1, skipEvent: true, backwall: true);
				});
			}
		}

		private static bool TryGetReplacement(
			BuildingDef def,
			int cell,
			Orientation orientation,
			IReadOnlyList<Tag> materials,
			out GameObject candidate)
		{
			candidate = null;
			if (def.ReplacementLayer == global::ObjectLayer.NumLayers || materials.Count == 0)
				return false;
			candidate = def.GetReplacementCandidate(cell);
			bool occupied = false;
			def.RunOnArea(cell, orientation, offset => occupied |= def.IsReplacementLayerOccupied(offset));
			BuildingComplete complete = candidate?.GetComponent<BuildingComplete>();
			if (candidate == null || occupied || complete == null || !complete.Def.Replaceable || !def.CanReplace(candidate))
				return false;
			Tag existingMaterial = candidate.GetComponent<PrimaryElement>()?.Element?.tag ?? Tag.Invalid;
			if (existingMaterial.GetHash() == (int)SimHashes.StableSnow)
				existingMaterial = SimHashes.Snow.CreateTag();
			return complete.Def != def || materials[0] != existingMaterial;
		}

		private static bool Fail(string message, ref string error)
		{
			error = message;
			return false;
		}
	}
}
