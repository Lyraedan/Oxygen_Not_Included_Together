using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ONI_Together.Networking.Components;
using ONI_Together.Patches.DLC.Aquatic;
using ONI_Together.Patches.DLC.SpacedOut;
using ONI_Together.Patches.World;
using ONI_Together.Patches.World.Buildings;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class PersistentRuntimeIdentityTests
	{
		[UnitTest(name: "Runtime save/load prefabs persist authoritative identity", category: "Sync")]
		public static UnitTestResult PrefabIdentityCoverage()
		{
			MethodInfo persistent = Method(
				typeof(NetworkIdentity), nameof(NetworkIdentity.EnsurePersistentPrefabIdentity));
			MethodInfo artifactInjector = Method(
				typeof(ArtifactPoiSync), nameof(ArtifactPoiSync.EnsurePersistentIdentity));
			List<MethodBase> artifactTargets = ArtifactPoiIdentityPatch.TargetMethods().ToList();
			if (persistent == null || artifactInjector == null ||
			    artifactTargets.Count != 2 || artifactTargets.Any(method => method == null) ||
			    HighEnergyParticlePrefabIdentityPatch.TargetMethod() == null ||
			    ReactorCometPrefabIdentityPatch.TargetMethod() == null ||
			    ClustercraftPrefabIdentityPatch.TargetMethod() == null)
			{
				return UnitTestResult.Fail("A runtime save/load prefab identity target is missing");
			}

			List<MethodBase> minnowTargets = MinnowPoiPrefabIdentityPatch.TargetMethods().ToList();
			if (minnowTargets.Count != 3 || minnowTargets.Any(method => method == null))
				return UnitTestResult.Fail("Not all Minnow POI prefabs persist NetworkIdentity");

			if (!Calls(Method(typeof(ArtifactPoiIdentityPatch), "Postfix"), artifactInjector) ||
			    !Calls(artifactInjector, persistent) ||
			    !Calls(Method(typeof(HighEnergyParticlePrefabIdentityPatch), "Postfix"), persistent) ||
			    !Calls(Method(typeof(ReactorCometPrefabIdentityPatch), "Postfix"), persistent) ||
			    !Calls(Method(typeof(ClustercraftPrefabIdentityPatch), "Postfix"), persistent) ||
			    !Calls(Method(typeof(MinnowPoiPrefabIdentityPatch), "Postfix"), persistent) ||
			    !Calls(Method(typeof(Operational_Patches.Operational_OnPrefabInit_Patch), "Postfix"), persistent) ||
			    !Calls(Method(typeof(WorkablePatch.Workable_OnPrefabInit_Patch), "Postfix"), persistent) ||
			    !Calls(Method(typeof(PickupablePatches.PickupablePrefabPatch), "Postfix"), persistent) ||
			    !Calls(Method(typeof(BuildingComplete_Patches.BuildingComplete_OnPrefabInit_Patch), "Postfix"), persistent))
			{
				return UnitTestResult.Fail("A runtime prefab does not declare persistent NetworkIdentity");
			}
			return UnitTestResult.Pass("Late join restores runtime NetIds before domain state arrives");
		}

		private static MethodInfo Method(Type type, string name)
			=> type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic |
			                        BindingFlags.Static | BindingFlags.Instance);

		private static bool Calls(MethodInfo caller, MethodInfo callee)
		{
			byte[] il = caller?.GetMethodBody()?.GetILAsByteArray();
			if (il == null || callee == null)
				return false;
			byte[] token = BitConverter.GetBytes(callee.MetadataToken);
			for (int i = 0; i <= il.Length - token.Length; i++)
				if (il[i] == token[0] && il[i + 1] == token[1] &&
				    il[i + 2] == token[2] && il[i + 3] == token[3])
					return true;
			return false;
		}
	}
}
