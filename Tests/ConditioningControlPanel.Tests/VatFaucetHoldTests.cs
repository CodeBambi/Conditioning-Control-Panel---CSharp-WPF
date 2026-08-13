using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE FAUCET'S HOLD, pinned — the desktop-only display layer between the shared
/// coordinator contract and the glass (owner survey 2026-08-13). The rules under
/// test: earned XP HOLDS (any amount, no minimum) while the Profile tab is on
/// screen; the click pours the WHOLE held delta to server truth; a cap retune is
/// not an earn and re-scales the display silently with the hold preserved; a
/// negative delta (midnight) drains silently and clears the hold; a delta landing
/// mid-pour EXTENDS the pour rather than joining the hold; and with the tab off
/// screen the hold never engages (pre-faucet behavior, silently).
///
/// XP accounting is untouched by all of this — TruthFill always tracks the last
/// accepted server fill and every path lands back on it.
/// </summary>
public class VatFaucetHoldTests
{
    private static VatRead Read(VatReadKind kind, double fill, int deltaXp, int cap,
                                bool scaleChanged = false, double lip = 1.20)
        => new()
        {
            Kind = kind,
            Fill = fill,
            DeltaXp = deltaXp,
            Cap = cap,
            ScaleChanged = scaleChanged,
            Lip = lip,
        };

    private static VatFaucetHold Seeded(double fill = 0.40, int cap = 5000)
    {
        var hold = new VatFaucetHold();
        hold.Fold(Read(VatReadKind.Seed, fill, 0, cap), holdActive: true, pouring: false);
        return hold;
    }

    // ------------------------------------------------------------------- seeding

    [Fact]
    public void Seed_SnapsToTruth_AndCarriesNoHold()
    {
        var hold = new VatFaucetHold();
        var step = hold.Fold(Read(VatReadKind.Seed, 0.40, 0, 5000), holdActive: true, pouring: false);

        Assert.Equal(FaucetActionKind.Snap, step.Action);
        Assert.Equal(0.40, step.Fill, 6);
        Assert.Equal(0, hold.HeldXp);
        Assert.Equal(0.40, hold.TruthFill, 6);
    }

    // ------------------------------------------------------------------ the hold

    /// <summary>
    /// Earned XP accumulates in the faucet and the glass is NOT touched: with an
    /// unchanged cap the held display value is mathematically the level already on
    /// screen. Sub-threshold trickles (the coordinator's Silent kind) hold too —
    /// the wobble has no minimum.
    /// </summary>
    [Fact]
    public void HeldXp_Accumulates_AndTheDisplayHolds()
    {
        var hold = Seeded(0.40, 5000);

        var big = hold.Fold(Read(VatReadKind.Pour, 0.42, 100, 5000), holdActive: true, pouring: false);
        var trickle = hold.Fold(Read(VatReadKind.Silent, 0.422, 10, 5000), holdActive: true, pouring: false);

        Assert.Equal(FaucetActionKind.None, big.Action);
        Assert.Equal(FaucetActionKind.None, trickle.Action);
        Assert.Equal(110, hold.HeldXp);
        Assert.Equal(0.422, hold.TruthFill, 6);
        Assert.Equal(0.40, hold.DisplayFill, 6);   // truth minus held/cap = where the glass sat
    }

    // ----------------------------------------------------------------- the click

    [Fact]
    public void PourAll_DrainsTheWholeHoldToTruth()
    {
        var hold = Seeded(0.40, 5000);
        hold.Fold(Read(VatReadKind.Pour, 0.44, 200, 5000), holdActive: true, pouring: false);

        var step = hold.PourAll();

        Assert.Equal(FaucetActionKind.Pour, step.Action);
        Assert.Equal(0.44, step.Fill, 6);          // the level rises to truth
        Assert.Equal(0, hold.HeldXp);              // wobble stops until new XP accrues
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
        var hold = Seeded(0.40, 5000);
        hold.Fold(Read(VatReadKind.Pour, 0.44, 200, 5000), holdActive: true, pouring: false);
        hold.PourAll();

        var step = hold.Fold(Read(VatReadKind.Pour, 0.46, 100, 5000), holdActive: true, pouring: true);

        Assert.Equal(FaucetActionKind.Pour, step.Action);
        Assert.Equal(0.46, step.Fill, 6);
        Assert.Equal(0, hold.HeldXp);
    }

    // ------------------------------------------------------------- the cap retune

