using Xunit;
using Xunit.Sdk;

namespace CcpClient.Tests;

/// <summary>
/// <b>The two timing verdicts, pinned against each other. Before this file neither of them
/// was pinned anywhere</b>, and that is how the defect below survived across roughly ten call sites
/// in two projects.
///
/// <para><b>What was wrong.</b> <see cref="TestWait"/> emits one of two greppable tokens on expiry
/// and they carry opposite instructions: <c>CONDITION-NEVER-TRUE</c> says "treat as a REAL
/// product/test failure" and <c>ENVIRONMENT-STARVED</c> says "rerun or reduce load BEFORE treating
/// this as a failure". The selector was a poll-loop heuristic — worst scheduler slip, or fewer polls
/// than a tenth of the window's expected count — and the SIGNAL overload has no poll loop at all. It
/// passed the sentinel <c>Polls = -1</c>, which is below a tenth of every window, so <b>every
/// expired signal wait in the suite emitted the verdict that tells the reader to re-run it.</b></para>
///
/// <para><b>Why that is worse than a wrong word.</b> The token survives into the TRX failure text
/// (an earlier land lesson, which is why it leads the message at all). This packet's own falsification
/// control is a signal wait: if the mechanism it forces ever genuinely breaks, the next engineer
/// would have been told, in the failure itself, to re-run — the exact instruction the named-failure
/// reporting and this file exist to refuse. A packet whose thesis is "do not make it green, make it
/// understood" cannot ship a control that asks for a re-run.</para>
///
/// <para><b>What is pinned.</b> Both directions, because a selector that answered one verdict
/// always would pass a one-sided test: an expired signal wait reads CONDITION-NEVER-TRUE, a starved
/// poll loop still reads ENVIRONMENT-STARVED, and a healthy poll loop that simply never saw its
/// condition still reads CONDITION-NEVER-TRUE.</para>
/// </summary>
public class TestWaitVerdictTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    /// <summary>The expected poll count for <see cref="Window"/> is 2000 (10 ms cadence); a tenth
    /// of that is the starvation line the selector uses.</summary>
    private const int HealthyPolls = 2000;

    [Fact]
    public async Task AnExpiredSIGNALWaitReportsAREALFailure_AndNeverTellsTheReaderToReRunIt()
    {
        // End to end through the public API, so this pins the WIRING and not only the selector: a
        // signal that never completes, a window whose elapsing is the subject of this fact (which is
        // the one case a short literal is for), and the message the caller would really see.
        var never = new TaskCompletionSource();

        var failure = await Assert.ThrowsAsync<XunitException>(
            () => TestWait.Until(
                never.Task,
                "a signal this fact deliberately never completes",
                () => "no actor: this fact has none",
                TimeSpan.FromMilliseconds(1)));

        Assert.Contains("TIMING-VERDICT:CONDITION-NEVER-TRUE", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TIMING-VERDICT:ENVIRONMENT-STARVED", failure.Message, StringComparison.Ordinal);

        // The instruction, not the label. This is the assertion that would have caught the defect.
        Assert.DoesNotContain("rerun", failure.Message, StringComparison.OrdinalIgnoreCase);

        // And the evidence no longer prints the poll sentinels as though they were measurements.
        Assert.Contains("there is NO poll loop here", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("polls=-1", failure.Message, StringComparison.Ordinal);
        Assert.Contains("a signal this fact deliberately never completes", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSelectorAnswersBOTHVerdicts_AndOnlyAPOLLLoopCanEverReadAsStarved()
    {
        // The selector directly, because the starved branch cannot be produced deterministically
        // through a real wait: whether a 1 ms window manages zero polls or one is a tick-boundary
        // accident, and a fact that depends on that accident is the very thing this packet is
        // about. The four rows below are the whole truth table the failure text turns on.
        var signal = TestWait.Verdict(
            "a deterministic signal", Window, deterministicSignal: true, polls: -1, worstSlipMs: -1);
        var starvedByPollCount = TestWait.Verdict(
            "a polled condition", Window, deterministicSignal: false, polls: 0, worstSlipMs: 0);
        var starvedBySlip = TestWait.Verdict(
            "a polled condition", Window, deterministicSignal: false, polls: HealthyPolls, worstSlipMs: 900);
        var healthyPoll = TestWait.Verdict(
            "a polled condition", Window, deterministicSignal: false, polls: HealthyPolls, worstSlipMs: 5);

        // A signal wait has no loop to starve, whatever sentinels it carries — the defect, pinned.
        Assert.Contains("TIMING-VERDICT:CONDITION-NEVER-TRUE", signal, StringComparison.Ordinal);
        Assert.Contains("the deterministic signal never completed", signal, StringComparison.Ordinal);
        Assert.DoesNotContain("rerun", signal, StringComparison.OrdinalIgnoreCase);

        // Both starvation routes still work: a loop that could not run, and a loop that ran late.
        // Without these two the fix above could have been "never say starved", which would throw
        // away the differential the verdict exists for.
        Assert.Contains("TIMING-VERDICT:ENVIRONMENT-STARVED", starvedByPollCount, StringComparison.Ordinal);
        Assert.Contains("rerun or reduce load", starvedByPollCount, StringComparison.Ordinal);
        Assert.Contains("TIMING-VERDICT:ENVIRONMENT-STARVED", starvedBySlip, StringComparison.Ordinal);

        // And a poll loop that ran on schedule and simply never saw its condition is a real failure.
        Assert.Contains("TIMING-VERDICT:CONDITION-NEVER-TRUE", healthyPoll, StringComparison.Ordinal);
        Assert.Contains("it never became true", healthyPoll, StringComparison.Ordinal);
        Assert.DoesNotContain("rerun", healthyPoll, StringComparison.OrdinalIgnoreCase);
    }
}
