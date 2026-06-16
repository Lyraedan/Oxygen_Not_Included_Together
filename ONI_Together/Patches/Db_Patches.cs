using HarmonyLib;
using ONI_Together.Patches.World.SideScreen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Patches
{
	internal class Db_Patches
	{

        [HarmonyPatch(typeof(Db), nameof(Db.Initialize))]
        public class Db_Initialize_Patch
        {
            public static void Postfix(Db __instance)
            {
	            using var _ = Profiler.Scope();

                Door_QueueStateChange_Patch.ExecutePatch();
                Door_Sim200ms_Patch.ExecutePatch();
                Door_OnCleanUp_Patch.ExecutePatch();

			}
        }
	}
}