    /// <summary>
    /// A cap retune (level-up rescales cap; delta 0) is NOT earned XP: the held
    /// amount survives and only the DISPLAY re-scales — snapped silently, per the
    /// web contract's ruling, never poured and never wobble-worthy on its own.
    /// </summary>
    [Fact]
    public void CapRetune_RescalesTheDisplaySilently_AndPreservesTheHold()
    {
        var hold = Seeded(0.40, 5000);
        hold.Fold(Read(VatReadKind.Pour, 0.42, 100, 5000), holdActive: true, pouring: false);

        // levelled up: cap 5000 -> 8000, same XP, fill re-reported at 2100/8000
        var step = hold.Fold(Read(VatReadKind.Silent, 0.2625, 0, 8000, scaleChanged: true),
                             holdActive: true, pouring: false);

        Assert.Equal(FaucetActionKind.Snap, step.Action);
        Assert.Equal(100, hold.HeldXp);                     // the hold is not an earn casualty
        Assert.Equal(0.2625 - 100.0 / 8000, step.Fill, 6);  // display = truth minus held at the NEW scale
    }

    // ---------------------------------------------------------- the midnight drain

    [Fact]
    public void MidnightReset_DrainsSilently_AndClearsTheHold()
    {
        var hold = Seeded(0.96, 5000);
        hold.Fold(Read(VatReadKind.Pour, 0.98, 100, 5000), holdActive: true, pouring: false);

        var step = hold.Fold(Read(VatReadKind.Silent, 0, -4900, 5000), holdActive: true, pouring: false);

        Assert.Equal(FaucetActionKind.Ease, step.Action);   // silent — never a pour DOWN
        Assert.Equal(0, step.Fill, 6);
        Assert.Equal(0, hold.HeldXp);                       // held XP of a finished day is gone
    }

    // ------------------------------------------------------------ hold not active

    /// <summary>
    /// Off the Profile tab the hold never engages: the reading applies silently
    /// (the pre-faucet host behavior for a pour nobody is there for).
    /// </summary>
    [Fact]
    public void WithTheTabOffScreen_ReadingsApplySilently_NothingHolds()
    {
        var hold = Seeded(0.40, 5000);

        var step = hold.Fold(Read(VatReadKind.Pour, 0.44, 200, 5000), holdActive: false, pouring: false);

        Assert.Equal(FaucetActionKind.Ease, step.Action);
        Assert.Equal(0.44, step.Fill, 6);
        Assert.Equal(0, hold.HeldXp);
    }

    // ------------------------------------------------------------- escape valves

    /// <summary>Tab re-entry: ClearHeld drops the hold so the glass can snap to truth.</summary>
    [Fact]
    public void ClearHeld_IsTheTabEntryValve_TruthRemains()
    {
        var hold = Seeded(0.40, 5000);
        hold.Fold(Read(VatReadKind.Pour, 0.42, 100, 5000), holdActive: true, pouring: false);

        hold.ClearHeld();

        Assert.Equal(0, hold.HeldXp);
        Assert.Equal(0.42, hold.TruthFill, 6);   // where the caller snaps the glass
        Assert.Equal(0.42, hold.DisplayFill, 6);
    }

    /// <summary>A re-seed (vat disappeared and came back) never carries a stale hold.</summary>
    [Fact]
    public void Reseed_DropsAnyStaleHold()
    {
        var hold = Seeded(0.40, 5000);
        hold.Fold(Read(VatReadKind.Pour, 0.42, 100, 5000), holdActive: true, pouring: false);

        var step = hold.Fold(Read(VatReadKind.Seed, 0.55, 0, 5000), holdActive: true, pouring: false);

        Assert.Equal(FaucetActionKind.Snap, step.Action);
        Assert.Equal(0.55, step.Fill, 6);
        Assert.Equal(0, hold.HeldXp);
    }

    /// <summary>
    /// Clamped at the lip: truth cannot rise for a real earn, so the held display
    /// must never push the shown level BELOW zero or invent liquid — DisplayFill
    /// stays clamped at 0 and the pour still lands on truth.
    /// </summary>
    [Fact]
    public void HeldDisplay_NeverGoesNegative()
    {
        var hold = Seeded(0.01, 2000);
        hold.Fold(Read(VatReadKind.Pour, 0.01, 500, 2000), holdActive: true, pouring: false);

        Assert.Equal(0, hold.DisplayFill, 6);
        Assert.Equal(FaucetActionKind.Pour, hold.PourAll().Action);
        Assert.Equal(0.01, hold.TruthFill, 6);
    }
}
