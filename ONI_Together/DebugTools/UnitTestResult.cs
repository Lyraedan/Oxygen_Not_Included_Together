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
        Failed
    }

    public class UnitTestResult
    {
        public TestState State { get; private set; } = TestState.NotRun;
        public string Message { get; private set; }

        public static UnitTestResult Pass(string message = null)
            => new UnitTestResult { State = TestState.Passed, Message = message };

        public static UnitTestResult Fail(string message)
            => new UnitTestResult { State = TestState.Failed, Message = message };

        public static UnitTestResult Skip(string message)
            => new UnitTestResult { State = TestState.NotRun, Message = message };

        public static UnitTestResult InProgress(string message = null)
            => new UnitTestResult { State = TestState.InProgress, Message = message };
    }
}
