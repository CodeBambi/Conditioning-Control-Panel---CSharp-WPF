using System;
using System.IO;
using ConditioningControlPanel.Services.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The line format, the category column and the two enrichers behind them.
///
/// <para>These exist because the format is load-bearing in a way a format usually is not: 7,300
/// call sites already put their category in the message by hand, in two different conventions, and
/// the bug reporter greps the result. Getting the category derivation or the prefix strip subtly
/// wrong does not throw - it silently mangles every line in the file and breaks the marker
/// sampling that triage runs on.</para>
/// </summary>
[Collection(LoggingStaticsCollection.Name)]
public class LogFormatTests
{
    public LogFormatTests() =>
        LogRedactor.ConfigureRoots(@"C:\Users\alice\AppData\Local\ConditioningControlPanel", null, null);

    private sealed class Factory : ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            => new(name, new ScalarValue(value));
    }

    private static LogEvent Event(string template, Exception? ex = null, params LogEventProperty[] props) =>
        new(new DateTimeOffset(2026, 9, 4, 12, 34, 56, 789, TimeSpan.Zero),
            LogEventLevel.Information, ex, new MessageTemplateParser().Parse(template), props);

    private static string Format(LogEvent e)
    {
        var factory = new Factory();
        new CategoryEnricher().Enrich(e, factory);
        new RedactingEnricher().Enrich(e, factory);
        using var sw = new StringWriter();
        new CcpLineFormatter().Format(e, sw);
        return sw.ToString();
    }

    [Theory]
    // A bracket tag, SHOUTED or not, lands in one column value.
    [InlineData("[EMOTE] xfade to {A}", "Emote")]
    [InlineData("[EmiDesk] ring open with {N} cards", "EmiDesk")]
    // "Class:" and "Class.Method:", with the redundant "Service" suffix dropped.
    [InlineData("GlobalHotkeyService: registered {Key}", "GlobalHotkey")]
    [InlineData("ProfileSyncService.Sync: done in {Ms}ms", "ProfileSync")]
    [InlineData("VideoService: opened", "Video")]
    // Everything else is App, including a lowercase lead and a bracket that is not a tag.
    [InlineData("Application starting v{Version}", "App")]
    [InlineData("started 3 things", "App")]
    [InlineData("[not a tag here, too long to be one] x", "App")]
    public void CategoryIsDerivedFromTheMessageTemplate(string template, string expected)
        => Assert.Equal(expected, CategoryEnricher.Derive(template));

    [Fact]
    public void ExplicitCategoryWins()
    {
        var e = Event("VideoService: opened", null,
            new LogEventProperty(CategoryEnricher.PropertyName, new ScalarValue("Chaos")));
        new CategoryEnricher().Enrich(e, new Factory());
        Assert.Equal("Chaos", ((ScalarValue)e.Properties[CategoryEnricher.PropertyName]).Value);
    }

    [Fact]
    public void LineShape_IsTimeLevelCategoryMessage()
    {
        var line = Format(Event("[EMOTE] xfade done"));
        // The category column is fixed width so the messages line up down the page, and the prefix
        // that is now IN that column is stripped from the message rather than printed twice.
        Assert.Equal("12:34:56.789 INF [Emote        ] xfade done" + Environment.NewLine, line);
    }

    [Fact]
    public void OnlyAnExactRepeatOfTheCategoryIsStripped()
    {
        // "Video" is the category of a "VideoService:" line, so the whole prefix goes...
        Assert.StartsWith("12:34:56.789 INF [Video        ] opened", Format(Event("VideoService: opened")));
        // ...but an unrelated bracket stays, because greps in the wild match on message text.
        Assert.Contains("[queue] drained", Format(Event("[EMOTE] [queue] drained")));
    }

    [Fact]
    public void InterpolatedMessages_AreRedactedByTheFormatter()
    {
        // Around half the call sites in this app pass an interpolated $"..." string, which puts the
        // path in the TEMPLATE, where no enricher can reach it. This is the safety net.
        var line = Format(Event(@"[FLASH] loading C:\Users\alice\Pictures\a.png"));
        Assert.Contains(@"~\Pictures\a.png", line);
        Assert.DoesNotContain("alice", line);
    }

    [Fact]
    public void PropertyValues_AreRedactedByTheEnricher()
    {
        var e = Event("[SETTINGS] saved to {Path} for {Id}", null,
            new LogEventProperty("Path", new ScalarValue(@"C:\Users\alice\AppData\Local\ConditioningControlPanel\settings.json")),
            new LogEventProperty("Id", new ScalarValue("123456789012347890")));
        var line = Format(e);
        Assert.Contains(@"%DATA%\settings.json", line);
        Assert.Contains("<id:…7890>", line);
        // The template itself must survive intact - it is the throttling key and the grep target.
        Assert.Equal("[SETTINGS] saved to {Path} for {Id}", e.MessageTemplate.Text);
    }

    [Fact]
    public void Exceptions_AreCompactedAndRedacted()
    {
        Exception caught;
        try { throw new IOException(@"cannot open C:\Users\alice\clip.mp4"); }
        catch (Exception ex) { caught = ex; }

        // Give it a real stack with a framework frame in it.
        try { "abc".Substring(99); }
        catch (Exception ex) { caught = new IOException(@"cannot open C:\Users\alice\clip.mp4", ex); }

        var line = Format(Event("[VIDEO] open failed", caught));
        Assert.DoesNotContain("alice", line);
        Assert.Contains(@"~\clip.mp4", line);
        // System.String.Substring is not our frame; it collapses to a count instead of a kilobyte
        // of PresentationFramework.
        Assert.Contains("framework frames", line);
    }

    [Fact]
    public void VerboseIsOptIn()
    {
        Assert.False(LogPipeline.VerboseRequested(new[] { "--hidden" }));
        Assert.True(LogPipeline.VerboseRequested(new[] { "--verbose" }));
    }
}
