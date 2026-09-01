using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE SPIRAL ROOM and THE FUSE RAIL CHIP (CONTRACT-FUSE-0816 §2.2/§2.4, owner ruling 2026-08-16) —
/// the tab that replaced <c>SpiralMapWindow</c>, the countdown chip that leads to it, and the rail
/// row that only exists when there is somewhere to go.
///
/// <para><b>Why the arithmetic is pure and lives in <see cref="SpiralRoom"/>.</b> Three surfaces
/// have to agree about one question — is there anything to show — and they are painted from three
/// different files by three different events. A rail row that is visible while the tab it points at
/// has nothing in it, or a chip still counting down to a door the user already walked through, are
/// both bugs you only find on the one night this feature runs. So the deciding is one pure function
/// with every input passed in, and this suite is the truth table for it. There is no
/// <c>Application</c> and no dispatcher here, which is exactly the point.</para>
/// </summary>
public class SpiralRoomTests
{
    private static AppSettings Fresh() => new();

    // ================================================================
    // THE FUSE RAIL CHIP — visibility
    // ================================================================

    /// <summary>
    /// THE WHOLE CHIP GATE, phase by phase, for an account that has NOT taken its ceremony. The
    /// chip is the countdown made touchable, so it lives exactly as long as the countdown does:
    /// from the Whisper (the first hint anything is coming) up to but not including Zero (from
    /// which the show owns the moment).
    /// </summary>
    [Theory]
    [InlineData(DescentFusePhase.Dark, false)]      // the kill switch, and every install today
    [InlineData(DescentFusePhase.Whisper, true)]
    [InlineData(DescentFusePhase.Clock, true)]
    [InlineData(DescentFusePhase.Dimming, true)]
    [InlineData(DescentFusePhase.Candle, true)]
    [InlineData(DescentFusePhase.Vigil, true)]
    [InlineData(DescentFusePhase.Terminal, true)]
    [InlineData(DescentFusePhase.Zero, false)]      // the show has it from here
    public void ChipVisibility_FollowsThePhaseBand(DescentFusePhase phase, bool expected)
        => Assert.Equal(expected, SpiralRoom.FuseChipVisible(phase, migrationCompleted: false));

    /// <summary>
    /// A MIGRATED ACCOUNT NEVER SEES THE CHIP, at any phase. Somebody who took their ceremony on
    /// the first night has a spent fuse; a clock still counting down to a door they already walked
    /// through would be the app forgetting what they did. This is the half of the gate that the
    /// phase band cannot express, because the server's timestamp stays armed for everybody else.
    /// </summary>
    [Theory]
    [InlineData(DescentFusePhase.Whisper)]
    [InlineData(DescentFusePhase.Clock)]
    [InlineData(DescentFusePhase.Dimming)]
    [InlineData(DescentFusePhase.Candle)]
    [InlineData(DescentFusePhase.Vigil)]
    [InlineData(DescentFusePhase.Terminal)]
    public void ChipVisibility_IsPermanentlyOff_ForAMigratedAccount(DescentFusePhase phase)
        => Assert.False(SpiralRoom.FuseChipVisible(phase, migrationCompleted: true));

    /// <summary>
    /// THE DORMANCY CLAIM. Dark plus un-migrated is every install on today's server, and it is the
    /// state in which the chip must measure zero pixels and start no clock.
    /// </summary>
    [Fact]
    public void ChipVisibility_IsDarkOnAFreshInstall()
    {
        Assert.False(SpiralRoom.FuseChipVisible(DescentFusePhase.Dark, migrationCompleted: false));
        Assert.Equal(FuseWobbleTier.None, SpiralRoom.WobbleFor(DescentFusePhase.Dark));
    }

    // ================================================================
    // THE FUSE RAIL CHIP — the wobble ladder
    // ================================================================

    /// <summary>
    /// The shake escalates in four steps and never skips one. Note that every phase the chip is NOT
    /// shown at returns None: a caller cannot start a clock on a hidden element by forgetting the
    /// visibility check first.
    /// </summary>
    [Theory]
    [InlineData(DescentFusePhase.Dark, FuseWobbleTier.None)]
    [InlineData(DescentFusePhase.Whisper, FuseWobbleTier.Rare)]
    [InlineData(DescentFusePhase.Clock, FuseWobbleTier.Frequent)]
    [InlineData(DescentFusePhase.Dimming, FuseWobbleTier.Frequent)]
    [InlineData(DescentFusePhase.Candle, FuseWobbleTier.Frequent)]
    [InlineData(DescentFusePhase.Vigil, FuseWobbleTier.Tremble)]
    [InlineData(DescentFusePhase.Terminal, FuseWobbleTier.Violent)]
    [InlineData(DescentFusePhase.Zero, FuseWobbleTier.None)]
    public void Wobble_EscalatesWithThePhase(DescentFusePhase phase, FuseWobbleTier expected)
        => Assert.Equal(expected, SpiralRoom.WobbleFor(phase));

