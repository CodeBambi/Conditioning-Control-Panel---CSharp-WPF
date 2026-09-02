using System;
using System.IO;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE ZERO SHOW's ordering, its audio gate and its chrome restore (CONTRACT-FUSE-0816 §2.3/§2.4).
///
/// <para>Three small contracts that are each one line of code and each capable of ruining the
/// night if they are the wrong line: whether the ceremony can open UNDERNEATH a fullscreen show,
/// whether an audio file nobody has shipped can make a noise, and whether the room ever gets its
/// colour back after the ceremony is taken.</para>
///
/// <para><b>What these tests cannot reach.</b> Without an <c>Application.Current</c> there is no
/// dispatcher, so the replay half of the hold (which marshals a window open) stops at the guard.
/// The tests below therefore pin the DECISION — held or not, remembered or dropped — and leave the
/// window open itself to the integration path. That is the correct seam: the decision is where the
/// bug would be.</para>
/// </summary>
public class DescentZeroShowOrderingTests
{
    private static DescentMigrationOffer AnOffer() =>
        new() { TotalXpEarned = 120_000, DevotionDays = 240 };

    // ------------------------------------------------------------ the offer hold

    /// <summary>
    /// Nothing is held by default. This is the dormancy claim for the hold itself: on every install
    /// that never plays a catch-up crack — which is all of them, on today's server — the gate is a
    /// zero that nobody increments.
    /// </summary>
    [Fact]
    public void ByDefault_NothingIsHeld()
    {
        var service = new DescentMigrationService();
        Assert.Equal(0, service.OfferHoldDepth);
        Assert.False(service.IsCeremonyOpen);
    }

    /// <summary>
    /// Depth counting, both ways. Nested holders must not release each other's, and — the one that
    /// would actually bite — an over-release from a teardown path that ran twice must not drive the
    /// gate negative and quietly arm it backwards.
    /// </summary>
    [Fact]
    public void HoldsAreDepthCountedAndCannotGoNegative()
    {
        var service = new DescentMigrationService();

        service.HoldOffers();
        service.HoldOffers();
        Assert.Equal(2, service.OfferHoldDepth);

        service.ReleaseOffers();
        Assert.Equal(1, service.OfferHoldDepth);

        service.ReleaseOffers();
        Assert.Equal(0, service.OfferHoldDepth);

        service.ReleaseOffers();
        service.ReleaseOffers();
        Assert.Equal(0, service.OfferHoldDepth);
    }

    /// <summary>
    /// THE ORDERING GUARANTEE (§2.4). An offer that lands while the catch-up crack is on screen is
    /// REMEMBERED, not dropped and not opened: the ceremony window must arrive after the fullscreen
    /// show has left, never underneath it. Holding it is only acceptable because it is a delay —
    /// the offer survives in <c>LiveOffer</c> and the release replays it.
    /// </summary>
    [Fact]
    public void AnOfferDuringTheShow_IsHeldNotDropped()
    {
        var service = new DescentMigrationService();
        service.HoldOffers();

        service.OfferReceived(AnOffer());

        Assert.False(service.IsCeremonyOpen);
        Assert.NotNull(service.LiveOffer);
        Assert.Equal(240, service.LiveOffer!.DevotionDays);

        // THE GATE ITSELF: with the hold up, the offer in hand cannot open anything.
        Assert.False(service.CanOpenCeremony());

        // And the moment the show is gone, it can — same offer, nothing re-fetched, no second sync
        // needed. The hold delayed the ceremony; it never cancelled it.
        service.ReleaseOffers();
        Assert.Equal(0, service.OfferHoldDepth);
        Assert.NotNull(service.LiveOffer);

        // EITHER OUTCOME IS THE GUARANTEE, and asserting only the first one made this suite depend
        // on the order it ran in. ReleaseOffers does not just un-gate: when an Application exists it
        // goes on to BeginInvoke the open, which is the correct behaviour and which sets the very
        // flag CanOpenCeremony reads. WpfRenderHarness stands one up on an STA thread and leaves it
        // alive for the rest of the process, so whether this test saw a live Application came down
        // to whether it beat the first WPF suite to the punch — and the BeginInvoke is asynchronous,
        // so even losing that race gave a different answer run to run. What §2.4 actually promises
        // is that the release either opened the ceremony or left it openable; what it never does is
        // drop the offer. That holds in both environments and in either half of the race.
        Assert.True(service.CanOpenCeremony() || service.IsCeremonyOpen);
    }

