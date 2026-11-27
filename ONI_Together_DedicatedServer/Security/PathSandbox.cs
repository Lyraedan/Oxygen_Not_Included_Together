using System;
using System.IO;

namespace ONI_Together_DedicatedServer.Security
{
    /// <summary>
    /// Confines user-controlled file paths to a known root to prevent directory traversal
    /// and accidental writes outside the intended saves/config directory.
    /// </summary>
    public static class PathSandbox
    {
        /// <summary>
        /// Resolve <paramref name="candidate"/> against <paramref name="root"/> and ensure the
        /// result stays inside <paramref name="root"/>. Returns null when unsafe.
        /// </summary>
        public static string? ResolveInsideRoot(string root, string candidate)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
                return null;

            string fullRoot;
            string fullCandidate;
            try
            {
                fullRoot = Path.GetFullPath(root);
                // Rooted candidates are resolved as-is, then checked for containment.
                fullCandidate = Path.IsPathRooted(candidate)
                    ? Path.GetFullPath(candidate)
                    : Path.GetFullPath(Path.Combine(fullRoot, candidate));
            }
            catch (Exception)
            {
                return null;
            }

            // Compare with trailing separator to avoid "/root2" matching root "/root".
            string rootWithSep = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;

            bool inside = fullCandidate.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
                          || fullCandidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);

            // On Linux/macOS comparisons are case-sensitive; retry with ordinal.
            if (!inside)
            {
                inside = fullCandidate.Equals(fullRoot, StringComparison.Ordinal)
                         || fullCandidate.StartsWith(
                             fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            }

            return inside ? fullCandidate : null;
        }

        /// <summary>
        /// Reject path segments that could escape a directory or are otherwise dangerous.
        /// </summary>
        public static bool IsSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;
            if (name.Contains("..", StringComparison.Ordinal))
                return false;
            if (name.Contains('/') || name.Contains('\\'))
                return false;
            // No control chars / NUL.
            foreach (char c in name)
            {
                if (char.IsControl(c))
                    return false;
            }
            return true;
        }
    }
}
