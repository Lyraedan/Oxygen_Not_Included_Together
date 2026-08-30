using System;
using HarmonyLib;
using ONI_Together.DebugTools;
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
					if (__result == null || __result.HasTag(GameTags.BaseMinion))
						return;

					__result.AddOrGet<OxySyncEntityPositionHandler>();
					__result.AddOrGet<NetworkIdentity>();
					__result.AddOrGet<AnimSyncer>();
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
