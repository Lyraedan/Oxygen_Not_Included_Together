using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Components;
using UnityEngine;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	[HarmonyPatch]
	internal static class ArtifactPoiIdentityPatch
	{
		internal static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.DeclaredMethod(
				typeof(ArtifactPOIConfig), nameof(ArtifactPOIConfig.CreateArtifactPOI),
				new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(HashedString) });
			yield return AccessTools.DeclaredMethod(
				typeof(ArtifactPOIConfig), nameof(ArtifactPOIConfig.CreateArtifactPOI),
				new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(HashedString), typeof(int) });
		}

		internal static void Postfix(GameObject __result)
			=> ArtifactPoiSync.EnsurePersistentIdentity(__result);
	}

	[HarmonyPatch]
	internal static class HighEnergyParticlePrefabIdentityPatch
	{
		internal static MethodBase TargetMethod()
			=> AccessTools.DeclaredMethod(
				typeof(HighEnergyParticleConfig), nameof(HighEnergyParticleConfig.CreatePrefab));

		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	[HarmonyPatch]
	internal static class ReactorCometPrefabIdentityPatch
	{
		internal static MethodBase TargetMethod()
			=> AccessTools.DeclaredMethod(
				typeof(NuclearWasteCometConfig), nameof(NuclearWasteCometConfig.CreatePrefab));

		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}

	[HarmonyPatch]
	internal static class ClustercraftPrefabIdentityPatch
	{
		internal static MethodBase TargetMethod()
			=> AccessTools.DeclaredMethod(
				typeof(ClustercraftConfig), nameof(ClustercraftConfig.CreatePrefab));

		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);
	}
}

namespace ONI_Together.Patches.DLC.Aquatic
{
	[HarmonyPatch]
	internal static class MinnowPoiPrefabIdentityPatch
	{
		internal static IEnumerable<MethodBase> TargetMethods()
		{
			yield return Resolve(typeof(MinnowImperativePOIAConfig));
			yield return Resolve(typeof(MinnowImperativePOIBConfig));
			yield return Resolve(typeof(MinnowImperativePOICConfig));
		}

		internal static void Postfix(GameObject __result)
			=> NetworkIdentity.EnsurePersistentPrefabIdentity(__result);

		private static MethodBase Resolve(Type type)
			=> AccessTools.DeclaredMethod(type, "CreatePrefab");
	}
}
