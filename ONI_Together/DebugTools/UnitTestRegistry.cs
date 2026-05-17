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
