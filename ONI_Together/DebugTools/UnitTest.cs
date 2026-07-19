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
        public bool LiveSafe { get; }

        private readonly MethodInfo _method;

        public bool HasRun { get; private set; }
        public TestState State { get; private set; } = TestState.NotRun;
        public string Message { get; private set; }
        public double DurationMs { get; private set; }

        public bool IsPassed => State == TestState.Passed;
        public bool IsFailed => State == TestState.Failed;
        public bool IsInProgress => State == TestState.InProgress;

        public UnitTest(string name, string category, bool liveSafe, MethodInfo method)
        {
            Name = name;
            Category = category;
            LiveSafe = liveSafe;
            _method = method;
        }

        public void Run(bool liveSession)
        {
            HasRun = true;
            if (liveSession && !LiveSafe)
            {
                State = TestState.NotRun;
                Message = "Not declared safe during an active multiplayer session";
                DurationMs = 0;
                return;
            }

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
