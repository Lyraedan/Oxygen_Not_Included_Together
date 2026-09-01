using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using Shared.Profiling;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	internal class WorkablePatch
	{
		private static bool IsAuthorizedToWork(Workable workable, WorkableSyncer.MethodType method, WorkerBase worker, out WorkableSyncer syncer)
		{
			using var _ = Profiler.Scope();

			syncer = null;

			if (workable == null || workable.IsNullOrDestroyed())
			{
				return true;
			}

			if (worker == null || worker.IsNullOrDestroyed())
			{
				return true;
			}

			if (MultiplayerSession.IsClient)
			{
				if (WorkableSyncer.IsAuthorized(workable, method, out var authorizedWokerNetId))
				{
					if (worker.GetNetId() == authorizedWokerNetId)
					{
						DebugConsole.Log($"[WorkablePatch] Client worker {worker.GetProperName()} is authorized to '{method}' on {workable.GetProperName()}");
						return true;
					}
				}

				// If we're a client and not authorized to work, we should skip the work
				return false;
			}

			if (MultiplayerSession.IsHostInSession && MultiplayerSession.SessionHasPlayers)
			{
				syncer = WorkableSyncer.Instance;

				if (syncer == null)
				{
					WorkableSyncer.RegisterNetId();
					syncer = WorkableSyncer.Instance;
				}

			}

			return true;
		}

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

		[HarmonyPatch(typeof(Workable), nameof(Workable.StartWork))]
		public class Workable_StartWork_Patch
		{
			public static bool Prefix(Workable __instance, out bool __state, ref WorkerBase worker_to_start)
			{
				using var _ = Profiler.Scope();
				__state = true;

				if (!IsAuthorizedToWork(__instance, WorkableSyncer.MethodType.StartWork, worker_to_start, out var syncer))
				{
					__state = false;
					return false;
				}

				
				syncer?.RequestUpdateWorkable(WorkableSyncer.MethodType.StartWork, __instance, worker_to_start);
				

				return true;
			}
			public static void Postfix(Workable __instance, bool __state, ref WorkerBase worker_to_start)
			{
				using var _ = Profiler.Scope();

				if (__instance.IsNullOrDestroyed() || !__state)
					return;
				
				if (MultiplayerSession.IsClient && IsAuthorizedToWork(__instance, WorkableSyncer.MethodType.StartWork, worker_to_start, out var _ignoredWorkerNetId))
				{
					WorkableSyncer.UnAuthorize(__instance, WorkableSyncer.MethodType.StartWork);
				}
			}
		}

		[HarmonyPatch(typeof(Workable), nameof(Workable.StopWork))]
		public class Workable_StopWork_Patch
		{
			public static bool Prefix(Workable __instance, out bool __state, ref WorkerBase workerToStop, bool aborted)
			{
				using var _ = Profiler.Scope();
				__state = true;

				if (__instance.IsNullOrDestroyed())
				{
					return true;
				}

				// Let the host decide if the work should start when we're a client
				WorkableSyncer.MethodType method = aborted ? WorkableSyncer.MethodType.AbortWork : WorkableSyncer.MethodType.StopWork;
				if (!IsAuthorizedToWork(__instance, method, workerToStop, out var syncer))
				{
					__state = false;
					return false;
				}

				syncer?.RequestUpdateWorkable(method, __instance, workerToStop);


				return true;
			}

			public static void Postfix(Workable __instance, bool __state, ref WorkerBase workerToStop, bool aborted)
			{
				using var _ = Profiler.Scope();
				if (__instance.IsNullOrDestroyed() || !__state)
					return;

				WorkableSyncer.MethodType method = aborted ? WorkableSyncer.MethodType.AbortWork : WorkableSyncer.MethodType.StopWork;
				if (MultiplayerSession.IsClient && IsAuthorizedToWork(__instance, method, workerToStop, out var _ignoredWorkerNetId))
				{
					WorkableSyncer.UnAuthorize(__instance, method);
				}
			}
		}

		[HarmonyPatch(typeof(Workable), nameof(Workable.CompleteWork))]
		public class Workable_CompleteWork_Patch
		{
			public static bool Prefix(Workable __instance, out bool __state, ref WorkerBase worker)
			{
				using var _ = Profiler.Scope();
				__state = true;

				if (__instance.IsNullOrDestroyed())
				{
					return true;
				}
				
				// Let the host decide if the work should complete when we're a client
				if (!IsAuthorizedToWork(__instance, WorkableSyncer.MethodType.CompleteWork, worker, out var syncer))
				{
					__state = false;
					return false;
				}

				syncer?.RequestUpdateWorkable(WorkableSyncer.MethodType.CompleteWork, __instance, worker);

				return true;
			}
			public static void Postfix(Workable __instance, bool __state, ref WorkerBase worker)
			{
				using var _ = Profiler.Scope();

				if (__instance.IsNullOrDestroyed() || !__state)
				{
					return;
				}

				if (MultiplayerSession.IsClient && IsAuthorizedToWork(__instance, WorkableSyncer.MethodType.CompleteWork, worker, out var _ignoredWorkerNetId))
				{
					WorkableSyncer.UnAuthorize(__instance, WorkableSyncer.MethodType.CompleteWork);
				}
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
