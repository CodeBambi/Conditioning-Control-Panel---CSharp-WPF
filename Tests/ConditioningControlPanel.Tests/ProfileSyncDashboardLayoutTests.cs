using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Dashboard;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dashboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Phase D - the Home slot layout rides the profile sync.
///
/// Two decisions carry the whole feature, and both are one-way doors on a live account:
///
/// SEND. The V2 load path pushes before it reads. Sending the local wall unconditionally would
/// mean a fresh install's very first POST overwrites the account's layout with the shipped
/// default, the response then echoes that default back, and the adopt below can never fire - the
/// exact shape of the bug that once stripped everyone's Trainer Card cosmetics. So the payload
/// stays null ("no change") until a round-trip has happened AND the subject has actually edited
/// something here. Only then does an empty string become meaningful, as "clear it".
///
/// ADOPT. Fill-if-empty, gated on the touched flag rather than the string, so a subject who
/// deliberately reset to the shipped wall is not handed another machine's arrangement. Null and
/// "" from the server both mean "nothing stored", never "clear yours", and a cloud string that
/// sanitizes back to the default wall is not worth spending the untouched flag on.
/// </summary>
public class ProfileSyncDashboardLayoutTests
{
    /// <summary>A real, non-default layout: Just Drop swapped off the wall for the Arcademy.</summary>
    private static string CustomWire()
    {
        var layout = DashboardLayout.Default();
        Assert.Equal(PlaceOutcome.Replaced, DashboardLayoutRule.Place(layout, 4, "arcademy", false));
        return DashboardLayoutRule.ToWire(layout);
    }

    // ---- the send decision ------------------------------------------------------------------

    [Fact]
    public void NothingIsSentBeforeTheFirstRoundTrip()
    {
        // Touched and edited, but this session has not read the account yet: still "no change".
        Assert.Null(ProfileSyncService.DashboardLayoutPayloadFor(CustomWire(), touched: true, hasLoadedProfile: false));
    }

    [Fact]
    public void NothingIsSentWhileTheWallIsUntouched()
    {
        // The shipped wall is not an opinion, so it never asserts itself over the account.
        Assert.Null(ProfileSyncService.DashboardLayoutPayloadFor(CustomWire(), touched: false, hasLoadedProfile: true));
        Assert.Null(ProfileSyncService.DashboardLayoutPayloadFor(null, touched: false, hasLoadedProfile: true));
    }

    [Fact]
    public void TheWireGoesUpOnceBothHold()
    {
        var wire = CustomWire();
        Assert.Equal(wire, ProfileSyncService.DashboardLayoutPayloadFor(wire, touched: true, hasLoadedProfile: true));
    }

    [Fact]
    public void ATouchedWallWithNoWireSendsNoChange()
    {
        // "" is the server's delete instruction and no client path means it; an inconsistent
        // touched-but-empty install stays silent rather than wiping the account's layout.
        Assert.Null(ProfileSyncService.DashboardLayoutPayloadFor(null, touched: true, hasLoadedProfile: true));
        Assert.Null(ProfileSyncService.DashboardLayoutPayloadFor("", touched: true, hasLoadedProfile: true));
    }

    [Fact]
    public void LogoutReleasesOwnershipButKeepsTheWall()
    {
        var settings = new AppSettings { DashboardLayoutWire = CustomWire(), DashboardLayoutTouched = true };
        ProfileSyncService.ReleaseDashboardLayoutOwnership(settings);
        Assert.False(settings.DashboardLayoutTouched);
        Assert.Equal(CustomWire(), settings.DashboardLayoutWire);
        // Released, the wall is adoptable again and no longer pushed.
        Assert.Null(ProfileSyncService.DashboardLayoutPayloadFor(settings.DashboardLayoutWire, settings.DashboardLayoutTouched, hasLoadedProfile: true));
        Assert.True(ProfileSyncService.ApplyCloudDashboardLayout(settings, "lockcard,flash,,,,,,,"));
        ProfileSyncService.ReleaseDashboardLayoutOwnership(null);
    }

    // ---- the adopt decision -----------------------------------------------------------------

    [Fact]
    public void AnUntouchedWallAdoptsTheCloudLayout()
    {
        var wire = CustomWire();
        var settings = new AppSettings();

        Assert.True(ProfileSyncService.ApplyCloudDashboardLayout(settings, wire));
        Assert.Equal(wire, settings.DashboardLayoutWire);
    }

    [Fact]
    public void AdoptingMarksTheWallTouched()
    {
        // Otherwise the inheriting machine would send "no change" forever and its own later edits
        // would never reach the account.
        var settings = new AppSettings();

        Assert.True(ProfileSyncService.ApplyCloudDashboardLayout(settings, CustomWire()));
        Assert.True(settings.DashboardLayoutTouched);
    }

    [Fact]
    public void ATouchedWallIsNeverRearrangedByTheCloud()
    {
        var settings = new AppSettings { DashboardLayoutTouched = true, DashboardLayoutWire = null };

        Assert.False(ProfileSyncService.ApplyCloudDashboardLayout(settings, CustomWire()));
        Assert.Null(settings.DashboardLayoutWire);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingStoredIsNotAnInstructionToClear(string? cloud)
    {
        var settings = new AppSettings();

        Assert.False(ProfileSyncService.ApplyCloudDashboardLayout(settings, cloud));
        Assert.Null(settings.DashboardLayoutWire);
        Assert.False(settings.DashboardLayoutTouched);
    }

    [Fact]
    public void ACloudLayoutThatSanitizesToTheDefaultWallIsNotAdopted()
    {
        var settings = new AppSettings();

        // The shipped wall itself.
        Assert.False(ProfileSyncService.ApplyCloudDashboardLayout(
            settings, DashboardLayoutRule.ToWire(DashboardLayout.Default())));
        // And nine fields of keys this build has never heard of, which sanitize down to nothing
        // and are then promoted back to the default rather than rendered as nine holes.
        Assert.False(ProfileSyncService.ApplyCloudDashboardLayout(settings, ",,,,,,,,"));
        Assert.False(ProfileSyncService.ApplyCloudDashboardLayout(settings, "nope,alsonope,,,,,,,"));

        Assert.Null(settings.DashboardLayoutWire);
        Assert.False(settings.DashboardLayoutTouched);
    }

    [Fact]
    public void AnAdoptedWireIsNormalizedBeforeItIsStored()
    {
        // Whatever an older or newer server hands over, what lands on disk is what this build's
        // own renderer will read back.
        var settings = new AppSettings();

        Assert.True(ProfileSyncService.ApplyCloudDashboardLayout(
            settings, "flash,video|bubblecount,subliminal,bouncingtext,arcademy,spiral|pinkfilter,mindwipe|braindrain,bubbles,ghostkey"));

        Assert.Equal(DashboardLayoutRule.ToWire(DashboardLayoutRule.FromWire(settings.DashboardLayoutWire)),
            settings.DashboardLayoutWire);
        Assert.DoesNotContain("ghostkey", settings.DashboardLayoutWire!);
    }
}
