#if DEBUG
using System;
using System.Linq;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SoakStateHashTests
	{
		[UnitTest(name: "Soak state hash is independent of enumeration order", category: "Networking")]
		public static UnitTestResult HashIsIndependentOfEnumerationOrder()
		{
			var cells = new[]
			{
				new SoakCellState { Cell = 9, ElementIdx = 2, Mass = 100, Temperature = 29315 },
				new SoakCellState { Cell = 3, ElementIdx = 1, Mass = 50, Temperature = 27115 },
			};
			var entities = new[]
			{
				new SoakEntityState { NetId = 40, PrefabHash = 7, Active = true },
				new SoakEntityState { NetId = 12, PrefabHash = 5, Active = false },
			};

			SoakStateHashes forward = SoakStateHash.Compute(cells, entities);
			SoakStateHashes reverse = SoakStateHash.Compute(cells.Reverse(), entities.Reverse());

			if (!forward.Grid.SequenceEqual(reverse.Grid))
				return UnitTestResult.Fail("Grid hash changed when only enumeration order changed");
			if (!forward.Registry.SequenceEqual(reverse.Registry))
				return UnitTestResult.Fail("Entity hash changed when only enumeration order changed");

			return UnitTestResult.Pass("Canonical state hashes ignore enumeration order");
		}

		[UnitTest(name: "Soak grid hash detects disease state changes", category: "Networking")]
		public static UnitTestResult GridHashDetectsDiseaseStateChanges()
		{
			var baseline = new[]
			{
				new SoakCellState { Cell = 3, ElementIdx = 1, Mass = 50, Temperature = 27115,
					DiseaseIdx = 2, DiseaseCount = 10 },
			};
			var changed = new[]
			{
				new SoakCellState { Cell = 3, ElementIdx = 1, Mass = 50, Temperature = 27115,
					DiseaseIdx = 2, DiseaseCount = 11 },
			};

			byte[] baselineHash = SoakStateHash.Compute(baseline, Enumerable.Empty<SoakEntityState>()).Grid;
			byte[] changedHash = SoakStateHash.Compute(changed, Enumerable.Empty<SoakEntityState>()).Grid;
			if (baselineHash.SequenceEqual(changedHash))
				return UnitTestResult.Fail("Grid hash ignored a disease count change");

			return UnitTestResult.Pass("Grid hash detects disease state changes");
		}

		[UnitTest(name: "Soak entity hash detects active state changes", category: "Networking")]
		public static UnitTestResult EntityHashDetectsActiveStateChanges()
		{
			var baseline = new[]
			{
				new SoakEntityState { NetId = 12, PrefabHash = 5, Active = true },
			};
			var changed = new[]
			{
				new SoakEntityState { NetId = 12, PrefabHash = 5, Active = false },
			};

			byte[] baselineHash = SoakStateHash.Compute(Enumerable.Empty<SoakCellState>(), baseline).Registry;
			byte[] changedHash = SoakStateHash.Compute(Enumerable.Empty<SoakCellState>(), changed).Registry;
			if (baselineHash.SequenceEqual(changedHash))
				return UnitTestResult.Fail("Entity hash ignored an active-state change");

			return UnitTestResult.Pass("Entity hash detects active-state changes");
		}

		[UnitTest(name: "Soak float encoding normalizes equivalent special values", category: "Networking")]
		public static UnitTestResult FloatEncodingNormalizesEquivalentSpecialValues()
		{
			if (SoakStateHash.NormalizeFloatBits(0f) != SoakStateHash.NormalizeFloatBits(-0f))
				return UnitTestResult.Fail("Positive and negative zero encoded differently");
			if (SoakStateHash.NormalizeFloatBits(float.NaN) !=
				SoakStateHash.NormalizeFloatBits(
					BitConverter.ToSingle(BitConverter.GetBytes(unchecked((int)0x7fc01234)), 0)))
				return UnitTestResult.Fail("NaN payloads encoded differently");
			if (SoakStateHash.NormalizeFloatBits(1f) == SoakStateHash.NormalizeFloatBits(1.01f))
				return UnitTestResult.Fail("Distinct finite values encoded identically");

			return UnitTestResult.Pass("Float encoding is canonical without rounding finite values");
		}

		[UnitTest(name: "Soak world drift diagnostics identify changed fields", category: "Networking")]
		public static UnitTestResult WorldDriftDiagnosticsIdentifyChangedFields()
		{
			var expected = new SoakWorldMembershipState
			{
				NetId = 12, WorldId = 1, Cell = 9,
				PositionX = 10, PositionY = 20, PositionZ = 30,
				HasPositionHandler = true, FlipX = true, NavType = NavType.Floor,
			};
			SoakWorldMembershipState actual = expected;
			if (SoakWorldMembershipDiagnostics.DifferentFields(expected, actual) != "none")
				return UnitTestResult.Fail("Equal world records were reported as drifted");

			actual.PositionY++;
			actual.NavType = NavType.Ladder;
			string fields = SoakWorldMembershipDiagnostics.DifferentFields(expected, actual);
			return fields == "positionY,navType"
				? UnitTestResult.Pass("World drift diagnostics identify exact changed fields")
				: UnitTestResult.Fail("Unexpected drift field list: " + fields);
		}

		[UnitTest(name: "Soak entity drift diagnostics identify changed fields", category: "Networking")]
		public static UnitTestResult EntityDriftDiagnosticsIdentifyChangedFields()
		{
			var expected = new SoakEntityState
			{
				NetId = 12, PrefabHash = 5, Active = true, Revision = 8,
				IsDuplicant = true, IsInLiveRoster = true,
				IsInLiveRosterByModel = true,
			};
			SoakEntityState actual = expected;
			if (SoakEntityLifecycleDiagnostics.DifferentFields(expected, actual) != "none")
				return UnitTestResult.Fail("Equal entity records were reported as drifted");

			actual.Active = false;
			actual.IsInLiveRoster = false;
			string fields = SoakEntityLifecycleDiagnostics.DifferentFields(expected, actual);
			return fields == "active,liveRoster"
				? UnitTestResult.Pass("Entity drift diagnostics identify exact changed fields")
				: UnitTestResult.Fail("Unexpected entity drift field list: " + fields);
		}
	}
}
#endif
