using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;

namespace ONI_Together.Patches.KleiPatches
{
    public class KSelectablePatches
    {
        public static Dictionary<StatusItemGroup, NetworkIdentity> SelectableStatusItemGroupToNetIdentity = new Dictionary<StatusItemGroup, NetworkIdentity>();

        [HarmonyPatch(typeof(KSelectable), nameof(KSelectable.OnPrefabInit))]
        public static class KSelectable_OnPrefabInit_Patch
        {
            public static void Postfix(KSelectable __instance)
            {
                // Disabled for now
                return;

                SelectableStatusItemGroupToNetIdentity.Add(__instance.statusItemGroup, __instance.GetNetIdentity());
            }
        }

        [HarmonyPatch(typeof(KSelectable), nameof(KSelectable.OnCleanUp))]
        public static class KKSelectable_OnCleanUp_Patch
        {
            public static bool Prefix(KSelectable __instance)
            {
                // Disabled for now
                return true;

                if (SelectableStatusItemGroupToNetIdentity.TryGetValue(__instance.statusItemGroup, out NetworkIdentity netIdentity)) {
                    SelectableStatusItemGroupToNetIdentity.Remove(__instance.statusItemGroup);
                }
                return true;
            }
        }
    }
}
