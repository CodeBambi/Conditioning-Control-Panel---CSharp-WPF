using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE WITHHOLD (CONTRACT-FUSE-0816 §2.4, owner ruling 2026-08-16) — "hide the spiral till the
/// ceremony finishes".
///
/// <para><b>The hole it closes.</b> Every spiral surface used to gate on block presence alone. At
/// zero the server's block dial auto-promotes to 'all' (§1.4), so the ONE sync that carries a
/// veteran's migration offer also carries their first descent block — and presence-only would light
/// the rail and the Trainer Card plate up beside a ceremony window that is still asking them which
/// half of their history they want to keep. The reveal is the payment for answering; it cannot
/// arrive before the question.</para>
///
/// <para><b>Why the predicate is pure.</b> <c>SpiralWithheldFor</c> takes every input — the
/// settings, whether an offer is in hand, whether the window is on screen — so all five personas
/// below can be pinned without an <c>Application</c>, a settings singleton or a server. The property
/// on the service is a two-line wrapper over it; the arithmetic is where the bug would be.</para>
/// </summary>
public class DescentSpiralWithholdTests
{
    private static AppSettings Fresh() => new();

    // ---------------------------------------------------------------- persona 1

    /// <summary>
    /// A FRESH POST-ZERO ACCOUNT IS NEVER WITHHELD. They have no history to migrate, so the server
    /// never offers them a ceremony — and the whole point of the dial promoting at zero is that they
    /// see the spiral the moment the block lands. A predicate that withheld from them would have
    /// hidden the feature from everybody who joined after launch night.
    /// </summary>
    [Fact]
    public void FreshAccount_IsNotWithheld()
    {
        var s = Fresh();

        Assert.False(DescentMigrationService.SpiralWithheldFor(s, offerInHand: false, ceremonyOpen: false));
    }

    // ---------------------------------------------------------------- persona 2

    /// <summary>A veteran with the ceremony ON SCREEN. The loudest case, and the one the spiral
    /// must never light up under.</summary>
    [Fact]
    public void VeteranMidCeremony_IsWithheld()
    {
        var s = Fresh();
        s.DescentMigrationOffered = true;

        Assert.True(DescentMigrationService.SpiralWithheldFor(s, offerInHand: true, ceremonyOpen: true));
    }

    /// <summary>
    /// The window is up but the offer has not been read off it yet — one frame wide, and still a
    /// frame in which the rail must stay dark. <c>ceremonyOpen</c> covers it on its own.
    /// </summary>
    [Fact]
    public void CeremonyOpenWithNoOfferInHand_IsStillWithheld()
    {
        Assert.True(DescentMigrationService.SpiralWithheldFor(Fresh(), offerInHand: false, ceremonyOpen: true));
    }

    /// <summary>
    /// "NOT TONIGHT", same session. The deferral is free and the question stands, so the spiral
    /// stays hidden — and it does so on <c>LiveOffer</c>, which the service never clears, rather than
    /// on anything the window had to remember to write.
    /// </summary>
    [Fact]
    public void VeteranWhoDeferred_IsWithheldForTheRestOfTheSession()
    {
        var s = Fresh();
        s.DescentMigrationOffered = true;

        Assert.True(DescentMigrationService.SpiralWithheldFor(s, offerInHand: true, ceremonyOpen: false));
    }

    /// <summary>
    /// THE RELAUNCH, which is the case that needed a persisted flag at all. The app restarts after a
    /// deferral; the descent block can arrive from the profile poll BEFORE the sync that re-delivers
    /// the offer does, and in those seconds there is nothing in memory that knows a ceremony is
    /// owed. <see cref="AppSettings.DescentMigrationOffered"/> is that memory, and without it the
    /// veteran watches the spiral flash on and off in front of an unanswered question.
    /// </summary>
    [Fact]
    public void VeteranAfterARelaunch_IsWithheldOnThePersistedMarkerAlone()
    {
        var s = Fresh();
        s.DescentMigrationOffered = true;   // written when the offer first arrived, last launch

        Assert.True(DescentMigrationService.SpiralWithheldFor(s, offerInHand: false, ceremonyOpen: false));
    }

    // ---------------------------------------------------------------- persona 3

    /// <summary>
    /// COMMITTED, AND EFFECTIVE IMMEDIATELY. ApplyChoice writes the pending choice before the server
    /// has acked anything, and from that instant the spiral is theirs — the first-light reveal opens
    /// the profile plate a second later and depends on finding this gate already open.
    ///
    /// <para>Note the inputs that are STILL set: the ceremony window is closing (so
    /// <c>ceremonyOpen</c> can still read true) and <c>_liveOffer</c> is never cleared. A predicate
    /// where "outstanding" outranked "answered" would hold the spiral shut through exactly the
    /// seconds the reveal is trying to open it.</para>
    /// </summary>
    [Theory]
    [InlineData(DescentMigrationChoices.Restore)]
    [InlineData(DescentMigrationChoices.Cycle)]
    public void VeteranWhoCommitted_IsNotWithheld_EvenWithTheOfferStillInHand(string choice)
    {
        var s = Fresh();
        s.DescentMigrationOffered = true;
        s.PendingDescentMigrationChoice = choice;

        Assert.False(DescentMigrationService.SpiralWithheldFor(s, offerInHand: true, ceremonyOpen: true));
    }

