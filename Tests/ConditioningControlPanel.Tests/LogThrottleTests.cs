using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The repeat cap. A week of real logs is 34,500 lines and a third of the BYTES are three templates
/// repeating - an emote crossfade every five seconds, a blocked bark, a profile-sync response - so
/// the lines that matter sit buried between copies of the ones that do not.
///
/// <para>The cap is only useful if the count survives: silently dropping the 21st copy would turn a
/// crash loop into a quiet log, which is worse than the noise. These pin both halves - what passes,
/// and that the suppressed count is reported rather than lost.</para>
/// </summary>
public class LogThrottleTests
{
    private sealed class Capture : ILogEventSink
    {
        public readonly List<LogEvent> Events = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static readonly MessageTemplateParser Parser = new();

    private static LogEvent Event(string template, string cat = "Emote",
        LogEventLevel level = LogEventLevel.Information) =>
        new(DateTimeOffset.Now, level, null, Parser.Parse(template),
            new[] { new LogEventProperty(CategoryEnricher.PropertyName, new ScalarValue(cat)) });

    [Fact]
    public void FirstTwentyPass_TheRestAreCounted()
    {
        var capture = new Capture();
        var sink = new ThrottlingSink(capture);

        for (int i = 0; i < 500; i++) sink.Emit(Event("[EMOTE] xfade to {Emote}"));

        Assert.Equal(ThrottlingSink.BurstLimit, capture.Events.Count);

        // Nothing is lost: the 480 that did not pass are reported as one line on flush.
        sink.Flush();
        var summary = Assert.Single(capture.Events.GetRange(ThrottlingSink.BurstLimit,
            capture.Events.Count - ThrottlingSink.BurstLimit));
        Assert.Contains("suppressed", summary.MessageTemplate.Text);
        Assert.Equal(480, ((ScalarValue)summary.Properties["Count"]).Value);
        Assert.Equal("[EMOTE] xfade to {Emote}", ((ScalarValue)summary.Properties["Template"]).Value);
        // The summary carries the category so it lands in the same column as the lines it counts.
        Assert.Equal("Emote", ((ScalarValue)summary.Properties[CategoryEnricher.PropertyName]).Value);
    }

    [Fact]
    public void TheKeyIsTheTemplate_NotTheRenderedText()
    {
        // "connected to 3 devices" and "connected to 4 devices" are one call site, so they share a
        // budget. If the key were the rendered text, a template with a counter in it would never
        // throttle at all - which is exactly the shape of the noisiest lines in the real log.
        var capture = new Capture();
        var sink = new ThrottlingSink(capture);
        for (int i = 0; i < 30; i++) sink.Emit(Event("[HAPTIC] connected to {N} devices"));
        Assert.Equal(ThrottlingSink.BurstLimit, capture.Events.Count);
    }

    [Fact]
    public void DifferentCategoriesAndTemplatesHaveTheirOwnBudget()
    {
        var capture = new Capture();
        var sink = new ThrottlingSink(capture);
        for (int i = 0; i < 30; i++)
        {
            sink.Emit(Event("[EMOTE] xfade"));
            sink.Emit(Event("[BARK] blocked", cat: "Bark"));
        }
        Assert.Equal(ThrottlingSink.BurstLimit * 2, capture.Events.Count);
    }

    [Fact]
    public void ErrorsAreThrottledToo_AndTheSummaryKeepsTheirLevel()
    {
        // Deliberate: a crash loop is the case that once wrote half a gigabyte, and a thousand
        // identical stack traces are not a thousand facts. But the summary must not demote itself
        // to Information, or an error storm disappears from an error-level read of the file.
        var capture = new Capture();
        var sink = new ThrottlingSink(capture);
        for (int i = 0; i < 100; i++) sink.Emit(Event("[VIDEO] open failed", level: LogEventLevel.Error));
        sink.Flush();

        Assert.Equal(ThrottlingSink.BurstLimit + 1, capture.Events.Count);
        Assert.Equal(LogEventLevel.Error, capture.Events[^1].Level);
    }

    [Fact]
    public void DisposeFlushesTheOutstandingCount()
    {
        var capture = new Capture();
        var sink = new ThrottlingSink(capture);
        for (int i = 0; i < 25; i++) sink.Emit(Event("[EMOTE] xfade"));
        sink.Dispose();
        Assert.Equal(ThrottlingSink.BurstLimit + 1, capture.Events.Count);
        Assert.Equal(5, ((ScalarValue)capture.Events[^1].Properties["Count"]).Value);
    }
}
