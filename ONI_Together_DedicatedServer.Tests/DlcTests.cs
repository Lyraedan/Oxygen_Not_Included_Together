using System;
using ONI_Together_DedicatedServer.Security;
using Xunit;

public class DlcTests
{
    [Fact]
    public void Off_allows_undeclared()
    {
        var v = new DeclaredDlcOwnershipVerifier(new DlcPolicy { Mode = DlcCheckMode.Off });
        Assert.True(v.Verify(new ClientDlcInfo()).Allowed);
    }

    [Fact]
    public void Declared_allows_undeclared_when_not_required()
    {
        var v = new DeclaredDlcOwnershipVerifier(new DlcPolicy { Mode = DlcCheckMode.Declared });
        Assert.True(v.Verify(new ClientDlcInfo { Declared = false }).Allowed);
    }

    [Fact]
    public void Required_rejects_undeclared()
    {
        var v = new DeclaredDlcOwnershipVerifier(new DlcPolicy { Mode = DlcCheckMode.Required });
        Assert.False(v.Verify(new ClientDlcInfo { Declared = false }).Allowed);
    }

    [Fact]
    public void RequiredDlc_must_be_owned_and_active()
    {
        var v = new DeclaredDlcOwnershipVerifier(new DlcPolicy
        {
            Mode = DlcCheckMode.Required,
            RequiredDlcIds = new[] { "expansion1" }
        });

        Assert.False(v.Verify(new ClientDlcInfo { Declared = true, OwnedDlcIds = Array.Empty<string>(), ActiveDlcIds = Array.Empty<string>() }).Allowed);
        // Owned OR active satisfies "required DLC" in implementation.
        Assert.True(v.Verify(new ClientDlcInfo { Declared = true, OwnedDlcIds = new[] { "expansion1" }, ActiveDlcIds = Array.Empty<string>() }).Allowed);
        Assert.True(v.Verify(new ClientDlcInfo { Declared = true, OwnedDlcIds = new[] { "expansion1" }, ActiveDlcIds = new[] { "expansion1" } }).Allowed);
    }

    [Fact]
    public void ForbiddenDlc_rejected()
    {
        var v = new DeclaredDlcOwnershipVerifier(new DlcPolicy
        {
            Mode = DlcCheckMode.Declared,
            ForbiddenDlcIds = new[] { "some.dlc" }
        });
        Assert.False(v.Verify(new ClientDlcInfo { Declared = true, OwnedDlcIds = new[] { "some.dlc" } }).Allowed);
    }

    [Fact]
    public void Dlc_mismatch_rejected()
    {
        var v = new DeclaredDlcOwnershipVerifier(new DlcPolicy
        {
            Mode = DlcCheckMode.Required,
            ServerActiveDlcIds = new[] { "expansion1" }
        });
        // Client missing server DLC.
        Assert.False(v.Verify(new ClientDlcInfo { Declared = true, ActiveDlcIds = Array.Empty<string>(), OwnedDlcIds = Array.Empty<string>() }).Allowed);
        // Client has extra active DLC.
        Assert.False(v.Verify(new ClientDlcInfo { Declared = true, ActiveDlcIds = new[] { "expansion1", "extra" }, OwnedDlcIds = new[] { "expansion1", "extra" } }).Allowed);
    }

    [Fact]
    public void Required_and_forbidden_overlap_sanitized()
    {
        var cfg = new ONI_Together_DedicatedServer.DedicatedConfig
        {
            DlcCheckMode = DlcCheckMode.Required,
            RequiredDlcIds = new[] { "x", "y" },
            ForbiddenDlcIds = new[] { "y" }
        };
        cfg.Sanitize();
        Assert.DoesNotContain("y", cfg.RequiredDlcIds);
        Assert.Contains("x", cfg.RequiredDlcIds);
    }
}
