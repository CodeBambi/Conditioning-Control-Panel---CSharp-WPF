using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Takeover quest time ("On Autopilot", "Set It and Forget It") is credited by
/// <see cref="RunningTimeCredit"/> from monotonic clock readings, not per tick. ccp-bugs #1327:
/// with the panel minimised Windows throttles the 1 s tracking timer, and the old 10 s per-tick
/// cap threw most of the real running time away.
/// </summary>
public class RunningTimeCreditTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>Feeds running samples at the given second marks and returns the total minutes.</summary>
    private static double Run(RunningTimeCredit credit, params double[] seconds)
        => seconds.Sum(t => credit.Sample(true, S(t)));

    [Fact]
    public void Normal_one_second_ticks_credit_the_real_running_time()
    {
        var credit = new RunningTimeCredit();
        var marks = Enumerable.Range(0, 601).Select(i => (double)i).ToArray(); // 0..600 s

        Assert.Equal(10.0, Run(credit, marks), 6);
    }

    [Fact]
    public void Slow_ticks_while_minimised_still_credit_the_full_interval()
    {
        var credit = new RunningTimeCredit();
        // A throttled timer: ticks 40 s apart for 20 minutes. The old cap credited 10 s a tick
        // (5 minutes); the real running time is 20 minutes.
        var marks = Enumerable.Range(0, 31).Select(i => i * 40.0).ToArray(); // 0..1200 s

        Assert.Equal(20.0, Run(credit, marks), 6);
    }

    [Fact]
    public void Irregular_ticks_add_up_to_the_wall_clock_span()
    {
        var credit = new RunningTimeCredit();

        Assert.Equal(5.0, Run(credit, 0, 1, 2, 31, 32, 95, 150, 151, 300), 6);
    }

    [Fact]
    public void A_long_sleep_gap_credits_nothing_for_that_gap()
    {
        var credit = new RunningTimeCredit();
        var total = Run(credit, 0, 60, 120);              // 2 minutes running
        total += credit.Sample(true, S(120 + 3600));       // laptop asleep for an hour
        total += credit.Sample(true, S(120 + 3600 + 60));  // one more minute after waking

        Assert.Equal(3.0, total, 6);
    }

    [Fact]
    public void A_gap_exactly_at_the_ceiling_still_counts_and_one_past_it_does_not()
    {
        var credit = new RunningTimeCredit(TimeSpan.FromMinutes(3));

        Assert.Equal(0, credit.Sample(true, S(0)));
        Assert.Equal(3.0, credit.Sample(true, S(180)), 6);
        Assert.Equal(0, credit.Sample(true, S(180 + 180.001)));
    }

    [Fact]
    public void Stop_and_start_credits_only_the_running_stretches()
    {
        var credit = new RunningTimeCredit();
        var total = Run(credit, 0, 30, 60);   // 1 minute running
        total += credit.Sample(false, S(90)); // stopped
        total += credit.Sample(false, S(400));
        total += Run(credit, 500, 530, 560);  // restarted: 1 more minute

        Assert.Equal(2.0, total, 6);
    }

    [Fact]
    public void The_first_sample_of_a_run_only_opens_the_stamp()
    {
        var credit = new RunningTimeCredit();

        Assert.Equal(0, credit.Sample(true, S(1000)));
    }

    [Fact]
    public void A_clock_going_backwards_or_repeating_credits_nothing()
    {
        var credit = new RunningTimeCredit();
        credit.Sample(true, S(100));

        Assert.Equal(0, credit.Sample(true, S(100)));
        Assert.Equal(0, credit.Sample(true, S(50)));
        Assert.Equal(0.5, credit.Sample(true, S(80)), 6); // resumes from the latest reading
    }

    [Fact]
    public void Credit_never_exceeds_the_measured_span()
    {
        var credit = new RunningTimeCredit();
        var rng = new Random(1327);
        double t = 0, total = 0;
        credit.Sample(true, S(t));
        for (int i = 0; i < 2000; i++)
        {
            t += rng.NextDouble() * 400; // some ticks fall past the ceiling
            total += credit.Sample(true, S(t));
        }

        Assert.True(total <= t / 60.0 + 1e-9);
    }

    [Fact]
    public void A_non_positive_ceiling_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunningTimeCredit(TimeSpan.Zero));
    }
}
