using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Patches.DLC.SpacedOut;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ArtifactPoiLifecycleTests
	{
		[UnitTest(name: "Artifact POI late binding is lifecycle-gated", category: "Sync")]
		public static UnitTestResult LateBindingGate()
		{
			if (ArtifactPoiSync.CanBindLifecycle(10, false, 9) ||
			    ArtifactPoiSync.CanBindLifecycle(10, true, 10) ||
			    !ArtifactPoiSync.CanBindLifecycle(10, false, 10) ||
			    !ArtifactPoiSync.CanBindLifecycle(10, true, 11))
			{
				return UnitTestResult.Fail("Artifact POI accepted stale or tombstoned lifecycle");
			}
			MethodInfo resolver = typeof(ArtifactPoiSync).GetMethod(
				"TryResolvePoi", BindingFlags.NonPublic | BindingFlags.Static);
			MethodInfo binder = typeof(NetworkIdentityRegistry).GetMethod(
				nameof(NetworkIdentityRegistry.TryBindAuthoritativeLifecycle));
			if (!Calls(resolver, binder))
				return UnitTestResult.Fail("Artifact POI resolver does not bind authoritative lifecycle");
			return UnitTestResult.Pass("Artifact POI binds one location-matched live lifecycle");
		}

		[UnitTest(name: "Artifact POI prefab persists network identity", category: "Sync")]
		public static UnitTestResult PersistentIdentityInjection()
		{
			List<MethodBase> targets = ArtifactPoiIdentityPatch.TargetMethods().ToList();
			MethodInfo postfix = typeof(ArtifactPoiIdentityPatch).GetMethod(
				"Postfix", BindingFlags.NonPublic | BindingFlags.Static);
			MethodInfo injector = typeof(ArtifactPoiSync).GetMethod(
				nameof(ArtifactPoiSync.EnsurePersistentIdentity),
				BindingFlags.NonPublic | BindingFlags.Static);
			if (targets.Count != 2 || targets.Any(target => target == null) ||
			    !targets.Select(target => target.GetParameters().Length).OrderBy(count => count)
				    .SequenceEqual(new[] { 5, 6 }) || postfix == null || injector == null)
				return UnitTestResult.Fail("Artifact POI persistent identity patch is incomplete");
			if (!Calls(postfix, injector) ||
			    injector.ReturnType != typeof(NetworkIdentity))
			{
				return UnitTestResult.Fail("Artifact POI prefab does not inject a persistent NetworkIdentity");
			}
			return UnitTestResult.Pass("Artifact POI save/load prefab always declares NetworkIdentity");
		}

		private static bool Calls(MethodInfo method, MethodInfo target)
		{
			byte[] il = method?.GetMethodBody()?.GetILAsByteArray();
			if (il == null || target == null)
				return false;
			byte[] token = BitConverter.GetBytes(target.MetadataToken);
			for (int i = 0; i <= il.Length - token.Length; i++)
				if (il[i] == token[0] && il[i + 1] == token[1] &&
				    il[i + 2] == token[2] && il[i + 3] == token[3])
					return true;
			return false;
		}
	}
}
