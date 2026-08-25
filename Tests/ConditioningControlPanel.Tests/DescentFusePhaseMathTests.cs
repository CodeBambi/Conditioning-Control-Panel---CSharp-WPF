using System;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE FUSE's clock, as arithmetic (CONTRACT-FUSE-0816 §2.1/§2.2).
///
/// <para><see cref="DescentCountdownService.PhaseFor"/> and
/// <see cref="DescentCountdownService.DimStepFor"/> are deliberately pure — no App, no settings, no
/// timer, no <c>DateTime.UtcNow</c> — precisely so the boundaries can be pinned here instead of
/// being discovered by a user at 3am on the night of the ceremony. Every surface in the feature
/// keys off the enum these two produce, so this file is the whole behavioural contract of the
/// countdown's visible half.</para>
///
/// <para>The one thing these tests cannot cover is the timer's cadence, which needs a dispatcher.
/// It is a single derived boolean (<c>remaining &lt;= 1h ⇒ 1s</c>) sitting next to the Vigil
/// boundary that IS pinned below.</para>
/// </summary>
public class DescentFusePhaseMathTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime At(TimeSpan outFromNow) => Now + outFromNow;

    // ------------------------------------------------------------ the dark state

    /// <summary>
    /// NO TIMESTAMP, NO FEATURE. This is the dormancy claim the whole contract rests on: with
    /// nothing cached there is no phase, which means no spark, no dimming, no candle and no timer.
    /// </summary>
    [Fact]
    public void NoTimestamp_IsDark()
    {
        Assert.Equal(DescentFusePhase.Dark, DescentCountdownService.PhaseFor(null, Now));
        Assert.Equal(0, DescentCountdownService.DimStepFor(null, Now));
    }

    /// <summary>
    /// More than seven days out is ALSO dark. The fuse being armed months early must look exactly
    /// like the fuse not existing — otherwise the owner cannot set the date until the last week.
    /// </summary>
    [Theory]
    [InlineData(8)]
    [InlineData(30)]
    [InlineData(365)]
    public void FarFuture_IsDark(int days)
    {
        Assert.Equal(DescentFusePhase.Dark,
            DescentCountdownService.PhaseFor(At(TimeSpan.FromDays(days)), Now));
    }

    // ------------------------------------------------------------ the ladder

    /// <summary>
    /// Every phase, sampled comfortably inside its band. Read top to bottom this is the contract's
    /// §2.1 list, in order.
    /// </summary>
    [Theory]
    [InlineData(6.5 * 24,   DescentFusePhase.Whisper)]    // ≤7d
    [InlineData(80,         DescentFusePhase.Whisper)]    // still >72h
    [InlineData(48,         DescentFusePhase.Clock)]      // ≤72h
    [InlineData(25,         DescentFusePhase.Clock)]      // still >24h
    [InlineData(20,         DescentFusePhase.Dimming)]    // ≤24h
    [InlineData(13,         DescentFusePhase.Dimming)]    // still >12h
    [InlineData(6,          DescentFusePhase.Candle)]     // ≤12h
    [InlineData(1.5,        DescentFusePhase.Candle)]     // still >1h
    [InlineData(0.5,        DescentFusePhase.Vigil)]      // ≤1h
    [InlineData(0.25,       DescentFusePhase.Vigil)]      // still >10m
    [InlineData(0.05,       DescentFusePhase.Terminal)]   // ≤10m (3 minutes)
    public void PhaseFor_WalksTheLadder(double hoursOut, DescentFusePhase expected)
    {
        Assert.Equal(expected, DescentCountdownService.PhaseFor(At(TimeSpan.FromHours(hoursOut)), Now));
    }

    /// <summary>
    /// Boundaries are inclusive on the NEAR side: standing exactly on 72h is Clock, not Whisper.
    /// Pinned because an off-by-one here is invisible in review and produces a phase that flickers
    /// between two states for one tick at every single boundary.
    /// </summary>
    [Theory]
    [InlineData(7 * 24,  DescentFusePhase.Whisper)]
    [InlineData(72,      DescentFusePhase.Clock)]
    [InlineData(24,      DescentFusePhase.Dimming)]
    [InlineData(12,      DescentFusePhase.Candle)]
    [InlineData(1,       DescentFusePhase.Vigil)]
    public void PhaseFor_BoundariesBelongToTheNearerPhase(double hoursOut, DescentFusePhase expected)
    {
        Assert.Equal(expected, DescentCountdownService.PhaseFor(At(TimeSpan.FromHours(hoursOut)), Now));
    }

    /// <summary>Exactly ten minutes is Terminal, the last phase with a voice.</summary>
    [Fact]
    public void TenMinutesExactly_IsTerminal()
    {
        Assert.Equal(DescentFusePhase.Terminal,
            DescentCountdownService.PhaseFor(At(TimeSpan.FromMinutes(10)), Now));
    }

    /// <summary>
    /// The instant itself, and everything after it, is Zero. A countdown that rendered a negative
    /// T-minus for a user who left the app open would be the ugliest possible ending.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100000)]
    public void AtAndAfterTheInstant_IsZero(double secondsOut)
    {
        Assert.Equal(DescentFusePhase.Zero,
            DescentCountdownService.PhaseFor(At(TimeSpan.FromSeconds(secondsOut)), Now));
    }

    /// <summary>
    /// The enum's ORDER is load-bearing: every surface asks "phase &gt;= X" because that is how the
    /// contract phrases its rules, and Dark must sort below everything so the same comparison also
    /// answers "is anything showing". Reordering this enum silently rewrites every surface's gate.
    /// </summary>
    [Fact]
    public void PhaseOrdering_IsFarToNear()
    {
        Assert.True(DescentFusePhase.Dark < DescentFusePhase.Whisper);
        Assert.True(DescentFusePhase.Whisper < DescentFusePhase.Clock);
        Assert.True(DescentFusePhase.Clock < DescentFusePhase.Dimming);
        Assert.True(DescentFusePhase.Dimming < DescentFusePhase.Candle);
        Assert.True(DescentFusePhase.Candle < DescentFusePhase.Vigil);
        Assert.True(DescentFusePhase.Vigil < DescentFusePhase.Terminal);
        Assert.True(DescentFusePhase.Terminal < DescentFusePhase.Zero);
    }

    // ------------------------------------------------------------ the dimming

    /// <summary>
    /// Four steps, six hours each, over the last day — and ZERO before it. The step must not creep
    /// in early: the chrome darkening is the loudest thing a no-block user ever sees, and it is
    /// supposed to arrive exactly one day out.
    /// </summary>
    [Theory]
    [InlineData(48,   0)]
    [InlineData(24.1, 0)]
    [InlineData(24,   1)]
    [InlineData(20,   1)]
    [InlineData(18,   2)]
    [InlineData(14,   2)]
    [InlineData(12,   3)]
    [InlineData(7,    3)]
    [InlineData(6,    4)]
    [InlineData(0.5,  4)]
    public void DimStepFor_OneStepPerSixHours(double hoursOut, int expected)
    {
        Assert.Equal(expected, DescentCountdownService.DimStepFor(At(TimeSpan.FromHours(hoursOut)), Now));
    }

    /// <summary>
    /// Step 4 HOLDS through zero. The chrome must not brighten back up in the second between the
    /// clock passing and the show opening.
    /// </summary>
    [Fact]
    public void DimStepFor_HoldsAtFourPastZero()
    {
        Assert.Equal(4, DescentCountdownService.DimStepFor(At(TimeSpan.Zero), Now));
        Assert.Equal(4, DescentCountdownService.DimStepFor(At(TimeSpan.FromHours(-5)), Now));
        Assert.Equal(4, DescentCountdownService.DimStepFor(At(-DescentCountdownService.DimHoldPastZero), Now));
    }

    /// <summary>
    /// ...but not forever (0825 F4). A "Not tonight", or an account the server never offered,
    /// used to keep a dimmed app until the owner unset the timestamp — which the auto-fire
    /// contract says never to do. Once the night is long over the chrome lets go by itself.
    /// </summary>
    [Fact]
    public void DimStepFor_LetsGoLongAfterZero()
    {
        var justPast = -DescentCountdownService.DimHoldPastZero - TimeSpan.FromSeconds(1);
        Assert.Equal(0, DescentCountdownService.DimStepFor(At(justPast), Now));
        Assert.Equal(0, DescentCountdownService.DimStepFor(At(TimeSpan.FromDays(-3)), Now));
        // The migrated overload still restores immediately, unchanged.
        Assert.Equal(0, DescentCountdownService.DimStepFor(At(TimeSpan.FromMinutes(-1)), Now, migrationCompleted: true));
    }

    // ------------------------------------------------------------ the late zero (0825 F5)

    /// <summary>
    /// A tick that first notices zero within the grace is the live night; one that notices it
    /// hours later (sleep, hibernate, a wedged UI thread) is not, and must take the away fork
    /// rather than play the full crack and mint a "you were there" keepsake.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(30, false)]
    [InlineData(299, false)]
    [InlineData(301, true)]
    [InlineData(8 * 3600, true)]
    public void IsZeroObservedLate_GraceIsFiveMinutes(int secondsAfterZero, bool expectedLate)
    {
        var zero = Now;
        Assert.Equal(expectedLate,
            DescentCountdownService.IsZeroObservedLate(zero, zero + TimeSpan.FromSeconds(secondsAfterZero)));
    }

    /// <summary>
    /// Step 0 is a genuine no-op on the colour, which is what lets the whole dimming live inside
    /// the app's existing theme writer without a "is the fuse on" branch around it.
    /// </summary>
    [Fact]
    public void ChromeDim_AtStepZero_ReturnsTheColourUntouched()
    {
        var original = System.Windows.Media.Color.FromArgb(0xB0, 0x25, 0x25, 0x42);
        Assert.Equal(original, DescentFuseChrome.Dim(original, 0));
        Assert.Equal(original, DescentFuseChrome.Dim(original, -3));
    }

    /// <summary>
    /// Each step is strictly darker than the last, alpha survives, and step 4 is nowhere near
    /// actual black — the room dims, the app stays readable.
    /// </summary>
    [Fact]
    public void ChromeDim_DarkensMonotonicallyAndKeepsAlpha()
    {
        var original = System.Windows.Media.Color.FromArgb(0xB0, 0x25, 0x25, 0x42);

        var previous = original;
        for (var step = 1; step <= DescentFuseChrome.MaxStep; step++)
        {
            var dimmed = DescentFuseChrome.Dim(original, step);
            Assert.Equal(original.A, dimmed.A);
            Assert.True(dimmed.R <= previous.R && dimmed.G <= previous.G && dimmed.B <= previous.B,
                $"step {step} should not be lighter than step {step - 1}");
            Assert.True(dimmed.R + dimmed.G + dimmed.B < previous.R + previous.G + previous.B,
                $"step {step} should be strictly darker than step {step - 1}");
            previous = dimmed;
        }

        // Still comfortably above the ceremony's ground: this is "the lights are low", not "the
        // window went black".
        Assert.True(previous.R > DescentFuseChrome.CeremonyBlack.R);
        Assert.True(previous.B > DescentFuseChrome.CeremonyBlack.B);
    }

    /// <summary>A step past the maximum clamps rather than overshooting into or past black.</summary>
    [Fact]
    public void ChromeDim_ClampsAboveMaxStep()
    {
        var original = System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x42);
        Assert.Equal(DescentFuseChrome.Dim(original, DescentFuseChrome.MaxStep),
                     DescentFuseChrome.Dim(original, DescentFuseChrome.MaxStep + 9));
    }

    // ------------------------------------------------------------ the readout

    /// <summary>
    /// The digits. Days drop off the front once there are none, and the last ten minutes show
    /// mm:ss because that is the only part anybody is reading by then.
    /// </summary>
    [Theory]
    [InlineData(2, 7, 14, 3, "2d 07:14:03")]
    [InlineData(0, 7, 14, 3, "07:14:03")]
    [InlineData(0, 1, 0, 0, "01:00:00")]
    [InlineData(0, 0, 9, 59, "09:59")]
    [InlineData(0, 0, 0, 7, "00:07")]
    public void TMinus_FormatsForTheDistance(int d, int h, int m, int s, string expected)
    {
        Assert.Equal(expected, DescentFuseCopy.TMinus(new TimeSpan(d, h, m, s)));
    }

    /// <summary>A negative span can never render a minus sign; it floors at zero.</summary>
    [Fact]
    public void TMinus_NeverGoesNegative()
    {
        Assert.Equal("00:00", DescentFuseCopy.TMinus(TimeSpan.FromSeconds(-90)));
    }

    /// <summary>
    /// The presence line's EXACT wording (server lane, PR #44). The integer counts sync beats in a
    /// fifteen-minute window, not distinct humans, so the copy is deliberately nounless — "N
    /// people" / "N others" / "N watching" would all be claims the number cannot support. Zero is a
    /// real in-window reading (the server omits the field when out of window), so it formats too.
    /// </summary>
    [Theory]
    [InlineData(0, "0 falling with you")]
    [InlineData(1, "1 falling with you")]
    [InlineData(2, "2 falling with you")]
    [InlineData(431, "431 falling with you")]
    public void Presence_IsNounlessAndExact(int count, string expected)
    {
        Assert.Equal(expected, DescentFuseCopy.Presence(count));
    }

    /// <summary>
    /// Guard against a well-meaning "improvement" that puts a noun back in. The count is beats, not
    /// people, and any of these words would turn an honest reading into a false claim.
    /// </summary>
    [Theory]
    [InlineData("people")]
    [InlineData("others")]
    [InlineData("users")]
    [InlineData("watching")]
    [InlineData("online")]
    public void Presence_NeverClaimsDistinctHumans(string forbidden)
    {
        Assert.DoesNotContain(forbidden, DescentFuseCopy.Presence(7), StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------ the companion

    /// <summary>
    /// One line per speaking phase, and TERMINAL IS THE LAST WORD. Zero is silent by design — the
    /// show speaks for itself — and Candle is silent because the flame is the line.
    /// </summary>
    [Fact]
    public void CompanionLines_ExistForTheSpeakingPhasesOnly()
    {
        Assert.NotNull(DescentFuseCopy.CompanionLine(DescentFusePhase.Whisper));
        Assert.NotNull(DescentFuseCopy.CompanionLine(DescentFusePhase.Clock));
        Assert.NotNull(DescentFuseCopy.CompanionLine(DescentFusePhase.Dimming));
        Assert.NotNull(DescentFuseCopy.CompanionLine(DescentFusePhase.Vigil));
        Assert.NotNull(DescentFuseCopy.CompanionLine(DescentFusePhase.Terminal));

        Assert.Null(DescentFuseCopy.CompanionLine(DescentFusePhase.Dark));
        Assert.Null(DescentFuseCopy.CompanionLine(DescentFusePhase.Candle));
        Assert.Null(DescentFuseCopy.CompanionLine(DescentFusePhase.Zero));
    }

    /// <summary>
    /// The petname travels as a TOKEN, not as a hardcoded "sweetie". These lines never pass through
    /// LocalizationManager.Get (they are not localized), so the service applies VocabTokens by
    /// hand — and if someone "simplifies" the token away, a Circe subject gets called by a Bambi
    /// name in the most-watched copy of the release. That is what this test is guarding.
    /// </summary>
    [Fact]
    public void ClockLine_CarriesThePetnameToken()
    {
        Assert.Contains("{petname}", DescentFuseCopy.CompanionClock, StringComparison.Ordinal);
        Assert.DoesNotContain("sweetie", DescentFuseCopy.CompanionClock, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// §0.6, made executable: NO OFFERS anywhere near the countdown. Not a price, not a tier, not
    /// an upsell, in any line the fuse can say.
    /// </summary>
    [Theory]
    [InlineData("patreon")]
    [InlineData("subscribe")]
    [InlineData("upgrade")]
    [InlineData("tier")]
    [InlineData("unlock")]
    [InlineData("$")]
    public void NoOffersInAnyFuseLine(string forbidden)
    {
        foreach (DescentFusePhase phase in Enum.GetValues<DescentFusePhase>())
        {
            var line = DescentFuseCopy.CompanionLine(phase);
            if (line is null) continue;
            Assert.DoesNotContain(forbidden, line, StringComparison.OrdinalIgnoreCase);
        }
    }
}