    /// <summary>The ladder never goes backwards while the chip is up — the whole point of the tiers
    /// is that the room gets less calm, never more.</summary>
    [Fact]
    public void Wobble_IsMonotonicAcrossTheVisibleBand()
    {
        var band = new[]
        {
            DescentFusePhase.Whisper, DescentFusePhase.Clock, DescentFusePhase.Dimming,
            DescentFusePhase.Candle, DescentFusePhase.Vigil, DescentFusePhase.Terminal,
        };

        for (int i = 1; i < band.Length; i++)
            Assert.True(SpiralRoom.WobbleFor(band[i]) >= SpiralRoom.WobbleFor(band[i - 1]),
                $"{band[i]} shakes less than {band[i - 1]}");
    }

    // ================================================================
    // THE FUSE RAIL CHIP — the flash-out at zero
    // ================================================================

    /// <summary>
    /// THE OWNER'S SCRIPT, 2026-08-16: "Timer goes to 0, starts flashing, and we run the cerimony."
    /// Every phase the chip was actually on screen for hands over to the flash; nothing else does.
    /// </summary>
    [Theory]
    [InlineData(DescentFusePhase.Whisper)]
    [InlineData(DescentFusePhase.Clock)]
    [InlineData(DescentFusePhase.Dimming)]
    [InlineData(DescentFusePhase.Candle)]
    [InlineData(DescentFusePhase.Vigil)]
    [InlineData(DescentFusePhase.Terminal)]
    public void FlashOut_FiresOnALiveTransitionIntoZero(DescentFusePhase previous)
        => Assert.True(SpiralRoom.ShouldFlashOutAtZero(previous, DescentFusePhase.Zero));

    /// <summary>
    /// A COLD START PAST ZERO IS SILENT. Every surface seeds itself from
    /// <see cref="DescentCountdownService.LastAnnouncedPhase"/>, which is Dark on an install whose
    /// timestamp the owner has cleared — so "Dark to Zero" is the shape of an app launched the
    /// morning after, and a rail that flickered violently for a moment the subject slept through
    /// would be the app performing an event rather than marking one.
    /// </summary>
    [Fact]
    public void FlashOut_DoesNotFireForAnAppThatLaunchedPastZero()
        => Assert.False(SpiralRoom.ShouldFlashOutAtZero(DescentFusePhase.Dark, DescentFusePhase.Zero));

    /// <summary>
    /// ZERO TO ZERO IS NOT A TRANSITION. A repaint — the migration flag landing, a settings reload,
    /// the kill switch travelling through — must not re-fire two and a half seconds of flicker on a
    /// chip that already left.
    /// </summary>
    [Fact]
    public void FlashOut_DoesNotRepeatOnARepaintAtZero()
        => Assert.False(SpiralRoom.ShouldFlashOutAtZero(DescentFusePhase.Zero, DescentFusePhase.Zero));

    /// <summary>
    /// Nothing below Zero flashes, however far the phase jumped. The chip escalating from Whisper
    /// straight to Terminal (an owner moving the date, a clock correction) is a wobble tier change
    /// and nothing more — the goodbye belongs to the instant, not to any hurry on the way to it.
    /// </summary>
    [Theory]
    [InlineData(DescentFusePhase.Whisper, DescentFusePhase.Terminal)]
    [InlineData(DescentFusePhase.Clock, DescentFusePhase.Vigil)]
    [InlineData(DescentFusePhase.Terminal, DescentFusePhase.Whisper)]  // the owner pushed the date back
    [InlineData(DescentFusePhase.Vigil, DescentFusePhase.Dark)]        // the kill switch
    public void FlashOut_OnlyEverFiresIntoZero(DescentFusePhase previous, DescentFusePhase current)
        => Assert.False(SpiralRoom.ShouldFlashOutAtZero(previous, current));

    // ================================================================
    // THE FOG ERA — the hero's heartbeat
    // ================================================================

    /// <summary>
    /// THE TEMPO LADDER TIGHTENS AND NEVER SLACKENS. The pulse under the fog's hero digits is the
    /// same instrument as the rail chip's wobble — the room gets less calm as the instant nears,
    /// never more — so the half-cycle across the visible band has to be monotonically decreasing.
    /// </summary>
    [Fact]
    public void FogPulse_TightensAcrossTheWholeBand()
    {
        var band = new[]
        {
            DescentFusePhase.Whisper, DescentFusePhase.Clock, DescentFusePhase.Dimming,
            DescentFusePhase.Candle, DescentFusePhase.Vigil, DescentFusePhase.Terminal,
        };

        for (int i = 1; i < band.Length; i++)
            Assert.True(SpiralRoom.FogPulseSecondsFor(band[i]) <= SpiralRoom.FogPulseSecondsFor(band[i - 1]),
                $"{band[i]} breathes slower than {band[i - 1]}");

        // The ladder actually moves - a flat table would pass the loop above and mean nothing.
        Assert.True(SpiralRoom.FogPulseSecondsFor(DescentFusePhase.Terminal)
                    < SpiralRoom.FogPulseSecondsFor(DescentFusePhase.Whisper) / 2);
    }

