using ONI_Together.DebugTools;
using ONI_Together.Misc;
using Steamworks;
using System;
using System.Numerics;
using System.Text;
using Shared.Profiling;

namespace ONI_Together.Networking
{
    /// <summary>
    /// Helper class to convert Steam Lobby IDs to human-readable codes and back.
    /// Uses Base36 encoding (0-9, A-Z) for readability.
    /// </summary>
    public static class LobbyCodeHelper
    {
        private const string Base36Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const int CodeLength = 8; // Fixed length for consistency

        /// <summary>
        /// Convert a Steam Lobby ID to a short alphanumeric code.
        /// </summary>
        public static string GenerateCode(ulong lobbyId)
        {
            using var _ = Profiler.Scope();

            if (!lobbyId.IsValid())
            {
                DebugConsole.LogWarning("[LobbyCodeHelper] Cannot generate code for invalid lobby ID");
                return string.Empty;
            }

            try
            {
                ulong id = lobbyId;
                return EncodeBase36(id);
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[LobbyCodeHelper] Failed to generate code: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Parse a lobby code back to a Steam Lobby ID.
        /// </summary>
        public static bool TryParseCode(string code, out ulong lobbyId)
        {
            using var _ = Profiler.Scope();

            lobbyId = Utils.NilUlong();

            if (string.IsNullOrWhiteSpace(code))
            {
                DebugConsole.LogWarning("[LobbyCodeHelper] Cannot parse empty code");
                return false;
            }

            try
            {
                code = code.Trim().ToUpperInvariant();
                ulong id = DecodeBase36(code);
                lobbyId = id;
                return lobbyId.IsValid();
            }
            catch (Exception ex)
            {
                DebugConsole.LogError($"[LobbyCodeHelper] Failed to parse code '{code}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validate that a code string is properly formatted.
        /// </summary>
        public static bool IsValidCodeFormat(string code)
        {
            using var _ = Profiler.Scope();

            if (string.IsNullOrWhiteSpace(code))
                return false;

            code = code.Trim().ToUpperInvariant();

            foreach (char c in code)
            {
                if (Base36Chars.IndexOf(c) < 0)
                    return false;
            }

            return code.Length >= 1 && code.Length <= 16;
        }

        /// <summary>
        /// Format a code for display (e.g., add dashes for readability).
        /// </summary>
        public static string FormatCodeForDisplay(string code)
        {
            using var _ = Profiler.Scope();

            return code;

			if (string.IsNullOrEmpty(code) || code.Length <= 4)
                return code;

            // Split into groups of 4 for readability: ABCD-EFGH-IJKL
            var sb = new StringBuilder();
            for (int i = 0; i < code.Length; i++)
            {
                if (i > 0 && i % 4 == 0)
                    sb.Append('-');
                sb.Append(code[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Remove formatting from a code (remove dashes, spaces, etc).
        /// </summary>
        public static string CleanCode(string code)
        {
            using var _ = Profiler.Scope();

            if (string.IsNullOrEmpty(code))
                return string.Empty;

            return code.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
        }

        private static string EncodeBase36(ulong value)
        {
            using var _ = Profiler.Scope();

            if (value == 0)
                return "0";

            var sb = new StringBuilder();
            while (value > 0)
            {
                int remainder = (int)(value % 36);
                sb.Insert(0, Base36Chars[remainder]);
                value /= 36;
            }

            // Pad to minimum length for consistency
            while (sb.Length < CodeLength)
            {
                sb.Insert(0, '0');
            }

            return sb.ToString();
        }

        private static ulong DecodeBase36(string encoded)
        {
            using var _ = Profiler.Scope();

            encoded = CleanCode(encoded);
            ulong result = 0;

            foreach (char c in encoded)
            {
                int charValue = Base36Chars.IndexOf(c);
                if (charValue < 0)
                    throw new FormatException($"Invalid character in code: {c}");

                result = result * 36 + (ulong)charValue;
            }

            return result;
        }
    }
}
