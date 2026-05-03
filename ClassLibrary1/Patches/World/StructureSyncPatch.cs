using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using Shared.Profiling;

namespace ONI_MP.Patches.World
{
	[HarmonyPatch(typeof(Battery), "OnSpawn")]
	public static class BatterySpawnPatch
	{
		public static void Postfix(Battery __instance)
		{
			using var _ = Profiler.Scope();

			StructureStateSyncer syncer = __instance.gameObject.AddOrGet<StructureStateSyncer>();
			syncer.InitalizeAsStructure(StructureStateSyncer.StructureType.BATTERY);
		}
	}

	[HarmonyPatch(typeof(Generator), "OnSpawn")]
	public static class GeneratorSpawnPatch
	{
		public static void Postfix(Generator __instance)
		{
			using var _ = Profiler.Scope();

            StructureStateSyncer syncer = __instance.gameObject.AddOrGet<StructureStateSyncer>();
            syncer.InitalizeAsStructure(StructureStateSyncer.StructureType.GENERATOR);
        }
    }
}