    /// <summary>
    /// A null offer is still nothing, held or not. The show's hold must not turn a malformed sync
    /// into a queued ceremony that opens the moment the crack finishes.
    /// </summary>
    [Fact]
    public void ANullOffer_IsIgnoredUnderAHold()
    {
        var service = new DescentMigrationService();
        service.HoldOffers();
        service.OfferReceived(null!);

        Assert.Null(service.LiveOffer);
        Assert.False(service.IsCeremonyOpen);
        Assert.False(service.CanOpenCeremony());
    }

    // ------------------------------------------------- the same-session deferral (#1111)

    /// <summary>
    /// THE BUG v6.9.0 SHIPPED. The sync heartbeat is 120 seconds and every response still carries
    /// <c>descent_migration.required</c> — by design, because the server keeps offering until a
    /// choice actually lands. With nothing but the "a window is already up" guard in front of it,
    /// "Not tonight" bought about two minutes before the fullscreen ceremony came back, and back,
    /// and back; #1111 (RopePunny, Michaela) both rolled back to 6.8.6 over it.
    ///
    /// <para>The whole fix is the fourth input to the open predicate, so that is what this pins:
    /// an uncommitted close latches, and every re-offer after it is a no-op.</para>
    /// </summary>
    [Fact]
    public void AnUncommittedClose_StopsTheHeartbeatReopeningTheCeremony()
    {
        var service = new DescentMigrationService();
        service.HoldOffers();               // keeps the open path off a dispatcher that is not there

        service.OfferReceived(AnOffer());
        Assert.False(service.DeferredThisSession);

        // "Not tonight" — the window leaves with nothing written.
        service.NoteCeremonyClosed(committed: false);

        Assert.True(service.DeferredThisSession);
        Assert.False(service.IsCeremonyOpen);

        // The next four heartbeats. Each one re-sends the same standing offer and each one is
        // turned away — this is the loop, and this is it not happening.
        for (var i = 0; i < 4; i++) service.OfferReceived(AnOffer());

        Assert.False(service.IsCeremonyOpen);
        Assert.False(service.CanOpenCeremony());
    }

    /// <summary>
    /// THE DEFERRAL DOES NOT COST THE WITHHOLD. <c>_liveOffer</c> is never cleared, so the spiral
    /// surfaces stay dark for the rest of a deferred session exactly as they did before — the
    /// documented intent, and the thing a fix that "cancelled" the offer instead would have broken.
    /// </summary>
    [Fact]
    public void ADeferredSession_StillHoldsTheOfferAndTheWithhold()
    {
        var service = new DescentMigrationService();
        service.HoldOffers();

        service.OfferReceived(AnOffer());
        service.NoteCeremonyClosed(committed: false);
        service.OfferReceived(AnOffer());

        Assert.NotNull(service.LiveOffer);
        Assert.Equal(240, service.LiveOffer!.DevotionDays);
        Assert.True(DescentMigrationService.SpiralWithheldFor(
            new ConditioningControlPanel.Models.AppSettings { DescentMigrationOffered = true },
            offerInHand: service.LiveOffer is not null, ceremonyOpen: false));
    }

    /// <summary>
    /// THE SECOND DOOR INTO THE SAME LOOP. <see cref="DescentMigrationService.ReleaseOffers"/>
    /// re-opens the moment <c>CanOpenCeremony</c> passes, so a deferral enforced only inside
    /// <c>OfferReceived</c> would still be walked straight through by a catch-up show that ends
    /// after the subject closed the ceremony. The latch lives in the predicate for exactly this.
    /// </summary>
    [Fact]
    public void AHoldReleaseCycleAfterADeferral_DoesNotReopen()
    {
        var service = new DescentMigrationService();
        service.HoldOffers();
        service.OfferReceived(AnOffer());
        service.NoteCeremonyClosed(committed: false);

        // A fullscreen show comes and goes on top of the deferred session.
        service.HoldOffers();
        Assert.False(service.CanOpenCeremony());
        service.ReleaseOffers();

        Assert.False(service.CanOpenCeremony());
        Assert.False(service.IsCeremonyOpen);

        // ...and the original hold lifting is not a way back in either.
        service.ReleaseOffers();
        Assert.Equal(0, service.OfferHoldDepth);
        Assert.False(service.CanOpenCeremony());
    }