    /// <summary>
    /// THE FOG ERA OUTLIVES THE INSTANT — for anybody whose own ceremony has not reached them yet,
    /// the room is still the fog and the readout says "any moment now", the least calm sentence in
    /// the feature. The pulse must not relax at exactly that moment.
    /// </summary>
    [Fact]
    public void FogPulse_IsAsTightAtZeroAsItIsAtTerminal()
        => Assert.Equal(SpiralRoom.FogPulseSecondsFor(DescentFusePhase.Terminal),
                        SpiralRoom.FogPulseSecondsFor(DescentFusePhase.Zero));

    /// <summary>
    /// NEVER ZERO AND NEVER NEGATIVE, at any phase including the ones the fog is never painted at.
    /// The caller hands this straight to <c>TimeSpan.FromSeconds</c>, where a zero is not a fast
    /// animation but a stuck one.
    /// </summary>
    [Theory]
    [InlineData(DescentFusePhase.Dark)]
    [InlineData(DescentFusePhase.Whisper)]
    [InlineData(DescentFusePhase.Clock)]
    [InlineData(DescentFusePhase.Dimming)]
    [InlineData(DescentFusePhase.Candle)]
    [InlineData(DescentFusePhase.Vigil)]
    [InlineData(DescentFusePhase.Terminal)]
    [InlineData(DescentFusePhase.Zero)]
    public void FogPulse_IsAlwaysAUsableDuration(DescentFusePhase phase)
        => Assert.True(SpiralRoom.FogPulseSecondsFor(phase) > 0);

    // ================================================================
    // THE ROOM'S SOUND — silent unless the subject said otherwise
    // ================================================================

    /// <summary>
    /// THE MUTE IS THE MUTE. There is no separate sfx flag in this app — a master volume of zero IS
    /// silence, app-wide — and <c>DescentCountdownAudio</c> is the subject's own switch for this
    /// feature specifically. Either one alone means the room says nothing.
    /// </summary>
    [Theory]
    [InlineData(true, 40, true)]
    [InlineData(true, 0, false)]     // the app is muted
    [InlineData(false, 40, false)]   // the countdown's audio was turned off
    [InlineData(false, 0, false)]
    public void RoomSfx_NeedsBothTheSwitchAndSomeVolume(bool audioEnabled, int master, bool expected)
        => Assert.Equal(expected, DescentRoomSfx.ShouldPlay(audioEnabled, master));

    /// <summary>
    /// The gate is the same shape as the heartbeat's, minus the asset term — and it has to be, or
    /// the one switch the owner has for this feature would silence the terminal loop and leave the
    /// room chiming over it.
    /// </summary>
    [Fact]
    public void RoomSfx_AgreesWithTheHeartbeatAboutSilence()
    {
        Assert.False(DescentRoomSfx.ShouldPlay(audioEnabled: false, masterVolume: 100));
        Assert.False(DescentHeartbeat.ShouldPlay(audioEnabled: false, masterVolume: 100, assetExists: true));

        Assert.False(DescentRoomSfx.ShouldPlay(audioEnabled: true, masterVolume: 0));
        Assert.False(DescentHeartbeat.ShouldPlay(audioEnabled: true, masterVolume: 0, assetExists: true));
    }

    // ================================================================
    // THE TAB — state selection
    // ================================================================

    /// <summary>
    /// A FRESH INSTALL WITH NOTHING: no fuse, no ceremony, no block. The room is the waiting room —
    /// not the fog, because there is nothing being kept from them, and not the spiral, because
    /// there is nothing to draw. (They also have no rail row; see the persona tests below.)
    /// </summary>
    [Fact]
    public void State_FreshInstallWithNothing_IsWaiting()
        => Assert.Equal(SpiralRoomState.Waiting, SpiralRoom.StateFor(
            Fresh(), DescentFusePhase.Dark, fuseArmed: false, spiralWithheld: false, hasBlock: false));

    /// <summary>
    /// THE FUSE IS BURNING. Everybody is in the fog while the countdown runs, block or no block —
    /// a spiral drawn before the night it is given away would be the reveal spent early.
    /// </summary>
    [Theory]
    [InlineData(DescentFusePhase.Whisper)]
    [InlineData(DescentFusePhase.Clock)]
    [InlineData(DescentFusePhase.Dimming)]
    [InlineData(DescentFusePhase.Candle)]
    [InlineData(DescentFusePhase.Vigil)]
    [InlineData(DescentFusePhase.Terminal)]
    public void State_WhileTheFuseBurns_IsFog_EvenWithABlockInHand(DescentFusePhase phase)
        => Assert.Equal(SpiralRoomState.Fog, SpiralRoom.StateFor(
            Fresh(), phase, fuseArmed: true, spiralWithheld: false, hasBlock: true));

