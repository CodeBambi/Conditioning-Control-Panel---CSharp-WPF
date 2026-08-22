using ConditioningControlPanel.Helpers;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Programs rail comet's layout gate - the decision that used to hard-freeze the app.
///
/// <para><b>Why this is worth a suite.</b> The bug behind ccp-bugs #984 / #993 / #996 / #1001 was
/// not a wrong pixel: the comet's host is authored Collapsed, so its ActualWidth was structurally
/// always 0, the gate never passed, and the retry re-posted itself forever at a dispatcher priority
/// ABOVE input - a window that kept rendering and never logged a crash but accepted no clicks, on
/// every visit to a hot Programs tab. Nothing about that is visible in a screenshot, and the effect
/// is cosmetic, so the property that actually matters is the one pinned here: this gate always
/// terminates, and its worst outcome is a missing comet.</para>
/// </summary>
public class ProgramCometGateTests
{
    // =====================================================================================
    //  the measured path
    // =====================================================================================

    [Theory]
    [InlineData(320.0)]
    [InlineData(60.0)]
    [InlineData(1920.0)]
    public void A_measured_host_runs_over_its_own_width(double width)
    {
        var gate = ProgramCometGate.Decide(width, 999.0, 0);

        Assert.Equal(ProgramCometAction.Run, gate.Action);
        Assert.Equal(width, gate.Width);
    }

    /// <summary>A run always hands back a spent budget of zero, so the next stall starts fresh.</summary>
    [Fact]
    public void A_run_resets_the_attempt_budget()
    {
        var gate = ProgramCometGate.Decide(400.0, 400.0, ProgramCometGate.MaxAttempts);

        Assert.Equal(ProgramCometAction.Run, gate.Action);
        Assert.Equal(0, gate.Attempts);
    }

    // =====================================================================================
    //  the collapsed-host fallback (LAYER 1)
    // =====================================================================================

    /// <summary>
    /// The exact shape of the ship-blocker: a host that has never been measured because it was
    /// still collapsed when it was read. The rail it lives inside HAS been measured, and its width
    /// is the same travel distance, so the comet runs instead of deferring.
    /// </summary>
    [Fact]
    public void A_collapsed_host_falls_back_to_the_rail_width()
    {
        var gate = ProgramCometGate.Decide(hostWidth: 0.0, railWidth: 420.0, attempts: 0);

        Assert.Equal(ProgramCometAction.Run, gate.Action);
        Assert.Equal(420.0, gate.Width);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-10.0)]
    public void A_nonsense_host_width_falls_back_rather_than_animating_over_it(double host)
    {
        var gate = ProgramCometGate.Decide(host, 300.0, 0);

        Assert.Equal(ProgramCometAction.Run, gate.Action);
        Assert.Equal(300.0, gate.Width);
    }

    /// <summary>A rail that is nonsense too is simply "not measured yet" - never a NaN duration.</summary>
    [Fact]
    public void Two_nonsense_widths_defer_instead_of_producing_a_NaN_run()
    {
        var gate = ProgramCometGate.Decide(double.NaN, double.NaN, 0);

        Assert.Equal(ProgramCometAction.Retry, gate.Action);
    }

    /// <summary>
    /// Below the floor is not "a short comet", it is "no measurement yet". 59 DIPs of travel for a
    /// 130-wide head entering from -140 is a smear inside its own clip.
    /// </summary>
    [Fact]
    public void A_rail_narrower_than_the_floor_is_treated_as_unmeasured()
    {
        var gate = ProgramCometGate.Decide(40.0, 59.0, 0);

        Assert.Equal(ProgramCometAction.Retry, gate.Action);
    }

    // =====================================================================================
    //  the attempt cap (LAYER 3)
    // =====================================================================================

    /// <summary>
    /// The load-bearing property. Feed the gate a surface that never measures and drive it exactly
    /// as the call site does - store back what it returns - and it must stop asking. This is the
    /// test that would have failed against the shipped code, whose retry cleared its own guard
    /// before recursing and so looped without bound.
    /// </summary>
    [Fact]
    public void An_unmeasurable_rail_terminates_instead_of_retrying_forever()
    {
        var attempts = 0;
        var retries = 0;

        for (var pass = 0; pass < 1000; pass++)
        {
            var gate = ProgramCometGate.Decide(0.0, 0.0, attempts);
            attempts = gate.Attempts;

            if (gate.Action == ProgramCometAction.GiveUp) break;

            Assert.Equal(ProgramCometAction.Retry, gate.Action);
            retries++;
        }

        Assert.Equal(ProgramCometGate.MaxAttempts, retries);
        Assert.Equal(ProgramCometGate.MaxAttempts, attempts);
    }

    /// <summary>
    /// Each deferral must SPEND budget. The old bool guard was cleared inside the retry callback
    /// before it recursed, so every pass looked like the first one - which is precisely how an
    /// unbounded chain of above-input work got started.
    /// </summary>
    [Fact]
    public void Every_retry_spends_budget_rather_than_clearing_it()
    {
        Assert.Equal(1, ProgramCometGate.Decide(0, 0, 0).Attempts);
        Assert.Equal(2, ProgramCometGate.Decide(0, 0, 1).Attempts);
        Assert.Equal(3, ProgramCometGate.Decide(0, 0, 2).Attempts);
    }

    [Fact]
    public void A_spent_budget_gives_up_and_stays_spent()
    {
        var gate = ProgramCometGate.Decide(0, 0, ProgramCometGate.MaxAttempts);

        Assert.Equal(ProgramCometAction.GiveUp, gate.Action);
        Assert.Equal(ProgramCometGate.MaxAttempts, gate.Attempts);

        // Giving up is stable: re-asking a parked comet does not re-open the retry chain.
        var again = ProgramCometGate.Decide(0, 0, gate.Attempts);
        Assert.Equal(ProgramCometAction.GiveUp, again.Action);
    }

    /// <summary>A counter that somehow went negative must not buy extra retries.</summary>
    [Fact]
    public void A_negative_counter_is_clamped_rather_than_trusted()
    {
        var gate = ProgramCometGate.Decide(0, 0, -50);

        Assert.Equal(ProgramCometAction.Retry, gate.Action);
        Assert.Equal(1, gate.Attempts);
    }

    /// <summary>
    /// Give-up is not permanent damage: the tab-hide path zeroes the counter, and the gate must
    /// then behave exactly as it does on a first visit.
    /// </summary>
    [Fact]
    public void A_reset_counter_re_arms_the_comet()
    {
        Assert.Equal(ProgramCometAction.GiveUp,
            ProgramCometGate.Decide(0, 0, ProgramCometGate.MaxAttempts).Action);

        // StopProgramIgnitionLoops sets the field back to 0.
        Assert.Equal(ProgramCometAction.Run, ProgramCometGate.Decide(0, 500.0, 0).Action);
    }
}
