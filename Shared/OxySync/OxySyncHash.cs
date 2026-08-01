using System;

namespace Shared.OxySync
{
    /// <summary>
    /// Stable protocol hash for OxySync field, method, and singleton identifiers.
    /// Unlike string.GetHashCode(), this value is deterministic across processes,
    /// runtimes, operating systems, and randomized hash implementations.
    /// </summary>
    public static class OxySyncHash
    {
        public static int Compute(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            unchecked
            {
                const uint offsetBasis = 2166136261u;
                const uint prime = 16777619u;
                uint hash = offsetBasis;

                // Hash both bytes of every UTF-16 code unit. This avoids allocations
                // and defines the exact same input representation on every runtime.
                for (int i = 0; i < value.Length; i++)
                {
                    char character = value[i];
                    hash ^= (byte)character;
                    hash *= prime;
                    hash ^= (byte)(character >> 8);
                    hash *= prime;
                }

                return (int)hash;
            }
        }
    }
}
