using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace ConditioningControlPanel.Tests
{
    /// <summary>
    /// Covers <see cref="Diag.Swallowed"/>: the first hit at a site is loud (one Warning), the next
    /// nine are Debug only, and everything past the tenth is counted but silent. The cap is what
    /// makes it safe to call from render loops and timer ticks.
    ///
    /// The tests are serialised because <see cref="Diag"/> keeps per-session static state.
    /// </summary>
    [Collection("DiagSwallow")]
    public class DiagSwallowTests : IDisposable
    {
        private readonly CaptureSink _sink = new();

        public DiagSwallowTests()
        {
            Diag.ResetForTests();
            Diag.LoggerOverride = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(_sink)
                .CreateLogger();
        }

        public void Dispose() => Diag.ResetForTests();

        [Fact]
        public void FirstHitAtASiteWarnsOnce()
        {
            Diag.Swallowed(new InvalidOperationException("boom"));

            var warnings = _sink.Events.Where(e => e.Level == LogEventLevel.Warning).ToList();
            Assert.Single(warnings);
            Assert.Equal("[Swallow] {Site} {Member} {ExType}: {ExMessage}", warnings[0].MessageTemplate.Text);
            Assert.StartsWith("DiagSwallowTests.cs:", Scalar(warnings[0], "Site"));
            Assert.Equal(nameof(FirstHitAtASiteWarnsOnce), Scalar(warnings[0], "Member"));
            Assert.Equal("InvalidOperationException", Scalar(warnings[0], "ExType"));
            Assert.Equal("boom", Scalar(warnings[0], "ExMessage"));
            Assert.Equal(1, Diag.SwallowCount);
        }

        [Fact]
        public void RepeatHitsAtTheSameSiteLogDebugOnlyOnce()
        {
            for (int i = 0; i < 3; i++) Diag.Swallowed(new Exception("again"));

            Assert.Single(_sink.Events, e => e.Level == LogEventLevel.Warning);
            Assert.Equal(3, _sink.Events.Count(e => e.Level == LogEventLevel.Debug));
            Assert.Equal(3, Diag.SwallowCount);
        }

        [Fact]
        public void PastTheTenthHitOnlyTheCounterMoves()
        {
            for (int i = 0; i < 11; i++) Diag.Swallowed(new Exception("loop"));

            // 1 Warning + 10 Debug; the 11th hit is silent but still counted.
            Assert.Single(_sink.Events, e => e.Level == LogEventLevel.Warning);
            Assert.Equal(10, _sink.Events.Count(e => e.Level == LogEventLevel.Debug));
            Assert.Equal(11, Diag.SwallowCount);
        }

        [Fact]
        public void NoteIsAppendedWhenGiven()
        {
            Diag.Swallowed(new Exception("x"), "window tearing down");

            var warning = _sink.Events.First(e => e.Level == LogEventLevel.Warning);
            Assert.EndsWith(" ({Note})", warning.MessageTemplate.Text);
            Assert.Equal("window tearing down", Scalar(warning, "Note"));
        }

        private static string Scalar(LogEvent e, string property)
            => Assert.IsType<ScalarValue>(e.Properties[property]).Value as string ?? string.Empty;

        [Fact]
        public void SummaryListsTheSiteWithItsCount()
        {
            for (int i = 0; i < 4; i++) Diag.Swallowed(new Exception("s"));

            var summary = Diag.SwallowSummary();
            Assert.Contains("DiagSwallowTests.cs:", summary);
            Assert.EndsWith(" 4", summary);
            Assert.Equal(1, Diag.SwallowSiteCount);
        }

        [Fact]
        public void SummaryIsOrderedByCountAndHonoursTop()
        {
            SiteA();
            SiteA();
            SiteB();

            var lines = Diag.SwallowSummary().Split('\n');
            Assert.Equal(2, lines.Length);
            Assert.EndsWith(" 2", lines[0]);
            Assert.EndsWith(" 1", lines[1]);
            Assert.Single(Diag.SwallowSummary(1).Split('\n'));
        }

        [Fact]
        public void SurvivesAMissingLoggerAndANullException()
        {
            Diag.LoggerOverride = null; // App.Logger is null in the test host
            Diag.Swallowed(null!);
            Assert.Equal(1, Diag.SwallowCount);
        }

        private static void SiteA() => Diag.Swallowed(new Exception("a"));

        private static void SiteB() => Diag.Swallowed(new Exception("b"));

        private sealed class CaptureSink : ILogEventSink
        {
            private readonly List<LogEvent> _events = new();

            public IReadOnlyList<LogEvent> Events
            {
                get { lock (_events) return _events.ToList(); }
            }

            public void Emit(LogEvent logEvent)
            {
                lock (_events) _events.Add(logEvent);
            }
        }
    }

    [CollectionDefinition("DiagSwallow", DisableParallelization = true)]
    public class DiagSwallowCollection { }
}
