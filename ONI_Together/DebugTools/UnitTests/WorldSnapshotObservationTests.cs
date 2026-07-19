#if DEBUG
using System.Collections.Generic;
using ONI_Together.Misc.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class WorldSnapshotObservationTests
	{
		[UnitTest(name: "World snapshot waits for every delayed grid cell", category: "Networking")]
		public static UnitTestResult SnapshotWaitsForEveryGridCell()
		{
			List<SnapshotGridCell> expected = Cells();
			var actual = new Dictionary<int, SnapshotGridCell>();
			foreach (SnapshotGridCell cell in expected)
				actual[cell.Cell] = cell;
			actual[1] = Cell(1, mass: 99f);
			var session = new SnapshotGridObservationSession(
				expected, deadline: 15f, scanBudget: 2);

			if (session.Poll(cell => actual[cell.Cell].Equals(cell), now: 10f)
			    != SnapshotGridObservationResult.Waiting)
				return UnitTestResult.Fail("Snapshot completed while a submitted cell was not observable");
			actual[1] = expected[1];
			if (session.Poll(cell => actual[cell.Cell].Equals(cell), now: 11f)
			    != SnapshotGridObservationResult.Waiting)
				return UnitTestResult.Fail("Snapshot skipped its remaining full-grid observation work");
			if (session.Poll(cell => actual[cell.Cell].Equals(cell), now: 12f)
			    != SnapshotGridObservationResult.Completed)
				return UnitTestResult.Fail("Snapshot did not complete after every target became observable");
			if (session.Poll(cell => actual[cell.Cell].Equals(cell), now: 13f)
			    != SnapshotGridObservationResult.Finished)
				return UnitTestResult.Fail("Snapshot completion could be consumed more than once");
			return UnitTestResult.Pass("Snapshot completes exactly once after a bounded full-grid observation");
		}

		[UnitTest(name: "World snapshot observation timeout rejects once", category: "Networking")]
		public static UnitTestResult SnapshotTimeoutRejectsOnce()
		{
			var session = new SnapshotGridObservationSession(
				new[] { Cell(0) }, deadline: 25f, scanBudget: 1);
			if (session.Poll(_ => false, now: 24.9f) != SnapshotGridObservationResult.Waiting
			    || session.Poll(_ => false, now: 25f) != SnapshotGridObservationResult.TimedOut
			    || session.Poll(_ => false, now: 26f) != SnapshotGridObservationResult.Finished)
				return UnitTestResult.Fail("Snapshot observation timeout was early, missing, or repeatable");
			return UnitTestResult.Pass("Snapshot observation emits one terminal timeout without fake completion");
		}

		[UnitTest(name: "World snapshot observation normalizes empty-cell temperature", category: "Networking")]
		public static UnitTestResult SnapshotUsesNormalizedGridValues()
		{
			SnapshotGridCell submittedVacuum = Cell(0, temperature: 400f, mass: 0f);
			SnapshotGridCell observedVacuum = Cell(0, temperature: 0f, mass: -0f);
			SnapshotGridCell changedMass = Cell(0, temperature: 400f, mass: 1f);
			if (!submittedVacuum.Equals(observedVacuum) || submittedVacuum.Equals(changedMass))
				return UnitTestResult.Fail("Snapshot observation did not use normalized exact grid values");
			return UnitTestResult.Pass("Empty-cell temperature is normalized while non-empty state stays exact");
		}

		private static List<SnapshotGridCell> Cells()
			=> new List<SnapshotGridCell> { Cell(0), Cell(1), Cell(2) };

		private static SnapshotGridCell Cell(
			int cell, float temperature = 300f, float mass = 1f)
			=> new SnapshotGridCell
			{
				Cell = cell,
				ElementIdx = 2,
				Temperature = temperature,
				Mass = mass,
				DiseaseIdx = 0,
				DiseaseCount = 0,
			}.Normalized();
	}
}
#endif
