using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using ONI_MP.Networking;
using UnityEngine;

namespace ONI_MP.Patches.World
{
    public static class BuildingDefPatches
    {
        [HarmonyPatch(typeof(BuildingDef),nameof(BuildingDef.TryPlace),new System.Type[] { typeof(GameObject), typeof(Vector3), typeof(Orientation), typeof(IList<Tag>), typeof(string), typeof(bool), typeof(int) })]
        public static class BuildingDef_TryPlace_Patch
        {
            public static void Prefix(GameObject src_go, Vector3 pos, Orientation orientation, IList<Tag> selected_elements, string facadeID, ref bool restrictToActiveWorld, int layer)
            {
                if (MultiplayerSession.InSession)
                {
                    restrictToActiveWorld = false;
                }
            }
        }

        // There is a few more functions like IsAreaClear that do currently restrict to active world. Unsure if this causes problems atm
    }
}
