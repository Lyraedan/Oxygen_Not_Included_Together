using HarmonyLib;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(AirConditioner), nameof(AirConditioner.UpdateState), new System.Type[] { typeof(float) })]
	internal static class AirConditionerPatch
	{
		// An Aquatuner or Air Conditioner whose sim handle does not exist yet.
		//
		// AirConditioner.OnSpawn is the only place that assigns structureTemperature, but
		// UpdateState can be reached before it runs. Operational.SetFlag ends with
		//
		//     if (value != IsOperational) UpdateOperational();
		//
		// and UpdateOperational triggers OnOperationalChanged, which calls UpdateState. So
		// EnergyConsumer.OnPrefabInit setting IsPowered is enough to drive the whole chain
		// during prefab init, one stage before the handle is registered.
		//
		// A handle that has never been assigned has _index 0, UnpackHandle turns that into
		// index -1, and the List lookup inside ProduceEnergy throws:
		//
		//     ArgumentOutOfRangeException: Index was out of range.
		//     Must be non-negative and less than the size of the collection.
		//       StructureTemperatureComponents.ProduceEnergy
		//       AirConditioner.UpdateState
		//       AirConditioner.OnOperationalChanged
		//       Operational.UpdateOperational
		//       Operational.SetFlag
		//       EnergyConsumer.set_IsPowered
		//       EnergyConsumer.OnPrefabInit
		//
		// Observed on a client during world load, three times in one session. It is fatal
		// rather than noisy: the game raises its error screen and the session ends there.
		//
		// Skipped rather than caught, and not gated to clients. The state this call would
		// have produced is recomputed by the next UpdateState against a spawned building,
		// and on a host the same invalid handle would throw the same way - there is nothing
		// useful this call can do before OnSpawn has run.
		public static bool Prefix(AirConditioner __instance)
		{
			using var _ = Profiler.Scope();

			// structureTemperature is a struct (HandleVector<int>.Handle), so no ?. on it.
			return __instance != null && __instance.structureTemperature.IsValid();
		}
	}
}
