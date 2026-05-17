using System;
using System.Collections.Generic;
using System.Text;

namespace ONI_Together.DebugTools.UnitTests
{
    public static class DuplicantTests
    {
        [UnitTest(name: "Duplicant is selected?", category: "Duplicant")]
        public static UnitTestResult HasDuplicantSelected()
        {
            var selected = SelectTool.Instance?.selected;
            if (selected == null)
                return UnitTestResult.Fail("No object selected");
            var hasDuplicant = selected.TryGetComponent(out MinionIdentity _);
            if (!hasDuplicant)
                return UnitTestResult.Fail("Selected object is not a duplicant");
            return UnitTestResult.Pass($"Selected object is the duplicant: {selected.name}");
        }
    }
}