    /// <summary>The server's ack. Same answer by a different route — and it outranks a stale
    /// marker, which is why a settings file that somehow keeps the marker set past a completed
    /// migration still reads as open.</summary>
    [Fact]
    public void AckedMigration_IsNotWithheld_EvenWithAStaleMarker()
    {
        var s = Fresh();
        s.DescentMigrationOffered = true;
        s.DescentMigrationCompleted = true;

        Assert.False(DescentMigrationService.SpiralWithheldFor(s, offerInHand: true, ceremonyOpen: true));
    }

    /// <summary>
    /// A JUNK CHOICE IS NOT AN ANSWER. The gate opens on a VALID pending choice only — the same
    /// validity test ProfileSyncService uses to decide whether to re-open the ceremony — so a
    /// hand-edited settings file cannot unlock the spiral by writing nonsense into the field.
    /// </summary>
    [Fact]
    public void AnInvalidPendingChoice_DoesNotOpenTheGate()
    {
        var s = Fresh();
        s.DescentMigrationOffered = true;
        s.PendingDescentMigrationChoice = "whatever";

        Assert.True(DescentMigrationService.SpiralWithheldFor(s, offerInHand: false, ceremonyOpen: false));
    }

    // ---------------------------------------------------------------- the edges

    /// <summary>
    /// No settings (headless, or a load that failed) reads as NOT withheld. No settings means no
    /// account, no block and no surface — and "withheld" would be a feature blanked by a failure
    /// somewhere else entirely.
    /// </summary>
    [Fact]
    public void NullSettings_AreNotWithheld()
    {
        Assert.False(DescentMigrationService.SpiralWithheldFor(null, offerInHand: true, ceremonyOpen: true));
    }

    /// <summary>
    /// THE DORMANCY CLAIM for the whole withhold: a service nobody has offered anything to, on a
    /// settings object nobody has written, withholds nothing. That is every install on today's
    /// server, and it is why these surfaces are byte-identical to the build before this landed.
    /// </summary>
    [Fact]
    public void AnUntouchedService_WithholdsNothing()
    {
        var service = new DescentMigrationService();

        Assert.Null(service.LiveOffer);
        Assert.False(service.IsCeremonyOpen);
        // App.Settings is null in a headless test, which the predicate reads as "nothing to
        // withhold from" — the same posture the property's own catch takes.
        Assert.False(service.SpiralWithheld);
        Assert.False(DescentMigrationService.SpiralWithheldFor(Fresh(), offerInHand: false, ceremonyOpen: false));
    }

    /// <summary>
    /// The in-session half of the deferral, through the real service: an offer that arrives is
    /// remembered in <c>LiveOffer</c> forever, which is the input the predicate reads. This is the
    /// wiring the persona above asserts the arithmetic for.
    /// </summary>
    [Fact]
    public void AnOfferReceived_LeavesTheWithholdsInputSet()
    {
        var service = new DescentMigrationService();
        service.HoldOffers();   // keeps the window-open path from needing a dispatcher

        service.OfferReceived(new DescentMigrationOffer { TotalXpEarned = 120_000, DevotionDays = 240 });

        Assert.NotNull(service.LiveOffer);
        Assert.True(DescentMigrationService.SpiralWithheldFor(Fresh(), offerInHand: service.LiveOffer is not null, ceremonyOpen: false));
    }

    /// <summary>
    /// THE #1111 LATCH IS NOT A WITHHOLD INPUT, and must never become one. The per-session deferral
    /// added in v6.9.1 stops the ceremony RE-OPENING every 120-second heartbeat after "Not tonight";
    /// it says nothing about whether the question has been answered, and the spiral is still owed
    /// nobody until it has. So the offer stays in <c>LiveOffer</c> across the close, the withhold
    /// still reads true off it, and the arithmetic below is byte-identical to the case above.
    ///
    /// <para>The failure this pins is the tempting one: "they closed it, so let them have the
    /// spiral". That would pay out the reveal for dismissing the question instead of answering it,
    /// and it would do so on a flag that exists purely to stop a window re-painting.</para>
    /// </summary>
    [Fact]
    public void ADeferredCeremony_StillWithholdsTheSpiral()
    {
        var service = new DescentMigrationService();
        service.HoldOffers();
        service.OfferReceived(new DescentMigrationOffer { TotalXpEarned = 120_000, DevotionDays = 240 });

        service.NoteCeremonyClosed(committed: false);

        Assert.True(service.DeferredThisSession);
        Assert.NotNull(service.LiveOffer);      // the withhold's input survives the close

        var s = Fresh();
        s.DescentMigrationOffered = true;
        Assert.True(DescentMigrationService.SpiralWithheldFor(s, offerInHand: service.LiveOffer is not null, ceremonyOpen: false));

        // ...and committing is still the only thing that opens it, deferral latched or not.
        s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;
        Assert.False(DescentMigrationService.SpiralWithheldFor(s, offerInHand: true, ceremonyOpen: false));
    }
}
