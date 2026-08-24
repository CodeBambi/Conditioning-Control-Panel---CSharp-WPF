using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Pop Quiz panel's sentences for the states a HEADLESS drive of the rack row cannot reach —
/// a question that ended badly, a card the operating system refused ink for, and an XP grant that
/// did not bank. Pure logic, no Avalonia: these are the words a user reads when something has gone
/// wrong, and nothing else in the suite would notice if they said the wrong thing.
///
/// <para>The reachable states (off, armed, the dials, the pool line, the no-ledger XP line) are
/// asserted against the RENDERED surface in
/// <c>CcpClient.HeadlessTests.PopQuizRowHeadlessTests</c> instead, because there the claim is that
/// the page really shows them.</para>
/// </summary>
public class PopQuizPanelNoticeTests
{
    private static string StateFor(PopQuizResolution resolution) =>
        PopQuizPanelNotices.DescribeQuizState(
            EffectDotState.Live,
            quizCount: 3,
            last: new PopQuizEvent(3, DateTimeOffset.UnixEpoch, QuestionLength: 41, OptionCount: 4),
            sessionRunning: true,
            canReachAUser: true,
            asking: false,
            answeredCount: 2,
            skippedCount: 1,
            lastResolution: resolution);

    /// <summary>
    /// <b>The four ways a question can end are four different sentences.</b> Upstream's card has one
    /// answer path and one Escape path (<c>Windows/PopQuizWindow.xaml.cs:128-134</c>,
    /// <c>:157-177</c>); the port adds the two the shared capability makes possible — a session that
    /// stopped with a card up, and an operating system that would not take the card. Collapsing any
    /// pair of them would tell a user who walked away that they answered, or tell a user whose
    /// desktop refused the card that they skipped it.
    /// </summary>
    [Fact]
    public void TheFourEndingsAreFourDifferentSentences_AndNoneOfThemReadsLikeAnother()
    {
        var answered = StateFor(PopQuizResolution.Answered);
        var skipped = StateFor(PopQuizResolution.Skipped);
        var withdrawn = StateFor(PopQuizResolution.Withdrawn);
        var refused = StateFor(PopQuizResolution.Refused);

        Assert.EndsWith(" You answered the last one.", answered, StringComparison.Ordinal);
        Assert.EndsWith(" You skipped the last one with Esc.", skipped, StringComparison.Ordinal);
        Assert.EndsWith(
            " The last one was taken down when the session stopped.", withdrawn, StringComparison.Ordinal);
        Assert.EndsWith(
            " The last question was taken straight back down: the operating system would not give it the "
            + "keyboard.",
            refused,
            StringComparison.Ordinal);

        // Four distinct strings, asserted as a set so a later edit cannot quietly make two of them
        // the same sentence.
        Assert.Equal(4, new HashSet<string>([answered, skipped, withdrawn, refused], StringComparer.Ordinal).Count);

        // And every one of them carries the counters, so the ending never replaces the tally.
        foreach (var line in new[] { answered, skipped, withdrawn, refused })
        {
            Assert.Contains("3 questions asked so far, 2 answered and 1 skipped.", line, StringComparison.Ordinal);
            Assert.Contains("The last one was #3.", line, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <b>A card the operating system gave the keyboard but no ink is reported as taken back
    /// down</b>, not as a working question. This is the module's own behaviour — <c>Deliver</c>
    /// dismisses on anything that is not <c>Available</c>, including <c>Degraded</c>, because a
    /// topmost blank window holding the user's keyboard is strictly worse than no question — and the
    /// sentence has to match it or the panel is describing a card that is not there.
    ///
    /// <para>The capability's own reason detail travels VERBATIM, which is the rule every panel on
    /// the rack keeps: the closing line quotes the operating system rather than saying something
    /// this page made up about a platform.</para>
    /// </summary>
    [Fact]
    public void ADegradedCardIsReportedAsTakenBackDown_AndTheOssOwnReasonTravelsVerbatim()
    {
        var reason = new CapabilityReason("input-card-blank", "the card's client area read back 0 inked pixels");
        var line = PopQuizPanelNotices.DescribeInputCapability(
            new CapabilityState.Degraded("the keyboard is held", reason),
            InputCaptureObservation.NotAsked);

        Assert.StartsWith("The question was taken straight back down.", line, StringComparison.Ordinal);
        Assert.Contains("the keyboard is held", line, StringComparison.Ordinal);
        Assert.Contains("the card's client area read back 0 inked pixels", line, StringComparison.Ordinal);

        // An Available card says the opposite thing, so the sentence above is not what this method
        // says about everything.
        var available = PopQuizPanelNotices.DescribeInputCapability(
            new CapabilityState.Available("foreground=True, keyboard-focus=True"),
            InputCaptureObservation.NotAsked);
        Assert.StartsWith("The operating system gave the card the keyboard:", available, StringComparison.Ordinal);
        Assert.DoesNotContain("taken straight back down", available, StringComparison.Ordinal);

        // Before anything has been asked the line is about THIS SESSION, never a claim about the
        // machine: the same discipline the Lock Card's own capability line keeps.
        var nothingAsked = PopQuizPanelNotices.DescribeInputCapability(null, InputCaptureObservation.NotAsked);
        Assert.Contains("Nothing has been asked of the operating system yet.", nothingAsked, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The XP line tells the truth in all three of its states</b>, and the one this build actually
    /// ships is the first: no ledger, so nothing banks. Upstream pays twenty-five on every answer
    /// (<c>Windows/PopQuizWindow.xaml.cs:161</c>, <c>AddXP(25, XPSource.Other)</c>), so a panel that
    /// went quiet about it would leave a user of the shipping app looking for XP that never arrives.
    /// A REFUSED grant is not reported as a banked one either — the port's ledger returns a typed
    /// refusal rather than throwing, and that outcome is what this renders.
    /// </summary>
    [Fact]
    public void TheXpLineSeparatesNoLedgerFromABankedGrantAndFromARefusedOne()
    {
        var none = PopQuizPanelNotices.DescribeXp(banksXp: false, lastGrant: null);
        Assert.Contains("The shipping app pays 25 XP for answering", none, StringComparison.Ordinal);
        Assert.Contains("banks nothing", none, StringComparison.Ordinal);

        var waiting = PopQuizPanelNotices.DescribeXp(banksXp: true, lastGrant: null);
        Assert.Equal("25 XP an answer, once you have answered one.", waiting);

        // Upstream's own number, pinned as a literal rather than through PopQuizEffect.AnswerXp: a
        // sentence that reported whatever constant it was handed would agree with a changed payout.
        var banked = PopQuizPanelNotices.DescribeXp(
            banksXp: true,
            new XpGrant(XpGrantState.Granted, 25, LevelBefore: 4, LevelAfter: 5, XpIntoLevel: 3, AtCeiling: false, Reason: string.Empty));
        Assert.Equal("Banked 25 XP for the last answer — level 5.", banked);

        var refused = PopQuizPanelNotices.DescribeXp(
            banksXp: true,
            new XpGrant(
                XpGrantState.RefusedLedgerUnknown, 25, LevelBefore: null, LevelAfter: null, XpIntoLevel: null,
                AtCeiling: false, Reason: "the ledger could not be read"));
        Assert.Equal("The last answer's 25 XP did not bank: the ledger could not be read", refused);
        Assert.DoesNotContain("Banked", refused, StringComparison.Ordinal);
    }
}
