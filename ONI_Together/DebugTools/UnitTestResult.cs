using System;
using System.Collections.Generic;
using System.Text;

namespace ONI_Together.DebugTools
{
    public enum TestState
    {
        NotRun,
        InProgress,
        Passed,
        Failed,
        Skipped
    }

    public class UnitTestResult
    {
        public TestState State { get; private set; } = TestState.NotRun;
        public string Message { get; private set; }

        public static UnitTestResult Pass(string message = null)
            => new UnitTestResult { State = TestState.Passed, Message = message };

        public static UnitTestResult Fail(string message)
            => new UnitTestResult { State = TestState.Failed, Message = message };

        /// <summary>Could not run here - wrong role, no session, world not loaded.</summary>
        public static UnitTestResult Skip(string reason)
            => new UnitTestResult { State = TestState.Skipped, Message = reason };

        public static UnitTestResult InProgress(string message = null)
            => new UnitTestResult { State = TestState.InProgress, Message = message };
    }
}