    /// <summary>
    /// THE WITHHOLD (the frozen <see cref="DescentMigrationService.SpiralWithheldFor"/>) outranks
    /// the block on its own. This is the veteran with the ceremony window on screen and their first
    /// descent block already synced — the exact race the withhold was written for.
    /// </summary>
    [Fact]
    public void State_WhenTheSpiralIsWithheld_IsFog()
        => Assert.Equal(SpiralRoomState.Fog, SpiralRoom.StateFor(
            Fresh(), DescentFusePhase.Zero, fuseArmed: true, spiralWithheld: true, hasBlock: true));

    /// <summary>
    /// ZERO PASSED, NOTHING ANSWERED, AND NO OFFER HAS ARRIVED YET. The withhold cannot see this
    /// state — it needs an offer in hand or the persisted marker, and the whole situation is that
    /// neither exists (a paused rollout dial, a sync that has not landed). Without this clause the
    /// veteran's spiral would light up in the gap between the instant and their ceremony.
    /// </summary>
    [Fact]
    public void State_AfterZeroWithNoAnswerAndNoOffer_IsStillFog()
        => Assert.Equal(SpiralRoomState.Fog, SpiralRoom.StateFor(
            Fresh(), DescentFusePhase.Zero, fuseArmed: true, spiralWithheld: false, hasBlock: true));

    /// <summary>
    /// A COMMITTED CHOICE OPENS THE ROOM IMMEDIATELY — before the server has acked anything. The
    /// pending choice is what the first-light reveal stands on when it runs a second later, and a
    /// gate that waited for the ack would hold the room shut through exactly those seconds.
    /// </summary>
    [Theory]
    [InlineData(DescentMigrationChoices.Restore)]
    [InlineData(DescentMigrationChoices.Cycle)]
    public void State_AfterACommittedChoice_IsTheSpiral(string choice)
    {
        var s = Fresh();
        s.DescentMigrationOffered = true;
        s.PendingDescentMigrationChoice = choice;

        Assert.Equal(SpiralRoomState.Spiral, SpiralRoom.StateFor(
            s, DescentFusePhase.Zero, fuseArmed: true, spiralWithheld: false, hasBlock: true));
    }

    /// <summary>
    /// THE SECONDS AFTER THE COMMIT. The choice is written but the descent record is still in
    /// flight, so the room is the waiting room — which is precisely what the first light hands back
    /// to if its twelve-second deadline expires.
    /// </summary>
    [Fact]
    public void State_CommittedButBlockStillInFlight_IsWaiting()
    {
        var s = Fresh();
        s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;

        Assert.Equal(SpiralRoomState.Waiting, SpiralRoom.StateFor(
            s, DescentFusePhase.Zero, fuseArmed: true, spiralWithheld: false, hasBlock: false));
    }

    /// <summary>
    /// A JUNK PENDING CHOICE IS NOT AN ANSWER. Same validity test the withhold and the sync path
    /// use, so a hand-edited settings file cannot walk past the ceremony by writing nonsense into
    /// the field — it lands back in the fog.
    /// </summary>
    [Fact]
    public void State_AnInvalidPendingChoice_DoesNotOpenTheRoom()
    {
        var s = Fresh();
        s.PendingDescentMigrationChoice = "whatever";

        Assert.Equal(SpiralRoomState.Fog, SpiralRoom.StateFor(
            s, DescentFusePhase.Zero, fuseArmed: true, spiralWithheld: false, hasBlock: true));
    }

    /// <summary>
    /// THE ACKED MIGRATION, and the ordinary state of the feature after launch week: the fuse has
    /// been switched off server-side (Dark, unarmed), the account is migrated, the block is in
    /// hand. That is the spiral, plainly.
    /// </summary>
    [Fact]
    public void State_AMigratedAccountWithADarkFuse_IsTheSpiral()
    {
        var s = Fresh();
        s.DescentMigrationCompleted = true;

        Assert.Equal(SpiralRoomState.Spiral, SpiralRoom.StateFor(
            s, DescentFusePhase.Dark, fuseArmed: false, spiralWithheld: false, hasBlock: true));
    }

