using System;
using System.Collections.Generic;
using System.Linq;

namespace ONI_Together_DedicatedServer.Security
{
    /// <summary>
    /// DLC ownership / compatibility checking mode. Klei requires that content ownership
    /// not be bypassed; this enforces against client-declared DLC. It cannot cryptographically
    /// verify Klei Steam ownership without game/Steam integration (see notes in TODO.md).
    /// </summary>
    public enum DlcCheckMode
    {
        /// <summary>No server-side DLC enforcement (rely on existing client-side mismatch checks).</summary>
        Off = 0,
        /// <summary>Enforce against client-declared owned/active DLC lists.</summary>
        Declared = 1,
        /// <summary>Like Declared, but reject clients that do not declare DLC info.</summary>
        Required = 2
    }

    public sealed class ClientDlcInfo
    {
        public bool Declared { get; set; }
        public IReadOnlyCollection<string> OwnedDlcIds { get; set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> ActiveDlcIds { get; set; } = Array.Empty<string>();
    }

    public sealed class DlcCheckResult
    {
        public bool Allowed { get; init; }
        public string Reason { get; init; } = "";
        public Shared.Networking.Dedicated.RelayRejectCode Code { get; init; } = Shared.Networking.Dedicated.RelayRejectCode.DlcPolicy;
        public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();

        public static DlcCheckResult Ok() => new() { Allowed = true, Reason = "" };
        public static DlcCheckResult Deny(string reason) => new() { Allowed = false, Reason = reason };
        public static DlcCheckResult Deny(Shared.Networking.Dedicated.RelayRejectCode code, string reason, params string[] args) =>
            new() { Allowed = false, Reason = reason, Code = code, Args = args };
    }

    public interface IDlcOwnershipVerifier
    {
        DlcCheckResult Verify(ClientDlcInfo client);
    }

    public sealed class DlcPolicy
    {
        public DlcCheckMode Mode { get; set; } = DlcCheckMode.Off;
        public IReadOnlyCollection<string> RequiredDlcIds { get; set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> ForbiddenDlcIds { get; set; } = Array.Empty<string>();
        public IReadOnlyCollection<string> ServerActiveDlcIds { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Verifies client-declared DLC against server policy. Does NOT prove Klei ownership;
    /// combine with existing client-side ownership checks and (future) Klei/Steam validation.
    /// </summary>
    public sealed class DeclaredDlcOwnershipVerifier : IDlcOwnershipVerifier
    {
        private readonly DlcPolicy _policy;
        public DeclaredDlcOwnershipVerifier(DlcPolicy policy) => _policy = policy ?? new DlcPolicy();

        public DlcCheckResult Verify(ClientDlcInfo client)
        {
            client ??= new ClientDlcInfo();

            if (_policy.Mode == DlcCheckMode.Off)
                return DlcCheckResult.Ok();

            if (_policy.Mode == DlcCheckMode.Required && !client.Declared)
                return DlcCheckResult.Deny("DLC declaration required.");

            if (_policy.Mode == DlcCheckMode.Declared && !client.Declared)
                return DlcCheckResult.Ok();

            var owned = Normalize(client.OwnedDlcIds);
            var active = Normalize(client.ActiveDlcIds);
            var activeOrOwned = owned.Union(active, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Required DLC must be owned (and active for matching).
            foreach (var req in Normalize(_policy.RequiredDlcIds))
            {
                if (!activeOrOwned.Contains(req))
                    return DlcCheckResult.Deny($"Missing required DLC: {req}");
                if (!active.Contains(req) && !owned.Contains(req))
                    return DlcCheckResult.Deny($"Required DLC not active/owned: {req}");
            }

            // Forbidden DLC must not be active/owned.
            foreach (var forb in Normalize(_policy.ForbiddenDlcIds))
            {
                if (activeOrOwned.Contains(forb) || active.Contains(forb))
                    return DlcCheckResult.Deny($"Forbidden DLC present: {forb}");
            }

            // DLC match between server and client when server active DLC is configured.
            var serverActive = Normalize(_policy.ServerActiveDlcIds);
            if (serverActive.Count > 0)
            {
                // Client must have all server-active DLC.
                foreach (var s in serverActive)
                    if (!activeOrOwned.Contains(s))
                        return DlcCheckResult.Deny($"DLC mismatch; server requires: {s}");

                // Client must not require DLC server doesn't have (active not in server set).
                foreach (var a in active)
                    if (!serverActive.Contains(a))
                        return DlcCheckResult.Deny($"DLC mismatch; client has extra active DLC: {a}");
            }

            return DlcCheckResult.Ok();
        }

        private static HashSet<string> Normalize(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ids == null) return set;
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                set.Add(id.Trim());
            }
            return set;
        }
    }
}
