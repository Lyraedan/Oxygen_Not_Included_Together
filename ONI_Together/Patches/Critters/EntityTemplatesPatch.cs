using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Menus;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Scripts.Creatures;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.Critters
{
	internal class EntityTemplatesPatch
	{
		[HarmonyPatch(typeof(EntityTemplates), nameof(EntityTemplates.ExtendEntityToBasicCreature), new Type[] { typeof(EntityTemplates.ExtendEntityToBasicCreatureData) })]
		public static class ExtendEntityToBasicCreature_Patch
		{
			public static void Postfix(GameObject __result)
			{
				using var _ = Profiler.Scope();
				try
				{
					if (__result == null)
						return;

					if (!AnimSyncEligibility.IsAnimatedCritter(__result))
						return;

					__result.AddOrGet<OxySyncEntityPositionHandler>();
					__result.AddOrGet<NetworkIdentity>();
					__result.AddOrGet<AnimStateSyncer>();
					__result.AddOrGet<CreatureMultiplayerInitializer>();
				}
				catch (Exception ex)
				{
					DebugConsole.LogError($"[EntityTemplatesPatch.ExtendEntityToBasicCreature_Patch] {ex}");
				}
			}
		}
	}
}
