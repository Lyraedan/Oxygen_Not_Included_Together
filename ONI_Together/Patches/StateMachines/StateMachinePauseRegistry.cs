using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Patches.StateMachines
{
    public static class StateMachinePauseRegistry
    {
        private static readonly ConditionalWeakTable<StateMachine.Instance, PauseData> table = new ConditionalWeakTable<StateMachine.Instance, PauseData>();

        public class PauseData
        {
            public bool Paused;
        }

        public static bool IsPaused(StateMachine.Instance smi) => table.TryGetValue(smi, out var data) && data.Paused;

        public static void SetPaused(StateMachine.Instance smi, bool paused)
        {
            using var _ = Profiler.Scope();

            var data = table.GetOrCreateValue(smi);
            data.Paused = paused;
        }

        public static void PauseSMI(this StateMachine.Instance smi)
        {
            using var _ = Profiler.Scope();

            SetPaused(smi, true);
        }

        public static void ResumeSMI(this StateMachine.Instance smi)
        {
            using var _ = Profiler.Scope();

            SetPaused(smi, false);
        }

        public static bool IsSMIPaused(this StateMachine.Instance smi)
        {
            using var _ = Profiler.Scope();

            return IsPaused(smi);
        }
    }
}