    /// <summary>
    /// A COMMITTED CLOSE IS NOT A DEFERRAL. It does not need to be — the server stops sending
    /// <c>required</c> once the choice lands, and ProfileSyncService turns the re-send away on the
    /// pending choice long before this — but latching on a commit would be a lie in the state, and
    /// the flag is read by the predicate that decides whether anything can ever open again.
    /// </summary>
    [Fact]
    public void ACommittedClose_DoesNotDefer()
    {
        var service = new DescentMigrationService();
        service.HoldOffers();
        service.OfferReceived(AnOffer());

        service.NoteCeremonyClosed(committed: true);

        Assert.False(service.DeferredThisSession);
        Assert.False(service.IsCeremonyOpen);

        service.ReleaseOffers();
        // Same race as the assertion at line 103: ReleaseOffers may go on to open the ceremony when
        // an Application is alive, so §2.4 is satisfied by either half. See #472 for the full story.
        Assert.True(service.CanOpenCeremony() || service.IsCeremonyOpen);
    }

    /// <summary>
    /// A CEREMONY THAT NEVER PAINTED NEVER DEFERRED. A held offer — the catch-up crack's ordering
    /// guarantee, and the same shape as a run muted with <c>CCP_DESCENT_CEREMONY=off</c> — opens no
    /// window and closes none, so nothing latches and the release replays it exactly as it always
    /// did. The latch is armed by a user closing something, never by a sync.
    /// </summary>
    [Fact]
    public void AHeldOfferThatNeverOpened_IsNotADeferral()
    {
        var service = new DescentMigrationService();
        service.HoldOffers();

        service.OfferReceived(AnOffer());
        service.OfferReceived(AnOffer());   // and again, the way the heartbeat does

        Assert.False(service.DeferredThisSession);

        service.ReleaseOffers();
        // Same race again: a released hold either opens the ceremony or leaves it openable, and the
        // offer is never dropped either way.
        Assert.True(service.CanOpenCeremony() || service.IsCeremonyOpen);
    }

    /// <summary>
    /// THE CLOSE EVENT IS UNCHANGED. <c>CeremonyClosed</c> still carries "was it committed" to the
    /// Year One ignition on every exit path — the deferral latch is set beside the raise, not
    /// instead of it, so a subscriber added for the fuse still hears both halves.
    /// </summary>
    [Fact]
    public void TheCloseEvent_StillFiresWithTheCommittedFlag()
    {
        var service = new DescentMigrationService();
        var heard = new System.Collections.Generic.List<bool>();
        service.CeremonyClosed += (_, committed) => heard.Add(committed);

        service.NoteCeremonyClosed(committed: false);
        service.NoteCeremonyClosed(committed: true);

        Assert.Equal(new[] { false, true }, heard);
    }

    /// <summary>
    /// THE OPEN PREDICATE, as arithmetic. Four inputs, and every one of them is a veto — the same
    /// shape as <c>SpiralWithheldFor</c>, and testable without a dispatcher for the same reason.
    /// </summary>
    [Fact]
    public void TheOpenPredicate_VetoesOnEveryInput()
    {
        Assert.True(DescentMigrationService.ShouldOpenCeremonyFor(
            offerInHand: true, ceremonyOpen: false, offerHold: 0, deferredThisSession: false));

        Assert.False(DescentMigrationService.ShouldOpenCeremonyFor(
            offerInHand: false, ceremonyOpen: false, offerHold: 0, deferredThisSession: false));
        Assert.False(DescentMigrationService.ShouldOpenCeremonyFor(
            offerInHand: true, ceremonyOpen: true, offerHold: 0, deferredThisSession: false));
        Assert.False(DescentMigrationService.ShouldOpenCeremonyFor(
            offerInHand: true, ceremonyOpen: false, offerHold: 1, deferredThisSession: false));
        Assert.False(DescentMigrationService.ShouldOpenCeremonyFor(
            offerInHand: true, ceremonyOpen: false, offerHold: 0, deferredThisSession: true));
    }

