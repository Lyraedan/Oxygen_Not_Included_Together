using System;

namespace ONI_Together.DebugTools
{

    [AttributeUsage(AttributeTargets.Method)]
    public class UnitTestAttribute : Attribute
    {
        public string Name { get; }
        public string Category { get; }

        public UnitTestAttribute(string name = null, string category = "General")
        {
            Name = name;
            Category = category;
        }
    }
}
