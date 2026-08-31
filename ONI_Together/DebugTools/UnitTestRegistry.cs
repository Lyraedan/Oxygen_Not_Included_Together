using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ONI_Together.DebugTools
{
    public static class UnitTestRegistry
    {
        private static readonly List<UnitTest> _tests = new();

        public static IReadOnlyList<UnitTest> Tests => _tests;

        public static void DiscoverTests()
        {
            _tests.Clear();

            var assembly = typeof(UnitTestRegistry).Assembly; // Limit to only this assembly (for now)
            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                return;
            }

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = method.GetCustomAttribute<UnitTestAttribute>();
                    if (attr == null)
                        continue;

                    var name = attr.Name ?? $"{type.Name}.{method.Name}";
                    var category = attr.Category ?? "Uncategorized";

                    _tests.Add(new UnitTest(name, category, method));
                }
            }
        }

        public static void RunAll()
        {
            foreach (var test in _tests)
                test.Run();

            LogSummary("all");
        }

        /// <summary>
        /// Run one category. The UI's category picker only filtered the table - "Run All" always
        /// ran everything - so there was no way to exercise just the handful of tests that need
        /// a live hosted session without also running the other fifty.
        /// </summary>
        public static void RunCategory(string category)
        {
            foreach (var test in _tests)
            {
                if (test.Category == category)
                    test.Run();
            }

            LogSummary(category);
        }

        private static void LogSummary(string scope)
        {
            int passed = 0, failed = 0, skipped = 0, other = 0;

            foreach (var test in _tests)
            {
                if (!test.HasRun)
                    continue;
                if (scope != "all" && test.Category != scope)
                    continue;

                if (test.IsPassed) passed++;
                else if (test.IsFailed) failed++;
                else if (test.IsSkipped) skipped++;
                else other++;
            }

            DebugConsole.Log($"[UnitTest] === {scope}: {passed} passed, {failed} failed, {skipped} skipped ===");
        }

        public static void RunFailed()
        {
            foreach (var test in _tests)
            {
                if (test.HasRun && test.IsFailed)
                    test.Run();
            }
        }

        public static IEnumerable<string> GetCategories()
        {
            return _tests.Select(t => t.Category).Distinct();
        }
    }
}
