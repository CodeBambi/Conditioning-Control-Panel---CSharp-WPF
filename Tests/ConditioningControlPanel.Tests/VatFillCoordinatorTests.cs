using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE VAT'S ANIMATION CONTRACT, pinned — the half that three clients have to
/// agree on (planning/one-descent PLAN.md, "SHARED ANIM CONTRACT" 2026-08-12):
/// pour only when the XP delta clears VatPourMinXp, ease silently below it, never
/// pour a drop, and never invent a fill for an account the server sent no vat for.
///
/// If the threshold is ever retuned, these numbers move with it on desktop, mobile
/// and web together. Three clients disagreeing about when the faucet appears is a
/// bug report nobody can reproduce.
/// </summary>
public class VatFillCoordinatorTests
{
    private static DescentBlock? BlockWith(int cap, int todayXp, double fillPct, double lipPct = 120)
        => DescentReader.Parse(DescentReader.ParseWire(
            $"{{ \"devotion_days\": 10, \"vat\": {{ \"cap\": {cap}, \"today_xp\": {todayXp}, " +
            $"\"fill_pct\": {fillPct}, \"fill_lip_pct\": {lipPct} }} }}"));

    // ------------------------------------------------------------- the threshold

    /// <summary>
    /// 1% of the daily cap, floored at 25 XP. The floor is what keeps the faucet
    /// meaningful for a low-level account whose 1% is a rounding error.
    /// </summary>
    [Theory]
    [InlineData(100, 25)]
    [InlineData(2000, 25)]
    [InlineData(2500, 25)]
    [InlineData(2501, 26)]
    [InlineData(5200, 52)]
    [InlineData(43000, 430)]
    public void VatPourMinXp_IsOnePercentOfCapWithA25Floor(int cap, int expected)
        => Assert.Equal(expected, VatFillCoordinator.VatPourMinXp(cap));

    // ----------------------------------------------------------------- tri-state

    [Fact]
    public void NoBlock_IsIgnoredAndDrawsNothing()
    {
        var c = new VatFillCoordinator();
        var read = c.Apply(null);
        Assert.Equal(VatReadKind.Ignored, read.Kind);
        Assert.Null(c.LastFill);
    }

    [Fact]
    public void BlockWithNoVat_IsIgnored()
    {
        var c = new VatFillCoordinator();
        var block = DescentReader.Parse(DescentReader.ParseWire("{ \"devotion_days\": 10 }"));
        Assert.Equal(VatReadKind.Ignored, c.Apply(block).Kind);
        Assert.Null(c.LastFill);
    }