    /// <summary>
    /// A BLOCK WITHDRAWN MID-SESSION (server dial narrowed, logout) drops a migrated account back
    /// to the waiting room rather than to a spiral drawn from a record the server has stopped
    /// serving. §2.5's leak rule, in the one place it can now be observed.
    /// </summary>
    [Fact]
    public void State_AWithdrawnBlock_FallsBackToWaiting_NotToAStaleSpiral()
    {
        var s = Fresh();
        s.DescentMigrationCompleted = true;

        Assert.Equal(SpiralRoomState.Waiting, SpiralRoom.StateFor(
            s, DescentFusePhase.Dark, fuseArmed: false, spiralWithheld: false, hasBlock: false));
    }

    /// <summary>
    /// A migrated account is not dragged back into the fog by a fuse that is still armed for
    /// everybody else. This is the "answered wins" ordering the withhold already carries, restated
    /// at the room's own door because the room has a third input the withhold does not (the phase).
    /// </summary>
    [Fact]
    public void State_AMigratedAccountIsNeverPulledBackIntoTheFogAfterZero()
    {
        var s = Fresh();
        s.DescentMigrationCompleted = true;

        Assert.Equal(SpiralRoomState.Spiral, SpiralRoom.StateFor(
            s, DescentFusePhase.Zero, fuseArmed: true, spiralWithheld: false, hasBlock: true));
    }

    /// <summary>Null settings (headless, or a settings load that failed) read as a fresh account
    /// rather than as anything withheld — the same posture every other Descent predicate takes.</summary>
    [Fact]
    public void State_NullSettings_ReadAsAFreshAccount()
        => Assert.Equal(SpiralRoomState.Waiting, SpiralRoom.StateFor(
            null, DescentFusePhase.Dark, fuseArmed: false, spiralWithheld: false, hasBlock: false));

    // ================================================================
    // THE RAIL ROW — one persona per row
    // ================================================================

    /// <summary>
    /// THE PERSONA THAT MATTERS MOST: a fuse-dark fresh account with no block sees NOTHING. No
    /// rail row, no chip. The You door measures exactly as it did before the Spiral Room existed,
    /// which is the whole safety property of shipping this dark.
    /// </summary>
    [Fact]
    public void RailRow_FuseDarkFreshAccount_SeesNothing()
    {
        var state = SpiralRoom.StateFor(Fresh(), DescentFusePhase.Dark,
            fuseArmed: false, spiralWithheld: false, hasBlock: false);

        Assert.Equal(SpiralRoomState.Waiting, state);
        Assert.False(SpiralRoom.RailEntryVisible(state));
        Assert.False(SpiralRoom.FuseChipVisible(DescentFusePhase.Dark, migrationCompleted: false));
    }

    /// <summary>During the fog era the row is there but ANONYMOUS: an ellipsis, not a name. Naming
    /// the room before the night it opens would spoil the one thing the fog is protecting.</summary>
    [Fact]
    public void RailRow_DuringTheFogEra_IsVisibleAndAnonymous()
    {
        var state = SpiralRoom.StateFor(Fresh(), DescentFusePhase.Clock,
            fuseArmed: true, spiralWithheld: false, hasBlock: false);

        Assert.Equal(SpiralRoomState.Fog, state);
        Assert.True(SpiralRoom.RailEntryVisible(state));
        Assert.True(SpiralRoom.RailEntryIsAnonymous(state));
    }

    /// <summary>Once the spiral is open the row wears its real name.</summary>
    [Fact]
    public void RailRow_InTheSpiralEra_IsVisibleAndNamed()
    {
        var s = Fresh();
        s.DescentMigrationCompleted = true;
        var state = SpiralRoom.StateFor(s, DescentFusePhase.Dark,
            fuseArmed: false, spiralWithheld: false, hasBlock: true);

        Assert.Equal(SpiralRoomState.Spiral, state);
        Assert.True(SpiralRoom.RailEntryVisible(state));
        Assert.False(SpiralRoom.RailEntryIsAnonymous(state));
    }

    /// <summary>
    /// THE WAITING ROOM HAS NO ROW. A row is a promise that there is somewhere to go, and a
    /// committed account whose block has not landed yet would find a held promise on the other side
    /// of it. The row comes back on the next BlockChanged, which is what the rail subscribes to.
    /// </summary>
    [Fact]
    public void RailRow_IsHiddenWhileTheRoomIsOnlyWaiting()
        => Assert.False(SpiralRoom.RailEntryVisible(SpiralRoomState.Waiting));

    // ================================================================
    // REGISTRATION — the tab is on the strip, and it never slides
    // ================================================================

    /// <summary>
    /// MANDATORY (the brief's word): the Spiral Room hosts a WebView2, and a native child HWND does
    /// not follow a RenderTransform. Sliding this tab would tear the browser away from its slab for
    /// the length of the transition, exactly as it would for Settings and the Graded Intake.
    /// </summary>
    [Fact]
    public void TheSpiralRoomIsAnAirspaceTab()
        => Assert.True(ChromeFxNav.IsAirspaceTab(SpiralRoom.TabKey));

