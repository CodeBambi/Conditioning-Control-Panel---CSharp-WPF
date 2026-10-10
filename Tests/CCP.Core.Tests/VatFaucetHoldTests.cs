using System;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE FAUCET'S HOLD, pinned — the desktop-only display layer between the shared
/// coordinator contract and the glass.
///
/// REWRITTEN for the pitch "The tap holds" (owner-approved 2026-08-30), which
/// SUPERSEDES the 2026-08-13 in-memory hold. Two rules of the old design are
/// deliberately inverted here, and their old tests are gone with them:
///   • the hold no longer needs the Profile tab to be on screen (it used to be
///     dropped entirely when it was not), and
///   • arriving at the tab no longer CLEARS the hold (ClearHeld is gone).
/// Between those two old rules there was no reachable path from "I earned XP" to
/// "I poured it": you cannot earn XP while staring at the jar, and walking to the
/// jar threw away whatever you had.
///
/// The rules now under test: held is DERIVED as today_xp minus a persisted
/// watermark, so it survives tab switches, relaunches and XP earned on another
/// client; a seed SHOWS the hold instead of clearing it and still never pours; a
/// completed CHARGE-HOLD pours the whole held amount to server truth and moves the
/// watermark; a cap retune is not an earn and re-scales the display silently with
/// the hold preserved; a negative delta (midnight) drains silently and zeroes the
/// hold; a delta landing mid-pour EXTENDS the pour rather than joining the hold;
/// and a stale watermark can never invent a hold.
///
/// XP accounting is untouched by all of this — TruthFill always tracks the last
/// accepted server fill and every path lands back on it.
/// </summary>
public class VatFaucetHoldTests
{
    /// <summary>A ledger a test can pre-load and then read back, with no settings behind it.</summary>
    private sealed class FakeLedger : IVatPourLedger
    {
        public int PouredTodayXp { get; set; }
        public int Records { get; private set; }

        public void Record(int todayXp)
        {
            PouredTodayXp = Math.Max(0, todayXp);
            Records++;
        }
    }

    private static VatRead Read(VatReadKind kind, double fill, int deltaXp, int cap, int todayXp,
                                bool scaleChanged = false, double lip = 1.20)
        => new()
        {
            Kind = kind,
            Fill = fill,
            DeltaXp = deltaXp,
            Cap = cap,
            TodayXp = todayXp,
            ScaleChanged = scaleChanged,
            Lip = lip,
        };

    /// <summary>Seeded at 40% of a 5000 cap, i.e. 2000 XP banked today and nothing waiting.</summary>
    private static VatFaucetHold Seeded(FakeLedger ledger, double fill = 0.40, int cap = 5000, int todayXp = 2000)
    {
        var hold = new VatFaucetHold(ledger);
        ledger.PouredTodayXp = todayXp;                       // everything so far is already poured
        hold.Fold(Read(VatReadKind.Seed, fill, 0, cap, todayXp), pouring: false);
        return hold;
    }

    // ------------------------------------------------------------------- seeding

    [Fact]
    public void Seed_SnapsToTruth_WhenNothingIsWaiting()
    {
        var ledger = new FakeLedger { PouredTodayXp = 2000 };
        var hold = new VatFaucetHold(ledger);

        var step = hold.Fold(Read(VatReadKind.Seed, 0.40, 0, 5000, 2000), pouring: false);

        Assert.Equal(FaucetActionKind.Snap, step.Action);
        Assert.Equal(0.40, step.Fill, 6);
        Assert.Equal(0, hold.HeldXp);
        Assert.Equal(0.40, hold.TruthFill, 6);
    }

    /// <summary>
    /// THE WHOLE POINT OF THE REWRITE. A relaunch seeds against a watermark left
    /// behind by the last pour, so XP earned since then (in a class, on the web, on
    /// the phone) is ALREADY waiting in the tap the first time the card is drawn.
    /// It is SNAPPED to, never poured: opening the card is not an earn.
    /// </summary>
    [Fact]
    public void Seed_ShowsTheHold_LeftBehindByAPreviousLaunch()
    {
        var ledger = new FakeLedger { PouredTodayXp = 2000 };   // last pour stopped here
        var hold = new VatFaucetHold(ledger);

        var step = hold.Fold(Read(VatReadKind.Seed, 0.50, 0, 5000, 2500), pouring: false);

        Assert.Equal(FaucetActionKind.Snap, step.Action);
        Assert.Equal(500, hold.HeldXp);
        Assert.Equal(0.50, hold.TruthFill, 6);
        Assert.Equal(0.40, step.Fill, 6);           // truth minus held/cap: the glass shows the hold
        Assert.Equal(0, ledger.Records);            // a seed never pours, so it never stamps
    }

    // ------------------------------------------------------------------ the hold

