using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using ONI_Together.Networking;
using UnityEngine;

namespace ONI_Together.Patches.World
{
    public static class BuildingDefPatches
    {
        [HarmonyPatch(typeof(BuildingDef), nameof(BuildingDef.TryPlace), new System.Type[] { typeof(GameObject), typeof(Vector3), typeof(Orientation), typeof(IList<Tag>), typeof(string), typeof(bool), typeof(int) })]
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

        [HarmonyPatch(typeof(BuildingDef), nameof(BuildingDef.IsValidPlaceLocation), new System.Type[] { typeof(GameObject), typeof(int), typeof(Orientation), typeof(bool), typeof(string), typeof(bool) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
        public static class BuildingDef_IsValidPlaceLocation_Patch
        {
            public static void Prefix(GameObject source_go, int cell, Orientation orientation, bool replace_tile, out string fail_reason, ref bool restrictToActiveWorld)
            {
                fail_reason = "MP Override";

                if (MultiplayerSession.InSession)
                {
                    restrictToActiveWorld = false;
                }
            }
        }

        [HarmonyPatch(typeof(BuildingDef), nameof(BuildingDef.IsAreaClear), new System.Type[] { typeof(GameObject), typeof(int), typeof(Orientation), typeof(ObjectLayer), typeof(ObjectLayer), typeof(bool), typeof(bool), typeof(string), typeof(bool) }, new ArgumentType[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
        public static class BuildingDef_IsAreaClear_Patch
        {
            public static void Prefix(GameObject source_go, int cell, Orientation orientation, ObjectLayer layer, ObjectLayer tile_layer, bool replace_tile, ref bool restrictToActiveWorld, out string fail_reason, bool permitUproots)
            {
                fail_reason = "MP Override";

                if (MultiplayerSession.InSession)
                {
                    restrictToActiveWorld = false;
                }
            }
        }
    }
}
