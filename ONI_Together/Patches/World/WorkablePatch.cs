using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using Shared.Profiling;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	internal class WorkablePatch
	{
		private static bool TryGetRemotePercent(Component target, RemoteProgressKind progressKind, out float percentComplete)
		{
			using var _ = Profiler.Scope();

			percentComplete = 0f;
			if (!MultiplayerSession.IsClient || target == null || target.gameObject.IsNullOrDestroyed())
			{
				return false;
			}

			if (!target.gameObject.TryGetComponent<NetworkIdentity>(out var identity) || identity == null || identity.NetId == 0)
			{
				return false;
			}

			return RemoteProgressRegistry.TryGetPercent(identity.NetId, progressKind, out percentComplete);
		}

		[HarmonyPatch(typeof(Workable), nameof(Workable.OnPrefabInit))]
		public class Workable_OnPrefabInit_Patch
		{
			public static void Postfix(Workable __instance)
			{
				using var _ = Profiler.Scope();

				__instance.gameObject.AddOrGet<NetworkIdentity>();
			}
		}

		[HarmonyPatch(typeof(Workable), nameof(Workable.GetPercentComplete))]
		public class Workable_GetPercentComplete_Patch
		{
			public static bool Prefix(Workable __instance, ref float __result)
			{
				using var _ = Profiler.Scope();

				if (!TryGetRemotePercent(__instance, RemoteProgressKind.WorkablePercent, out float percentComplete))
				{
					return true;
				}

				__result = percentComplete;
				return false;
			}
		}

		[HarmonyPatch]
		public class DerivedWorkable_GetPercentComplete_Patch
		{
			private static IEnumerable<MethodBase> TargetMethods()
			{
				using var _ = Profiler.Scope();

				string[] typeNames =
				{
					"Diggable",
					"EmptyConduitWorkable",
					"EmptySolidConduitWorkable",
					"AstronautTrainingCenter",
					"ResearchCenter",
					"NuclearResearchCenterWorkable"
				};

				foreach (string typeName in typeNames)
				{
					var type = AccessTools.TypeByName(typeName);
					var method = type == null ? null : AccessTools.Method(type, nameof(Workable.GetPercentComplete));
					if (method != null)
					{
						yield return method;
					}
				}
			}

			public static bool Prefix(Component __instance, ref float __result)
			{
				using var _ = Profiler.Scope();

				if (!TryGetRemotePercent(__instance, RemoteProgressKind.WorkablePercent, out float percentComplete))
				{
					return true;
				}

				__result = percentComplete;
				return false;
			}
		}

		[HarmonyPatch(typeof(ComplexFabricator), "get_OrderProgress")]
		public class ComplexFabricator_OrderProgress_Patch
		{
			public static bool Prefix(ComplexFabricator __instance, ref float __result)
			{
				using var _ = Profiler.Scope();

				if (!TryGetRemotePercent(__instance, RemoteProgressKind.ComplexFabricatorOrder, out float percentComplete))
				{
					return true;
				}

				__result = percentComplete;
				return false;
			}
		}
	}
}