    // ------------------------------------------------------------ the heartbeat

    /// <summary>
    /// THE ASSET DOES NOT SHIP, and the gate says so. §2.4 is explicit: missing file ⇒ silent
    /// no-op. This is the check that stops a future "let's just use an existing wav" from becoming
    /// a note the owner never approved, playing under the loudest ten minutes of the year.
    /// </summary>
    [Fact]
    public void Heartbeat_IsSilentWithoutItsAsset()
    {
        Assert.False(DescentHeartbeat.ShouldPlay(audioEnabled: true, masterVolume: 80, assetExists: false));
    }

    /// <summary>Every gate independently means silence. All three have to agree.</summary>
    [Theory]
    [InlineData(true,  80, true,  true)]
    [InlineData(false, 80, true,  false)]   // the subject turned the countdown's audio off
    [InlineData(true,   0, true,  false)]   // the app is muted
    [InlineData(true,  80, false, false)]   // no asset
    [InlineData(false,  0, false, false)]
    public void Heartbeat_GateNeedsEveryCondition(bool enabled, int master, bool exists, bool expected)
    {
        Assert.Equal(expected, DescentHeartbeat.ShouldPlay(enabled, master, exists));
    }

    /// <summary>
    /// The asset's path is the contract's path, off BaseDirectory like every other on-disk sound —
    /// so a content pack or a hand-drop can supply it without a rebuild, and so a reviewer looking
    /// for "where would the file go" finds one answer.
    /// </summary>
    [Fact]
    public void Heartbeat_LooksWhereTheContractSays()
    {
        var path = DescentHeartbeat.AssetPath;
        Assert.Equal("heartbeat.wav", Path.GetFileName(path));
        Assert.Equal("descent", Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Equal("Resources", Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))));
    }

    // ------------------------------------------------------------ the chrome restore

    /// <summary>
    /// The dimming HOLDS through zero — that is deliberate, so the room does not brighten while the
    /// show is opening — and this pins that the two-argument overload every existing caller uses is
    /// completely unchanged by the migration-aware one added beside it.
    /// </summary>
    [Fact]
    public void DimStep_StillHoldsAtFourThroughZero()
    {
        var at = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(4, DescentCountdownService.DimStepFor(at, at));
        Assert.Equal(4, DescentCountdownService.DimStepFor(at, at.AddHours(6)));
        Assert.Equal(4, DescentCountdownService.DimStepFor(at, at.AddHours(11)));
        // ...and lets go on its own once the night is long over (0825 F4, DimHoldPastZero).
        Assert.Equal(0, DescentCountdownService.DimStepFor(at, at.AddDays(9)));
    }

    /// <summary>
    /// A MIGRATED ACCOUNT IS NOT IN THE DARK ANY MORE (§2.4's chrome restore). Without this the
    /// pure arithmetic returns 4 for every moment after the instant, so a subject who took the
    /// ceremony on the first night would keep a dimmed app until the owner eventually unset the
    /// server timestamp days later — and the ignition's RefreshThemeAwareElements call would have
    /// nothing to repaint back to.
    /// </summary>
    [Fact]
    public void DimStep_IsZeroOnceTheCeremonyIsTaken()
    {
        var at = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(0, DescentCountdownService.DimStepFor(at, at, migrationCompleted: true));
        Assert.Equal(0, DescentCountdownService.DimStepFor(at, at.AddDays(3), migrationCompleted: true));

        // Including BEFORE the instant, which is the odd-looking case that is nonetheless right:
        // an account cannot be migrated before its own ceremony except by migrating on another
        // device, and that account has no reason to sit in a dimmed room either.
        Assert.Equal(0, DescentCountdownService.DimStepFor(at, at.AddHours(-3), migrationCompleted: true));

        // Unmigrated is untouched — the walk is exactly what it was.
        Assert.Equal(1, DescentCountdownService.DimStepFor(at, at.AddHours(-23), migrationCompleted: false));
        Assert.Equal(4, DescentCountdownService.DimStepFor(at, at.AddHours(-3), migrationCompleted: false));
    }
}
