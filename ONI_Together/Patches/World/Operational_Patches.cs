using HarmonyLib;
using ONI_Together.Networking.Components;
using ONI_Together.Scripts.Buildings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	internal class Operational_Patches
	{

        [HarmonyPatch(typeof(Operational), nameof(Operational.OnPrefabInit))]
        public class Operational_OnPrefabInit_Patch
		{
            public static void Postfix(Operational __instance)
            {
	            using var _ = Profiler.Scope();

				NetworkIdentity.EnsurePersistentPrefabIdentity(__instance.gameObject);
                __instance.gameObject.AddOrGet<ClientReceiver_Operational>();
            }
        }
	}
}
