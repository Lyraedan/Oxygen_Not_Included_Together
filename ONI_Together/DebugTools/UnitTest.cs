using System;
using System.Diagnostics;
using System.Reflection;
using static UnityEngine.LowLevelPhysics2D.PhysicsQuery;

namespace ONI_Together.DebugTools
{
    public class UnitTest
    {
        public string Name { get; }
        public string Category { get; }

        private readonly MethodInfo _method;

        public bool HasRun { get; private set; }
        public TestState State { get; private set; } = TestState.NotRun;
        public string Message { get; private set; }
        public double DurationMs { get; private set; }

        public bool IsPassed => State == TestState.Passed;
        public bool IsFailed => State == TestState.Failed;
        public bool IsInProgress => State == TestState.InProgress;

        public UnitTest(string name, string category, MethodInfo method)
        {
            Name = name;
            Category = category;
            _method = method;
        }

        public void Run()
        {
            HasRun = true;
            State = TestState.InProgress;
            Message = null;

            var sw = Stopwatch.StartNew();

            try
            {
                var result = _method.Invoke(null, null);

                if (_method.ReturnType == typeof(UnitTestResult))
                {
                    var testResult = (UnitTestResult)result;

                    State = testResult.State;
                    Message = testResult.Message;
                }
                else
                {
                    // No return type = assume pass if no exception
                    State = TestState.Passed;
                }
            }
            catch (Exception ex)
            {
                if (ex is TargetInvocationException tie && tie.InnerException != null)
                    Message = tie.InnerException.ToString();
                else
                    Message = ex.ToString();

                State = TestState.Failed;
            }

            sw.Stop();
            DurationMs = sw.Elapsed.TotalMilliseconds;
        }
    }
}
