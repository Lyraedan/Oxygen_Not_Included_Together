using System;

namespace Shared.OxySync.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class InterestGroupAttribute : Attribute
    {
        public int Group { get; set; } = -1;
    }
}