    /// <summary>
    /// It is ON the strip (a -1 would mean a ghost key), and it was APPENDED rather than inserted:
    /// ChromeFxNavTests pins four indices by number, and inserting "spiral" beside "discord" — where
    /// it actually sits on the rail — would have moved three of them. The cost of appending is a
    /// slide direction computed from the wrong end of the strip, which is free here precisely
    /// because the tab never slides.
    /// </summary>
    [Fact]
    public void TheSpiralRoomIsAppendedToTheNavStrip_NotInserted()
    {
        var order = ChromeFxNav.NavOrder;
        Assert.Equal(SpiralRoom.TabKey, order[^1]);
        Assert.Equal(order.Length - 1, ChromeFxNav.IndexOf(SpiralRoom.TabKey));

        // The four pinned neighbours are exactly where ChromeFxNavTests left them.
        Assert.Equal(0, ChromeFxNav.IndexOf("settings"));
        Assert.Equal(8, ChromeFxNav.IndexOf("lab"));
        Assert.Equal(22, ChromeFxNav.IndexOf("assets"));
        Assert.Equal(23, ChromeFxNav.IndexOf("appsettings"));
    }

    // ================================================================
    // THE REDIRECT CONTRACT — the window is gone and every door leads here
    // ================================================================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ProductDir => Path.Combine(RepoRoot(), "ConditioningControlPanel");

    private static string AppFile(params string[] parts) => SourceRoots.ReadProductFile(parts);

    /// <summary>
    /// THE WINDOW IS GONE. <c>SpiralMapWindow</c> was deleted, not merely bypassed: a second door
    /// into the spiral that still compiled would be a second set of gates to keep in step with the
    /// tab's, and the whole reason the map moved was that a window could not host the reveal in the
    /// room the user is actually in.
    /// </summary>
    [Fact]
    public void TheSpiralMapWindowIsDeleted()
    {
        Assert.False(File.Exists(Path.Combine(ProductDir, "Windows", "SpiralMapWindow.cs")),
            "SpiralMapWindow.cs is back - the map is a tab now (Views/Tabs/SpiralTabView)");
    }

