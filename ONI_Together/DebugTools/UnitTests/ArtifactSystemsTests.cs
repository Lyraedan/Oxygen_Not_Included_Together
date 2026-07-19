using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class ArtifactSystemsTests
	{
		[UnitTest(name: "Artifact system Harmony targets match build 740622", category: "Sync")]
		public static UnitTestResult HarmonyTargets()
		{
			if (!Matches(typeof(PedestalArtifactSpawner), "OnSpawn", typeof(void)) ||
			    !Matches(typeof(ClusterGridOneTimeResourceSpawner.Instance),
				    nameof(ClusterGridOneTimeResourceSpawner.Instance.SpawnResources), typeof(void)) ||
			    !Matches(typeof(PropSurfaceSatellite3Config), "OnLockerLooted", typeof(void),
				    typeof(GameObject)) ||
			    !Matches(typeof(Storage), nameof(Storage.Store), typeof(GameObject),
				    typeof(GameObject), typeof(bool), typeof(bool), typeof(bool), typeof(bool)) ||
			    !Matches(typeof(SingleEntityReceptacle), nameof(SingleEntityReceptacle.ForceDeposit),
				    typeof(void), typeof(GameObject)) ||
			    !Matches(typeof(Workable), nameof(Workable.StartWork), typeof(void), typeof(WorkerBase)) ||
			    !Matches(typeof(Workable), nameof(Workable.StopWork), typeof(void),
				    typeof(WorkerBase), typeof(bool)) ||
			    !Matches(typeof(Workable), nameof(Workable.CompleteWork), typeof(void), typeof(WorkerBase)))
				return UnitTestResult.Fail("An Artifact system Harmony target signature changed");
			return UnitTestResult.Pass("Artifact system targets match build 740622");
		}

		[UnitTest(name: "Artifact random and one-time gameplay is host authoritative", category: "Sync")]
		public static UnitTestResult GameplayAuthority()
		{
			if (!ArtifactGameplaySync.ShouldRunAuthoritative(false, false, true) ||
			    !ArtifactGameplaySync.ShouldRunAuthoritative(true, true, true) ||
			    ArtifactGameplaySync.ShouldRunAuthoritative(true, false, true) ||
			    !ArtifactGameplaySync.ShouldRunAuthoritative(true, false, false) ||
			    !ArtifactAnalysisSync.ShouldRunCompletion(false, false) ||
			    !ArtifactAnalysisSync.ShouldRunCompletion(true, true) ||
			    ArtifactAnalysisSync.ShouldRunCompletion(true, false))
				return UnitTestResult.Fail("Artifact gameplay authority gate is incorrect");
			return UnitTestResult.Pass("Artifact RNG, one-time output and analysis run only on host");
		}

		[UnitTest(name: "Artifact pedestal preserves lifecycle and restores entity links", category: "Sync")]
		public static UnitTestResult PedestalLifecycleAndAttachment()
		{
			if (ArtifactGameplaySync.GetPedestalSpawnGuardValue(false, false, false) ||
			    ArtifactGameplaySync.GetPedestalSpawnGuardValue(false, true, true) ||
			    !ArtifactGameplaySync.GetPedestalSpawnGuardValue(false, true, false) ||
			    !ArtifactGameplaySync.GetPedestalSpawnGuardValue(true, true, false))
				return UnitTestResult.Fail("Pedestal client guard does not suppress only client RNG");
			if (ArtifactGameplaySync.NeedsPedestalAttachment(true, true) ||
			    !ArtifactGameplaySync.NeedsPedestalAttachment(false, true) ||
			    !ArtifactGameplaySync.NeedsPedestalAttachment(true, false) ||
			    !ArtifactGameplaySync.NeedsPedestalAttachment(false, false))
				return UnitTestResult.Fail("Pedestal attachment idempotency gate is incorrect");
			return UnitTestResult.Pass("Pedestal original lifecycle runs with temporary RNG guard and absolute links");
		}

		[UnitTest(name: "Artifact pedestal and satellite outcomes are absolute host state", category: "Sync")]
		public static UnitTestResult SpawnStateRoundtrip()
		{
			var input = new ArtifactSpawnStatePacket
			{
				SourceNetId = -11,
				Source = ArtifactSpawnSource.Satellite,
				Spawned = true,
				ArtifactNetId = 12,
				ArtifactId = "artifact_teapot",
				ArtifactCharmed = true,
				TerrestrialArtifact = true,
				Selector = new ArtifactSelectorStateData
				{
					Terrestrial = new List<string> { "artifact_teapot" }
				}
			};
			ArtifactSpawnStatePacket output = Roundtrip(input, new ArtifactSpawnStatePacket());
			if (output is not IHostOnlyPacket || output.SourceNetId != -11 ||
			    output.Source != ArtifactSpawnSource.Satellite || !output.Spawned ||
			    output.ArtifactNetId != 12 || output.ArtifactId != "artifact_teapot" ||
			    !output.ArtifactCharmed || !output.TerrestrialArtifact ||
			    output.Selector.Terrestrial.Count != 1 ||
			    output.Selector.Terrestrial[0] != "artifact_teapot")
				return UnitTestResult.Fail("Artifact spawn state did not roundtrip");

			input.Spawned = false;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Artifact spawn state accepted payload for an unspawned source");
			input.Spawned = true;
			input.Selector.Terrestrial.Clear();
			for (int i = 0; i <= ArtifactSelectorStateData.MaxIds; i++)
				input.Selector.Terrestrial.Add($"artifact_{i}");
			if (input.IsWireValid())
				return UnitTestResult.Fail("Artifact spawn state accepted an oversized selector snapshot");
			return UnitTestResult.Pass("Pedestal and satellite artifact state is bounded and host-only");
		}

		[UnitTest(name: "Artifact POI one-time resources use absolute host state", category: "Sync")]
		public static UnitTestResult PoiOneTimeStateRoundtrip()
		{
			var input = new ArtifactPoiOneTimeStatePacket
			{
				PoiNetId = -31,
				HasSpawnedResources = true
			};
			ArtifactPoiOneTimeStatePacket output = Roundtrip(
				input, new ArtifactPoiOneTimeStatePacket());
			if (output is not IHostOnlyPacket || output.PoiNetId != -31 ||
			    !output.HasSpawnedResources)
				return UnitTestResult.Fail("Artifact POI one-time state did not roundtrip");
			return UnitTestResult.Pass("Artifact POI one-time state is host-only and absolute");
		}

		[UnitTest(name: "Artifact analysis completion is idempotent absolute state", category: "Sync")]
		public static UnitTestResult AnalysisStateRoundtrip()
		{
			var input = new ArtifactAnalysisStatePacket
			{
				StationNetId = -41,
				Revision = 3,
				WorkerNetId = 0,
				WorkTimeRemaining = 150f,
				ArtifactNetId = 42,
				ArtifactId = "artifact_teapot",
				ArtifactCharmed = false,
				TerrestrialArtifact = true,
				Selector = new ArtifactSelectorStateData
				{
					Terrestrial = new List<string> { "artifact_teapot" },
					AnalyzedTerrestrialCount = 1,
					AnalyzedIds = new List<string> { "artifact_teapot" }
				}
			};
			ArtifactAnalysisStatePacket output = Roundtrip(
				input, new ArtifactAnalysisStatePacket());
			if (output is not IHostOnlyPacket || output.StationNetId != -41 ||
			    output.Revision != 3 || output.WorkerNetId != 0 ||
			    output.WorkTimeRemaining != 150f || output.ArtifactNetId != 42 ||
			    output.ArtifactCharmed || output.Selector.AnalyzedIds.Count != 1 ||
			    output.Selector.AnalyzedTerrestrialCount != 1)
				return UnitTestResult.Fail("Artifact analysis state did not roundtrip");
			if (ArtifactAnalysisSync.NeedsApply(3, 3) ||
			    ArtifactAnalysisSync.NeedsApply(4, 3) ||
			    !ArtifactAnalysisSync.NeedsApply(2, 3))
				return UnitTestResult.Fail("Artifact analysis revision gate is not idempotent");
			return UnitTestResult.Pass("Analysis work, unlock and artifact state is bounded and idempotent");
		}

		private static T Roundtrip<T>(T input, T output) where T : IPacket
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			if (stream.Position != stream.Length)
				throw new InvalidDataException($"{typeof(T).Name} left unread bytes");
			return output;
		}

		private static bool Matches(Type type, string name, Type returnType, params Type[] parameters)
		{
			MethodInfo method = AccessTools.Method(type, name, parameters);
			return method != null && method.ReturnType == returnType;
		}
	}
}