    /// <summary>
    /// Earned XP waits in the faucet and the glass is NOT touched: with an unchanged
    /// cap the held display value is mathematically the level already on screen.
    /// Sub-threshold trickles (the coordinator's Silent kind) hold too — the wobble
    /// has no minimum.
    /// </summary>
    [Fact]
    public void HeldXp_IsDerivedFromTodayXp_AndTheDisplayHolds()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger);

        var big = hold.Fold(Read(VatReadKind.Pour, 0.42, 100, 5000, 2100), pouring: false);
        var trickle = hold.Fold(Read(VatReadKind.Silent, 0.422, 10, 5000, 2110), pouring: false);

        Assert.Equal(FaucetActionKind.None, big.Action);
        Assert.Equal(FaucetActionKind.None, trickle.Action);
        Assert.Equal(110, hold.HeldXp);
        Assert.Equal(0.422, hold.TruthFill, 6);
        Assert.Equal(0.40, hold.DisplayFill, 6);   // truth minus held/cap = where the glass sat
    }

    /// <summary>
    /// THE TAB GATE IS GONE. There is no longer a parameter that can turn the hold
    /// off, and nothing in the fold consults visibility: the same reading holds
    /// whether or not anybody is looking at the jar. (This test is the gravestone of
    /// the old "holdActive: false drains silently" rule.)
    /// </summary>
    [Fact]
    public void TheHoldEngages_WhetherOrNotAnybodyIsWatching()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger);

        var step = hold.Fold(Read(VatReadKind.Pour, 0.44, 200, 5000, 2200), pouring: false);

        Assert.Equal(FaucetActionKind.None, step.Action);
        Assert.Equal(200, hold.HeldXp);
        Assert.Equal(0, ledger.Records);
    }

    // ------------------------------------------------------- the completed charge

    [Fact]
    public void PourAll_DrainsTheWholeHoldToTruth_AndMovesTheWatermark()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger);
        hold.Fold(Read(VatReadKind.Pour, 0.44, 200, 5000, 2200), pouring: false);

        var step = hold.PourAll();

        Assert.Equal(FaucetActionKind.Pour, step.Action);
        Assert.Equal(0.44, step.Fill, 6);          // the level rises to truth
        Assert.Equal(0, hold.HeldXp);              // the tap is empty until new XP lands
        Assert.Equal(2200, ledger.PouredTodayXp);  // and the watermark says so, persistently
    }

    /// <summary>A second charge on an empty tap pours nothing and cannot go negative.</summary>
    [Fact]
    public void PourAll_Twice_SecondOneIsEmpty()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger);
        hold.Fold(Read(VatReadKind.Pour, 0.44, 200, 5000, 2200), pouring: false);
        hold.PourAll();

        var again = hold.PourAll();

        Assert.Equal(0, hold.HeldXp);
        Assert.Equal(0.44, again.Fill, 6);
        Assert.Equal(0.44, hold.DisplayFill, 6);
    }

    // ------------------------------------------------------------- the extension

    /// <summary>
    /// A delta arriving MID-POUR extends the pour (the glass's own extend-never-
    /// restart machinery takes it from there) and never re-enters the hold — same
    /// rule as the web contract.
    /// </summary>
    [Fact]
    public void DeltaLandingMidPour_ExtendsThePour_NeverHolds()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger);
        hold.Fold(Read(VatReadKind.Pour, 0.44, 200, 5000, 2200), pouring: false);
        hold.PourAll();

        var step = hold.Fold(Read(VatReadKind.Pour, 0.46, 100, 5000, 2300), pouring: true);

        Assert.Equal(FaucetActionKind.Pour, step.Action);
        Assert.Equal(0.46, step.Fill, 6);
        Assert.Equal(0, hold.HeldXp);
        Assert.Equal(2300, ledger.PouredTodayXp);
    }

    // ------------------------------------------------------------- the cap retune

    /// <summary>
    /// A cap retune (level-up rescales cap; delta 0) is NOT earned XP: the held
    /// amount survives and only the DISPLAY re-scales — snapped silently, per the
    /// web contract's ruling, never poured.
    /// </summary>
    [Fact]
    public void CapRetune_RescalesTheDisplaySilently_AndPreservesTheHold()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger);
        hold.Fold(Read(VatReadKind.Pour, 0.42, 100, 5000, 2100), pouring: false);

        // levelled up: cap 5000 -> 8000, same XP, fill re-reported at 2100/8000
        var step = hold.Fold(Read(VatReadKind.Silent, 0.2625, 0, 8000, 2100, scaleChanged: true),
                             pouring: false);

        Assert.Equal(FaucetActionKind.Snap, step.Action);
        Assert.Equal(100, hold.HeldXp);                     // the hold is not an earn casualty
        Assert.Equal(0.2625 - 100.0 / 8000, step.Fill, 6);  // display = truth minus held at the NEW scale
    }

    // ---------------------------------------------------------- the midnight drain

    [Fact]
    public void MidnightReset_DrainsSilently_AndZeroesTheHold()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger, 0.96, 5000, 4800);
        hold.Fold(Read(VatReadKind.Pour, 0.98, 100, 5000, 4900), pouring: false);

        var step = hold.Fold(Read(VatReadKind.Silent, 0, -4900, 5000, 0), pouring: false);

        Assert.Equal(FaucetActionKind.Ease, step.Action);   // silent — never a pour DOWN
        Assert.Equal(0, step.Fill, 6);
        Assert.Equal(0, hold.HeldXp);                       // held XP of a finished day is gone
        Assert.Equal(0, ledger.PouredTodayXp);              // both numbers land on 0, as ruled
    }

    // ------------------------------------------------------------- escape valves

    /// <summary>
    /// A re-seed (vat disappeared and came back mid-day) SHOWS whatever the
    /// watermark says is still waiting. The old rule dropped it, which is exactly
    /// how a hold became unreachable.
    /// </summary>
    [Fact]
    public void Reseed_ShowsTheHold_InsteadOfDroppingIt()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger);
        hold.Fold(Read(VatReadKind.Pour, 0.42, 100, 5000, 2100), pouring: false);

        var step = hold.Fold(Read(VatReadKind.Seed, 0.42, 0, 5000, 2100), pouring: false);

        Assert.Equal(FaucetActionKind.Snap, step.Action);
        Assert.Equal(100, hold.HeldXp);
        Assert.Equal(0.42 - 100.0 / 5000, step.Fill, 6);
    }

    /// <summary>
    /// Reset() forgets the in-flight reading (vat disarmed, logout) but NOT the
    /// persisted watermark: wiping that would re-offer a pour the user already made.
    /// </summary>
    [Fact]
    public void Reset_ForgetsTheReading_ButNotTheWatermark()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger);
        hold.Fold(Read(VatReadKind.Pour, 0.42, 100, 5000, 2100), pouring: false);

        hold.Reset();
        Assert.Equal(0, hold.HeldXp);
        Assert.Equal(0, hold.TruthFill, 6);

        // the vat re-arms with the same day's numbers: the hold is still 100
        var step = hold.Fold(Read(VatReadKind.Seed, 0.42, 0, 5000, 2100), pouring: false);
        Assert.Equal(100, hold.HeldXp);
        Assert.Equal(0.42 - 100.0 / 5000, step.Fill, 6);
    }

    /// <summary>
    /// A watermark from a finished day (or plain corruption) is clamped to today's
    /// total, so the worst a wrong row can do is cost one pour animation — it can
    /// never invent a hold, and never drain the glass to a level nobody earned.
    /// </summary>
    [Fact]
    public void AStaleWatermark_CannotInventAHold()
    {
        var ledger = new FakeLedger { PouredTodayXp = 99999 };
        var hold = new VatFaucetHold(ledger);

        var step = hold.Fold(Read(VatReadKind.Seed, 0.10, 0, 5000, 500), pouring: false);

        Assert.Equal(0, hold.HeldXp);
        Assert.Equal(0.10, step.Fill, 6);
    }

    /// <summary>
    /// The display can never push the shown level BELOW zero or invent liquid —
    /// DisplayFill stays clamped at 0 and the pour still lands on truth.
    /// </summary>
    [Fact]
    public void HeldDisplay_NeverGoesNegative()
    {
        var ledger = new FakeLedger();
        var hold = new VatFaucetHold(ledger);
        hold.Fold(Read(VatReadKind.Seed, 0.01, 0, 2000, 20), pouring: false);
        hold.Fold(Read(VatReadKind.Pour, 0.01, 500, 2000, 520), pouring: false);

        Assert.Equal(0, hold.DisplayFill, 6);
        Assert.Equal(FaucetActionKind.Pour, hold.PourAll().Action);
        Assert.Equal(0.01, hold.TruthFill, 6);
    }

    /// <summary>An Ignored reading touches nothing at all — not the truth, not the hold.</summary>
    [Fact]
    public void IgnoredReading_ChangesNothing()
    {
        var ledger = new FakeLedger();
        var hold = Seeded(ledger);
        hold.Fold(Read(VatReadKind.Pour, 0.42, 100, 5000, 2100), pouring: false);

        var step = hold.Fold(Read(VatReadKind.Ignored, 0, 0, 0, 0), pouring: false);

        Assert.Equal(FaucetActionKind.None, step.Action);
        Assert.Equal(100, hold.HeldXp);
        Assert.Equal(0.42, hold.TruthFill, 6);
    }

    // --------------------------------------------------------------- the ledger

    /// <summary>
    /// The default in-memory ledger is day-scoped on the SAME UTC clock the server
    /// rolls the vat on: a watermark stamped yesterday reads as "nothing poured
    /// today", which is what makes a fresh day start with a full tap.
    /// </summary>
    [Fact]
    public void InMemoryLedger_ForgetsAcrossMidnight()
    {
        var now = new DateTime(2026, 8, 30, 23, 50, 0, DateTimeKind.Utc);
        var ledger = new InMemoryVatPourLedger { UtcNow = () => now };

        ledger.Record(4200);
        Assert.Equal(4200, ledger.PouredTodayXp);

        now = now.AddMinutes(20);                 // over the line into the 31st
        Assert.Equal(0, ledger.PouredTodayXp);
    }
}
