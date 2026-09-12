using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1176 — "companion does not comment on programs/window titles at all".
///
/// The report arrived with 107 log lines and not one of them about awareness: every per-frame
/// [AWARE] line is Debug (they name the resolved app id), Debug never reaches the session file the
/// bug-report flow attaches, and [AWARE] is not a BugReportService diag marker. This summary is the
/// part that does reach a report, so what it may and may not carry is the thing worth pinning: reason
/// tokens and counts, never an app id, a title or a cluster.
/// </summary>
public class AwarenessTallyTests
{
    [Fact]
    public void AnEmptyTallyRendersNothing()
    {
        var tally = new AwarenessTally();
        Assert.True(tally.IsEmpty);
        Assert.Equal(string.Empty, tally.Drain());
    }

    [Fact]
    public void BucketsAreGroupedAndTheLoudestReasonComesFirst()
    {
        var tally = new AwarenessTally();
        tally.Note("drop", FrameDrop.OwnProcess);
        tally.Note("drop", FrameDrop.OwnProcess);
        tally.Note("drop", FrameDrop.DenyListed);
        tally.Note("dnd", DndGate.TypingBurst);
        tally.Note("arbiter", "llm-unavailable/no-bark");

        var line = tally.Drain();

        Assert.Equal("arbiter=llm-unavailable-no-bark:1; dnd=typingburst:1; drop=ownprocess:2,denylisted:1", line);
    }

    [Fact]
    public void DrainClearsSoEachWindowStandsAlone()
    {
        var tally = new AwarenessTally();
        tally.Note("scored", "below-floor");
        Assert.NotEqual(string.Empty, tally.Drain());

        Assert.True(tally.IsEmpty);
        Assert.Equal(string.Empty, tally.Drain());
    }

    [Fact]
    public void AnythingThatDoesNotLookLikeAReasonTokenIsRefused()
    {
        // Defence in depth: every caller passes an enum or a code-owned token today, but this line is
        // Information and therefore ships in bug reports, so the one thing it must never do is put a
        // window title into a GitHub issue.
        var tally = new AwarenessTally();
        tally.Note("drop", "Inbox (14) - beebee@example.com - Outlook");
        tally.Note("drop", "Naughty Bambi 109749 - the longest video title in the pool");

        Assert.Equal("drop=other:2", tally.Drain());
    }

    [Fact]
    public void ABlankReasonStillCounts()
    {
        var tally = new AwarenessTally();
        tally.Note("arbiter", (string?)null);
        Assert.Equal("arbiter=none:1", tally.Drain());
    }
}
