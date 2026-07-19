using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ONI_Together.Networking;

namespace ONI_Together.DebugTools
{
    internal readonly struct UnitTestRunSummary
    {
        internal int Total { get; }
        internal int Passed { get; }
        internal int Failed { get; }
        internal int NotRun { get; }
        internal bool Success => Failed == 0;

        internal UnitTestRunSummary(int total, int passed, int failed, int notRun)
        {
            Total = total;
            Passed = passed;
            Failed = failed;
            NotRun = notRun;
        }
    }

    public static class UnitTestRegistry
    {
        private static readonly List<UnitTest> _tests = new();
        private static string _discoveryError;

        public static IReadOnlyList<UnitTest> Tests => _tests;

        public static bool DiscoverTests()
        {
            _tests.Clear();
            bool succeeded = TryDiscoverTests(
                () => typeof(UnitTestRegistry).Assembly.GetTypes(),
                _tests,
                out _discoveryError);
            if (!succeeded)
                DebugConsole.LogError($"[UnitTests] DISCOVERY FAIL: {_discoveryError}", false);
            return succeeded;
        }

        internal static bool TryDiscoverTests(
            Func<Type[]> getTypes,
            ICollection<UnitTest> destination,
            out string failure)
        {
            failure = null;
            try
            {
                var discovered = new List<UnitTest>();
                foreach (var type in getTypes())
                {
                    foreach (var method in type.GetMethods(
                                 BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        var attr = method.GetCustomAttribute<UnitTestAttribute>();
                        if (attr == null)
                            continue;

                        var name = attr.Name ?? $"{type.Name}.{method.Name}";
                        var category = attr.Category ?? "Uncategorized";

                        discovered.Add(new UnitTest(name, category, attr.LiveSafe, method));
                    }
                }
                if (discovered.Count == 0)
                {
                    failure = "No unit tests were discovered";
                    return false;
                }
                foreach (var test in discovered)
                    destination.Add(test);
                return true;
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public static bool RunAll()
        {
            bool liveSession = MultiplayerSession.InSession;
            foreach (var test in _tests)
                test.Run(liveSession);

            return ReportResults("all");
        }

        public static bool RunFailed()
        {
            bool liveSession = MultiplayerSession.InSession;
            foreach (var test in _tests)
            {
                if (test.HasRun && test.IsFailed)
                    test.Run(liveSession);
            }

            return ReportResults("failed");
        }

        internal static UnitTestRunSummary CreateSummary(
            IReadOnlyCollection<UnitTest> tests,
            string discoveryError)
        {
            int discoveryFailures = string.IsNullOrEmpty(discoveryError) ? 0 : 1;
            int passed = tests.Count(test => test.IsPassed);
            int failed = tests.Count(test => test.IsFailed) + discoveryFailures;
            int total = tests.Count + discoveryFailures;
            return new UnitTestRunSummary(total, passed, failed, total - passed - failed);
        }

        private static bool ReportResults(string scope)
        {
            UnitTestRunSummary summary = CreateSummary(_tests, _discoveryError);
            DebugConsole.Log($"[UnitTests] Run {scope}: total={summary.Total}, " +
                             $"passed={summary.Passed}, failed={summary.Failed}, notRun={summary.NotRun}");
            foreach (var test in _tests.Where(test => test.IsFailed))
                DebugConsole.LogError($"[UnitTests] FAIL: {test.Category}/{test.Name}: {test.Message}", false);
            return summary.Success;
        }

        public static IEnumerable<string> GetCategories()
        {
            return _tests.Select(t => t.Category).Distinct();
        }
    }
}
