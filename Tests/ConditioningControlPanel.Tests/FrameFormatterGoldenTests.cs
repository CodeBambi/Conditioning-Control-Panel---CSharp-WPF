using System;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Train 1 — golden text for <see cref="FrameFormatter"/>.
///
/// <para>Before the collapse, each of the six user-input frames existed three times (cloud, local,
/// OpenAI-compatible). The copies had already drifted once — the awareness frame's <c>Duration</c>
/// was hardcoded to <c>0m</c> everywhere until Train 0 — so the point of these tests is not that the
/// strings are pretty, it is that de-duplicating them did not silently change a single character of
/// what reaches the model. Update a golden here only when you intend to change the prompt.</para>
/// </summary>
public class FrameFormatterGoldenTests
{
    // ---------- duration bucketing ----------

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(1, "1s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1m")]
    [InlineData(90, "1m")]           // truncation, not rounding
    [InlineData(3599, "59m")]
    [InlineData(3600, "1h")]
    [InlineData(7260, "2h")]
    public void Duration_BucketsSecondsMinutesHours(int seconds, string expected)
    {
        Assert.Equal(expected, FrameFormatter.Duration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Duration_NullReadsAsZero()
    {
        // The awareness path's "we don't know how long they've been here".
        Assert.Equal("0s", FrameFormatter.Duration(null));
    }

    // ---------- legacy frames ----------

    [Fact]
    public void AwarenessFrame_IsTheContextTag()
    {
        var frame = FrameFormatter.AwarenessFrame(
            detectedName: "chrome",
            category: "Media/Streaming",
            serviceName: "YouTube",
            pageTitle: "Bambi Bae - full loop",
            duration: TimeSpan.FromMinutes(22));

        Assert.Equal("[Category: Media/Streaming | App: YouTube | Title: Bambi Bae - full loop | Duration: 22m]", frame);
    }

    [Fact]
    public void AwarenessFrame_FallsBackToTheDetectedNameForBothSlots()
    {
        // Empty service name / page title is the "bare window detection" case.
        var frame = FrameFormatter.AwarenessFrame("Notepad", "Productivity", "", "", TimeSpan.Zero);
        Assert.Equal("[Category: Productivity | App: Notepad | Title: Notepad | Duration: 0s]", frame);
    }

    [Fact]
    public void StillOnFrame_UsesTheDisplayNameForAppAndTitle()
    {
        var frame = FrameFormatter.StillOnFrame("Amazon", "Shopping", TimeSpan.FromMinutes(75));
        Assert.Equal("[Category: Shopping | App: Amazon | Title: Amazon | Duration: 1h]", frame);
    }

    [Fact]
    public void KeywordFrame_DefaultWording()
    {
        Assert.Equal("You just caught the user on the word 'bimbo'. React in character, one short line.",
            FrameFormatter.KeywordFrame("bimbo", null));
    }

    [Fact]
    public void KeywordFrame_UserTemplateWins()
    {
        Assert.Equal("Tease them about saying bimbo, twice.",
            FrameFormatter.KeywordFrame("bimbo", "Tease them about saying {keyword}, twice."));
    }

    [Fact]
    public void LockScreenFrame_DefaultWording()
    {
        // "of time" is not a typo introduced here — it is the shipped string, preserved verbatim.
        Assert.Equal(
            "The user made 3 mistakes in 'good girls obey' for the lock screen. " +
            "They had to type it 5 of time. React in character, one short line.",
            FrameFormatter.LockScreenFrame("good girls obey", 3, 5, null));
    }

    [Fact]
    public void LockScreenFrame_UserTemplateSubstitutesAllThreePlaceholders()
    {
        // {sentance} keeps its shipped misspelling — users have it in their saved templates.
        Assert.Equal("2 slips typing 'be a doll' over 4 tries",
            FrameFormatter.LockScreenFrame("be a doll", 2, 4, "{mistakes} slips typing '{sentance}' over {amount} tries"));
    }

    [Fact]
    public void VideoDoneFrame_DefaultWording()
    {
        Assert.Equal("The user has just finished the mandatory video Bambi Bae. React in character, one short line.",
            FrameFormatter.VideoDoneFrame("Bambi Bae", null));
    }

    [Fact]
    public void VideoDoneFrame_UserTemplateWins()
    {
        Assert.Equal("They finished Bambi Bae. Praise them.",
            FrameFormatter.VideoDoneFrame("Bambi Bae", "They finished {title}. Praise them."));
    }

    // ---------- Train 1 ambient descriptors ----------

    [Fact]
    public void AwarenessEvent_IsProseAndPutsTheTitleLast()
    {
        // Prose, not a bracketed tag: the brain wraps this in «event: …» and clamps it to ~25
        // tokens, and the title is the part we can afford to lose to that clamp.
        Assert.Equal("user is on YouTube (Media/Streaming) for 22m, tab \"Bambi Bae\"",
            FrameFormatter.AwarenessEvent("chrome", "Media/Streaming", "YouTube", "Bambi Bae", TimeSpan.FromMinutes(22)));
    }

    [Fact]
    public void AwarenessEvent_OmitsTheTabWhenItIsJustTheAppAgain()
    {
        Assert.Equal("user is on Notepad (Productivity) for 30s",
            FrameFormatter.AwarenessEvent("Notepad", "Productivity", "", "", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void AmbientDescriptors_SurviveTheBrainsTwentyFiveTokenClamp()
    {
        // The legacy FRAMES do not — the default lock-screen frame alone is ~130 characters, which
        // is why the descriptors exist at all. Guard the property rather than the wording.
        var descriptors = new[]
        {
            FrameFormatter.StillOnEvent("Amazon", "Shopping", TimeSpan.FromMinutes(75)),
            FrameFormatter.KeywordEvent("bimbo"),
            FrameFormatter.LockScreenEvent("good girls obey", 3, 5),
            FrameFormatter.VideoDoneEvent("Bambi Bae")
        };

        Assert.All(descriptors, d => Assert.True(d.Length <= 100, $"descriptor too long for the clamp: {d}"));
        Assert.Equal("user is still on Amazon (Shopping) after 1h", descriptors[0]);
        Assert.Equal("user just said the word \"bimbo\"", descriptors[1]);
        Assert.Equal("user typed the mantra \"good girls obey\" 5x with 3 mistake(s)", descriptors[2]);
        Assert.Equal("user finished the mandatory video \"Bambi Bae\"", descriptors[3]);
    }

    // ---------- routing gate ----------

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("React to {keyword} like a brat.", false)]
    public void CanRouteAmbient_OnlyWithoutAUserAuthoredTemplate(string? template, bool expected)
    {
        // A user's template is an INSTRUCTION, not a record of something that happened: it must not
        // be wrapped in the event sigil, and the clamp would truncate a long one into nonsense.
        Assert.Equal(expected, FrameFormatter.CanRouteAmbient(template));
    }
}
