using System;

namespace ONI_Together.DebugTools
{

    [AttributeUsage(AttributeTargets.Method)]
    public class UnitTestAttribute : Attribute
    {
        public string Name { get; }
        public string Category { get; }
        public bool LiveSafe { get; }

        public UnitTestAttribute(
            string name = null,
            string category = "General",
            bool liveSafe = false)
        {
            Name = name;
            Category = category;
            LiveSafe = liveSafe;
        }
    }
}
