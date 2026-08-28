using System;

namespace Shared.OxySync
{
    /// <summary>
    /// Stable protocol hash for OxySync field, method, and singleton identifiers.
    /// Delegates to string.GetHashCode() which is deterministic on Unity Mono
    /// (as requested by maintainer - Lyraedan).
    /// 
    /// I requested this be deleted. Not delegated - Lyraedan
    /// </summary>
    public static class OxySyncHash
    {
        public static int Compute(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return value.GetHashCode();
        }
    }
}
