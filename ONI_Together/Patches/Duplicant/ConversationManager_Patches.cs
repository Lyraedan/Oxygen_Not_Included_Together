using HarmonyLib;
using ONI_Together.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Patches.Duplicant
{
	internal class ConversationManager_Patches
	{

        [HarmonyPatch(typeof(ConversationManager), nameof(ConversationManager.Sim200ms))]
        public class ConversationManager_Sim200ms_Patch
        {
            public static bool Prefix(ConversationManager __instance)
            {
                using var _ = Profiler.Scope();

                return !MultiplayerSession.IsClient;
            }
        }
	}
}
