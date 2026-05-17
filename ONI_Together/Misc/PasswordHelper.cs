using System;
using System.Security.Cryptography;
using System.Text;
using Shared.Profiling;

namespace ONI_Together.Misc
{
    /// <summary>
    /// Utility class for password hashing and verification.
    /// Uses SHA256 for simplicity - suitable for game lobbies.
    /// </summary>
    public static class PasswordHelper
    {
        /// <summary>
        /// Hash a password using SHA256.
        /// </summary>
        public static string HashPassword(string password)
        {
            using var _ = Profiler.Scope();

            if (string.IsNullOrEmpty(password))
                return string.Empty;

            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(password);
                byte[] hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Verify a password against a stored hash.
        /// </summary>
        public static bool VerifyPassword(string password, string storedHash)
        {
            using var _ = Profiler.Scope();

            if (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(storedHash))
                return true;

            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
                return false;

            string inputHash = HashPassword(password);
            return string.Equals(inputHash, storedHash, StringComparison.Ordinal);
        }

        /// <summary>
        /// Check if a password meets minimum requirements.
        /// </summary>
        public static bool IsValidPassword(string password)
        {
            using var _ = Profiler.Scope();

            // Allow empty passwords (no password protection)
            if (string.IsNullOrEmpty(password))
                return true;

            // Minimum 4 characters for simplicity
            return password.Length >= 4;
        }

        /// <summary>
        /// Check if a hash string is valid (non-empty and proper format).
        /// </summary>
        public static bool HasPassword(string hash)
        {
            using var _ = Profiler.Scope();

            return !string.IsNullOrEmpty(hash);
        }
    }
}