    /// <summary>
    /// The vat disappearing (rollout dial narrowed, logout) must RESET rather than
    /// leave a stale baseline behind: the next block has to seed, not pour a delta
    /// measured against a meter nobody was watching.
    /// </summary>
    [Fact]
    public void VatDisappearing_ResetsSoTheNextBlockSeeds()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 2000, 40));
        c.Apply(null);
        Assert.Null(c.LastFill);

        var back = c.Apply(BlockWith(5000, 4000, 80));
        Assert.Equal(VatReadKind.Seed, back.Kind);
        Assert.Equal(0.80, back.Fill, 6);
    }

    // ------------------------------------------------------------ the first read

    /// <summary>
    /// Opening the Trainer Card is not an earn. The first read renders last-known
    /// server fill; a faucet here would announce XP banked hours ago, every time.
    /// </summary>
    [Fact]
    public void FirstRead_SeedsAndNeverPours()
    {
        var c = new VatFillCoordinator();
        var read = c.Apply(BlockWith(5000, 4900, 98));
        Assert.Equal(VatReadKind.Seed, read.Kind);
        Assert.Equal(0.98, read.Fill, 6);
        Assert.Equal(1.20, read.Lip, 6);
    }

    // ----------------------------------------------------------------- the delta

    [Fact]
    public void UnchangedReading_IsIgnored()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 2000, 40));
        Assert.Equal(VatReadKind.Ignored, c.Apply(BlockWith(5000, 2000, 40)).Kind);
    }

    [Fact]
    public void DeltaAtTheThreshold_Pours()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 2000, 40));              // threshold = 50
        var read = c.Apply(BlockWith(5000, 2050, 41));
        Assert.Equal(VatReadKind.Pour, read.Kind);
        Assert.Equal(50, read.DeltaXp);
        Assert.Equal(0.41, read.Fill, 6);
    }

    [Fact]
    public void DeltaBelowTheThreshold_EasesSilently()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 2000, 40));              // threshold = 50
        var read = c.Apply(BlockWith(5000, 2049, 40.98));
        Assert.Equal(VatReadKind.Silent, read.Kind);
        Assert.Equal(49, read.DeltaXp);
    }

    /// <summary>
    /// LICENSE TO RUN OVER (contract ruling 2026-08-12, program-wide). A qualifying
    /// earn POURS even when the liquid cannot move because the vat is already parked
    /// at the ceiling — the XP delta is the whole test, and the overflow and spill
    /// FX (driven by the LEVEL, not by this decision) are what carry it. The
    /// alternative is that the biggest earns of a deep day are the only ones the
    /// meter never acknowledges.
    /// </summary>
    [Fact]
    public void BigDeltaThatMovesNoLiquid_StillPours()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 9000, 400));             // clamped to the brim
        var read = c.Apply(BlockWith(5000, 20000, 400));
        Assert.Equal(VatReadKind.Pour, read.Kind);
        Assert.Equal(11000, read.DeltaXp);
        Assert.Equal(c.LastFill!.Value, read.Fill, 6);   // the level did not move
    }

    /// <summary>
    /// The other half of the same ruling: a delta UNDER the threshold at the ceiling
    /// is still not a pour. Running over is a license for qualifying earns, not a
    /// blanket one.
    /// </summary>
    [Fact]
    public void SmallDeltaAtTheCeiling_DoesNotPour()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 9000, 400));             // threshold = 50
        var read = c.Apply(BlockWith(5000, 9040, 400));
        Assert.NotEqual(VatReadKind.Pour, read.Kind);
    }

    /// <summary>
    /// The drop at UTC midnight is deliberately silent: a faucet pouring a vat DOWN
    /// is a lie about which direction the day went.
    /// </summary>
    [Fact]
    public void DayRollover_DrainsSilently()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 4800, 96));
        var read = c.Apply(BlockWith(5000, 0, 0));
        Assert.Equal(VatReadKind.Silent, read.Kind);
        Assert.Equal(-4800, read.DeltaXp);
        Assert.Equal(0, read.Fill);
    }

    [Fact]
    public void ThresholdFollowsTheCap_SoTheSameGrantReadsDifferentlyAtDepth()
    {
        var shallow = new VatFillCoordinator();
        shallow.Apply(BlockWith(2000, 0, 0));            // threshold = 25
        Assert.Equal(VatReadKind.Pour, shallow.Apply(BlockWith(2000, 40, 2)).Kind);

        var deep = new VatFillCoordinator();
        deep.Apply(BlockWith(20000, 0, 0));              // threshold = 200
        Assert.Equal(VatReadKind.Silent, deep.Apply(BlockWith(20000, 40, 0.2)).Kind);
    }

    // ------------------------------------------------------------------ the scale

    [Fact]
    public void LipIsCarriedThroughAsTheMetersScale()
    {
        var c = new VatFillCoordinator();
        var read = c.Apply(BlockWith(5000, 2000, 40, lipPct: 130));
        Assert.Equal(1.30, read.Lip, 6);
    }

    [Fact]
    public void ACapChange_FlagsAScaleChangeAndIsNotSilentlyIgnored()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 2000, 40));
        var read = c.Apply(BlockWith(6000, 2000, 40));   // levelled up: new cap, same XP
        Assert.True(read.ScaleChanged);
        Assert.Equal(VatReadKind.Silent, read.Kind);
    }

    [Fact]
    public void ALipChange_FlagsAScaleChange()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 2000, 40, lipPct: 120));
        var read = c.Apply(BlockWith(5000, 2000, 40, lipPct: 125));   // stage 4 perk landed
        Assert.True(read.ScaleChanged);
    }

    /// <summary>
    /// A CAP CHANGE WITH NO EARN IS NEVER A POUR. Levelling up mid-session re-scales
    /// the meter under the same XP; the host snaps to the new fraction (see
    /// VatRead.ScaleChanged) and the faucet stays out of it — it has nothing to
    /// announce.
    /// </summary>
    [Fact]
    public void ACapChangeWithNoEarn_NeverPours()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 4000, 80));
        var read = c.Apply(BlockWith(9000, 4000, 44.4));   // bigger cap, same XP
        Assert.Equal(VatReadKind.Silent, read.Kind);
        Assert.True(read.ScaleChanged);
        Assert.Equal(0, read.DeltaXp);
    }

    /// <summary>
    /// The other side of it: a cap change that arrives in the SAME reading as a
    /// qualifying earn is still an earn. The re-scale rule exists to stop a silent
    /// level-up drawing a drain, not to swallow XP.
    /// </summary>
    [Fact]
    public void ACapChangeCarryingAQualifyingEarn_StillPours()
    {
        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 4000, 80));
        var read = c.Apply(BlockWith(9000, 4500, 50));     // levelled up AND earned 500
        Assert.Equal(VatReadKind.Pour, read.Kind);
        Assert.True(read.ScaleChanged);
    }

    // ------------------------------------------------------------- the extension

    /// <summary>
    /// THE RETRACT WINDOW. The faucet leaves 2.1s after the LAST qualifying delta —
    /// every pour re-arms the full window inside VatGlassCanvas (<c>_pourT =
    /// PourSeconds</c>) with the slide left where it is, so a burst of earns is one
    /// unbroken pour rather than a faucet that swings in and out per grant.
    ///
    /// The canvas's own timing needs a live STA render host and a machine-dependent
    /// MotionFx level, so what is pinned here is the constant the whole contract is
    /// written against and the fact that back-to-back qualifying deltas each answer
    /// Pour — which is what re-arms it.
    /// </summary>
    [Fact]
    public void ConsecutiveQualifyingDeltas_EachPour_AndTheWindowIs2Point1s()
    {
        Assert.Equal(2.1, ConditioningControlPanel.Controls.VatGlassCanvas.PourSeconds, 6);

        var c = new VatFillCoordinator();
        c.Apply(BlockWith(5000, 1000, 20));                // threshold = 50
        Assert.Equal(VatReadKind.Pour, c.Apply(BlockWith(5000, 1100, 22)).Kind);
        Assert.Equal(VatReadKind.Pour, c.Apply(BlockWith(5000, 1200, 24)).Kind);
    }
}