    /// <summary>
    /// EVERY DOOR LEADS TO THE TAB. Four call sites used to open the window; all four now go through
    /// <c>ShowTab("spiral")</c> — which is the same door the rail, the bark rules and the Ctrl+K
    /// palette use, so there is exactly one way to reach the room.
    ///
    /// <para>A source read, for the reason Phase8RedirectContractTests states: <c>ShowTab</c> is an
    /// instance method on a MainWindow that cannot be constructed off a real app, and the failure
    /// being guarded against (a redirect quietly reverted to a window) is textual anyway.</para>
    /// </summary>
    [Fact]
    public void EveryDoorIntoTheSpiralGoesThroughShowTab()
    {
        Assert.Contains("ShowTab(SpiralRoom.TabKey)",
            AppFile("Controls", "SpiralRailHost.cs"), StringComparison.Ordinal);

        Assert.Contains("ShowTab(SpiralRoom.TabKey)",
            AppFile("MainWindow", "MainWindow.ProfileSpiral.cs"), StringComparison.Ordinal);

        Assert.Contains("main.ShowTab(SpiralRoom.TabKey)",
            AppFile("Controls", "DescentFuseRailChip.cs"), StringComparison.Ordinal);

        // The first light navigates through the same door and then plays in the room.
        var director = AppFile("Services", "Descent", "DescentShowDirector.cs");
        Assert.Contains("main.BeginSpiralFirstLight()", director, StringComparison.Ordinal);

        var room = AppFile("MainWindow", "MainWindow.SpiralRoom.cs");
        Assert.Contains("ShowTab(SpiralRoom.TabKey)", room, StringComparison.Ordinal);
        Assert.Contains("BeginFirstLight()", room, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE RIG SEAM SURVIVED THE MOVE. <c>RunFirstLightReveal</c> is how the owner's capture rig
    /// films the reveal without standing up a server, a veteran account and a ceremony. It is
    /// internal, takes no arguments and skips the one-shot guard on purpose; retargeting it at the
    /// tab must not have changed any of that.
    /// </summary>
    [Fact]
    public void TheFirstLightRigSeamStillExists()
    {
        Assert.Contains("internal static void RunFirstLightReveal()",
            AppFile("Services", "Descent", "DescentShowDirector.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// THE PROFILE PLATE PULSE RETIRED. It used to point at the Trainer Card's plate before opening
    /// the map window; with the reveal playing in the room there is nothing to point at on the way
    /// past. The PLATE stays (it is a second door); only the animation is gone.
    ///
    /// <para>The declaration and the call are what is asserted, not the bare identifier: the
    /// tombstone comments that explain WHY it went name it, and a scan that could pass or fail on
    /// prose is a scan that eventually gets a comment rewritten to make it green.</para>
    /// </summary>
    [Fact]
    public void TheProfilePlatePulseIsRetired()
    {
        Assert.DoesNotContain("void PlayFirstLightHighlight",
            AppFile("MainWindow", "MainWindow.ProfileSpiral.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("PlayFirstLightHighlight(",
            AppFile("Services", "Descent", "DescentShowDirector.cs"), StringComparison.Ordinal);

        // The plate and its gates are untouched.
        Assert.Contains("RefreshProfileSpiralPlate",
            AppFile("MainWindow", "MainWindow.ProfileSpiral.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// THE TAB IS REGISTERED EVERYWHERE IT HAS TO BE. Nine touchpoints, and missing any one of them
    /// fails silently in a different way: no view means a blank page, no collapse means two tabs
    /// stacked, no door row means the active indicator lands nowhere.
    /// </summary>
    [Fact]
    public void TheSpiralRoomIsRegisteredAtEveryTouchpoint()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        Assert.Contains("<views:SpiralTabView x:Name=\"SpiralTab\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<controls:DescentFuseRailChip x:Name=\"FuseRailChip\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BtnNavSpiral\"", xaml, StringComparison.Ordinal);

        var nav = AppFile("MainWindow", "MainWindow.TabNavigation.cs");
        Assert.Contains("SpiralTab.Visibility = Visibility.Collapsed", nav, StringComparison.Ordinal);
        Assert.Contains("case \"spiral\":", nav, StringComparison.Ordinal);
        Assert.Contains("SpiralTab.OnTabShown()", nav, StringComparison.Ordinal);
        // The You door owns it, right after the profile.
        Assert.Contains("\"discord\", \"spiral\"", nav, StringComparison.Ordinal);

        Assert.Contains("\"spiral\" => BtnNavSpiral",
            AppFile("MainWindow", "MainWindow.ChromeFx.cs"), StringComparison.Ordinal);
        Assert.Contains("BtnNavSpiral",
            AppFile("Services", "TutorialService.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// THE CHIP IS NOT A DOOR MEDALLION, and NavRailFlyoutTests depends on it: that test scrapes
    /// MainWindow.xaml for the 40px door Viewbox and asserts exactly eight of them. A chip authored
    /// in markup with a medallion's shape would make it nine, and the failure would read as the door
    /// art being wrong rather than as this control existing.
    /// </summary>
    [Fact]
    public void TheFuseChipContributesNoDoorMedallionMarkup()
    {
        var chip = AppFile("Controls", "DescentFuseRailChip.cs");
        Assert.DoesNotContain("<Viewbox Grid.Column=\"0\" Width=\"40\"", chip, StringComparison.Ordinal);

        // ...and it is code-built, so there is no .xaml beside it to grow one later.
        Assert.False(File.Exists(Path.Combine(ProductDir, "Controls", "DescentFuseRailChip.xaml")));
    }

    /// <summary>
    /// The room's name is localized in all nine shipped languages, and the fuse's own copy is NOT —
    /// the countdown speaks hardcoded English by contract (§4), so a loc key for "something is
    /// coming" would be the debt being paid in the wrong place.
    /// </summary>
    [Fact]
    public void TheRoomIsNamedInEveryLanguage()
    {
        var dir = SourceRoots.LanguagesDirectory;
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(9, files.Length);

        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            Assert.Contains("\"tab_spiral\"", text, StringComparison.Ordinal);
            Assert.Contains("\"tooltip_spiral_room\"", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// THE AIRSPACE STRATEGY, pinned where it can rot: the tab must never construct a WebView2 while
    /// the fog or the reveal is up. A <c>SpiralEmbedView</c> built in the constructor, or kept alive
    /// across a state change, would paint over both — a native child HWND ignores z-order entirely.
    /// </summary>
    [Fact]
    public void TheEmbedIsBuiltLazilyAndTornDownWithTheTab()
    {
        var view = AppFile("Views", "Tabs", "SpiralTabView.xaml.cs");

        // Built in exactly one place, and that place is not the constructor.
        Assert.Contains("new SpiralEmbedView(\"map\")", view, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(view, "new SpiralEmbedView("));

        // Both the fog and the reveal tear it down before they paint.
        Assert.Contains("TeardownEmbed", view, StringComparison.Ordinal);
        Assert.Contains("IsVisibleChanged", view, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE AIRSPACE STRATEGY, ONE LAYER DOWN — the splash's whole reason to work.
    ///
    /// <para>A constructed WebView2 paints its own dark slab the instant its native HWND exists,
    /// over every WPF element in the window whatever the z-order says. So a splash drawn "on top of"
    /// a loading browser would be invisible for all but the first few hundred milliseconds, which is
    /// precisely the stretch the owner reported as looking stuck. The fix is that the embed's slab
    /// is parked at <c>Visibility.Hidden</c> — ARRANGED, so the browser gets a real rectangle and
    /// navigates normally, but with no airspace — and is only promoted to Visible from the embed's
    /// own <c>Navigated</c> event.</para>
    ///
    /// <para><b>Collapsed would NOT do</b>: an unarranged WebView2 has no rectangle to lay a page
    /// out in, and this test exists so a later tidy-up does not "simplify" Hidden into Collapsed and
    /// silently break loading for everybody whose route actually deploys.</para>
    /// </summary>
    [Fact]
    public void TheSplashHoldsTheBrowsersAirspaceUntilItHasNavigated()
    {
        var view = AppFile("Views", "Tabs", "SpiralTabView.xaml.cs");

        Assert.Contains("EmbedHost.Visibility = Visibility.Hidden", view, StringComparison.Ordinal);
        Assert.Contains("embed.Navigated +=", view, StringComparison.Ordinal);
        Assert.Contains("ShowSplash()", view, StringComparison.Ordinal);

        // The embed raises it, and only on a SUCCESSFUL navigation - a failure still goes to Fail.
        var embed = AppFile("Controls", "SpiralEmbedView.cs");
        Assert.Contains("public event EventHandler? Navigated;", embed, StringComparison.Ordinal);
        Assert.Contains("Navigated?.Invoke(this, EventArgs.Empty)", embed, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE SPLASH CANNOT BECOME THE HANG IT WAS BUILT TO HIDE. Holding the browser's airspace back
    /// means the room is betting on an event arriving; the deadline is the bet's stop-loss, and
    /// without it a wedged renderer would leave a spinning glyph up forever with a perfectly good
    /// spiral behind it.
    /// </summary>
    [Fact]
    public void TheSplashHasADeadline()
    {
        var view = AppFile("Views", "Tabs", "SpiralTabView.xaml.cs");
        Assert.Contains("SplashRevealDeadline", view, StringComparison.Ordinal);
        Assert.Contains("OnSplashDeadline", view, StringComparison.Ordinal);
    }

    /// <summary>
    /// ACCENT IS UNTOUCHABLE (DescentFuseChrome). The fuse is the APP's own gold and never the
    /// active mod's pink — a countdown that took the skin's colour would look like part of whatever
    /// mod happened to be loaded rather than like the one thing every install is counting down to.
    ///
    /// <para>The tab's ONE legal accent read is in code, not markup: the spiral canvas takes the
    /// mod's colour because it colours the SPIRAL. So the rule this pins is that the fog era's
    /// MARKUP — every element the countdown is made of — carries literal gold and reaches for no
    /// COLOUR resource at all.</para>
    ///
    /// <para>This used to ban the string "DynamicResource" outright, which was a cheap stand-in
    /// for "no colour lookups". It cannot stay that broad: the countdown digits are monospace, and
    /// the app's monospace face now has to come from the swappable <c>Font.Mono</c> resource so
    /// FontGuard can strike out a corrupt Cascadia install (v6.8.6 rendered every panel blank
    /// because a hardcoded face threw from the layout pass). A font lookup is not an accent, so
    /// the rule is now stated the way it was always meant: every resource this markup reaches for
    /// must be a FONT, never a brush or a colour.</para>
    /// </summary>
    [Fact]
    public void TheFogEraWearsTheFusesOwnGoldAndNeverTheModAccent()
    {
        var xaml = AppFile("Views", "Tabs", "SpiralTabView.xaml");

        Assert.Contains("#E0B052", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PinkBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AccentBrush", xaml, StringComparison.Ordinal);

        var colourLookups = System.Text.RegularExpressions.Regex
            .Matches(xaml, @"\{(?:Dynamic|Static)Resource\s+([^}]+)\}")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(key => !key.StartsWith("Font.", StringComparison.Ordinal))
            .ToList();

        Assert.True(colourLookups.Count == 0,
            "the fog era must carry literal gold and look up nothing but fonts, but this markup " +
            "reaches for: " + string.Join(", ", colourLookups));
    }

    /// <summary>
    /// THE FUSE SPEAKS HARDCODED ENGLISH (§4), and the FX pass did not quietly change that. The
    /// splash's line is a const beside the view that draws it, in the same register as the waiting
    /// room's — a loc key for it would be the localization debt being paid in the wrong place, one
    /// string at a time, in the file least likely to be found by the pass that eventually pays it.
    /// </summary>
    [Fact]
    public void TheSplashCopyIsNotLocalized()
    {
        var view = AppFile("Views", "Tabs", "SpiralTabView.xaml.cs");
        Assert.Contains("private const string SplashCopy", view, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalizationManager", view, StringComparison.Ordinal);
    }

    private static int CountOf(string haystack, string needle)
    {
        int n = 0, at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { n++; at += needle.Length; }
        return n;
    }
}
