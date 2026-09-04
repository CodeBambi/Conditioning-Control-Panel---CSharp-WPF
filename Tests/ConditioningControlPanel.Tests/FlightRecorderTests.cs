using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Logging;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The flight recorder: Debug detail kept in memory and written out only when something goes wrong.
///
/// <para>The app makes 2,927 Debug calls that have never reached a disk, because the floor has been
/// Information for years. Every freeze and black-video report therefore arrives with the run-up to
/// the failure already discarded. These pin the three properties that make the ring worth having:
/// it keeps the NEWEST events (a ring that dropped the recent ones would be worse than nothing), it
/// writes on an error but not on the cancellations that are ordinary shutdown, and it does not fill
/// the logs folder with dumps.</para>
/// </summary>
public class FlightRecorderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-fr-" + Guid.NewGuid().ToString("N"));
    private static readonly MessageTemplateParser Parser = new();

    public FlightRecorderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* swallow: temp dir */ }
    }

    private static LogEvent Event(string template, LogEventLevel level = LogEventLevel.Debug, Exception? ex = null) =>
        new(DateTimeOffset.Now, level, ex, Parser.Parse(template), Array.Empty<LogEventProperty>());

    private string[] Dumps() => Directory.GetFiles(_dir, "diag-*.log");

    [Fact]
    public void TheRingKeepsTheNewestEvents()
    {
        var sink = new FlightRecorderSink(_dir, "test");
        for (int i = 0; i < FlightRecorderSink.Capacity + 50; i++)
            sink.Emit(Event("[TEST] line " + i));

        var path = sink.Dump("manual");
        Assert.NotNull(path);
        var text = File.ReadAllText(path!);

        Assert.Contains("line " + (FlightRecorderSink.Capacity + 49), text);
        // The first 50 were overwritten, which is the whole point of a bounded ring.
        Assert.DoesNotContain("] line 0" + Environment.NewLine, text);
        Assert.Contains("reason=manual", text);
    }

    [Fact]
    public void AnErrorDumpsTheRing_OnceAMinute()
    {
        var sink = new FlightRecorderSink(_dir, "test");
        sink.Emit(Event("[TEST] context"));
        sink.Emit(Event("[TEST] boom", LogEventLevel.Error, new InvalidOperationException("boom")));
        Assert.Single(Dumps());

        // A crash loop must not become a disk loop: the second error inside the window is silent.
        sink.Emit(Event("[TEST] boom", LogEventLevel.Error, new InvalidOperationException("boom")));
        Assert.Single(Dumps());
    }

    [Fact]
    public void CancellationAtShutdownDoesNotDump()
    {
        // Timers and in-flight HTTP calls throw these on every clean exit. Dumping for them would
        // mean a diag file after every session, burying the ones that mean something. There is no
        // WPF Application in a test host, which is the same "we are shutting down" answer.
        var sink = new FlightRecorderSink(_dir, "test");
        var restore = FlightRecorderSink.ShuttingDown;
        FlightRecorderSink.ShuttingDown = () => true;
        try
        {
            sink.Emit(Event("[TEST] cancelled", LogEventLevel.Error, new TaskCanceledException()));
            Assert.Empty(Dumps());

            // The same exception while the app is actually running is a real failure and dumps.
            FlightRecorderSink.ShuttingDown = () => false;
            sink.Emit(Event("[TEST] cancelled", LogEventLevel.Error, new TaskCanceledException()));
            Assert.Single(Dumps());
        }
        finally
        {
            FlightRecorderSink.ShuttingDown = restore;
        }
    }

    [Fact]
    public void OnlyTheNewestFiveDumpsAreKept()
    {
        for (int i = 0; i < 7; i++)
            File.WriteAllText(Path.Combine(_dir, $"diag-2026090{i}-000000-old.log"), "old");

        var sink = new FlightRecorderSink(_dir, "test");
        sink.Emit(Event("[TEST] something"));
        sink.Dump("manual");

        Assert.Equal(FlightRecorderSink.KeepDumps, Dumps().Length);
        // Newest survive: the two oldest stamps are the ones that went.
        Assert.DoesNotContain(Dumps(), f => f.Contains("20260900") || f.Contains("20260901"));
    }

    [Fact]
    public void NewestDump_IsTheOneTheBugReporterAttaches()
    {
        File.WriteAllText(Path.Combine(_dir, "diag-20260901-000000-crash.log"), "old");
        File.WriteAllText(Path.Combine(_dir, "diag-20260903-000000-hang.log"), "new");
        Assert.EndsWith("diag-20260903-000000-hang.log", FlightRecorderSink.NewestDump(_dir));
    }

    [Fact]
    public void TheAppLogFieldIsCappedBelowTheServerLimit()
    {
        // The server rejects an appLog over 200,000 chars, and the ring dump is large enough to
        // reach that on its own now. The cap keeps the TAIL, because the tail is the failure.
        var text = new string('x', 250_000) + "END";
        var capped = BugReportService.Tail(text, BugReportService.MaxAppLogFieldChars);
        Assert.Equal(BugReportService.MaxAppLogFieldChars, capped.Length);
        Assert.EndsWith("END", capped);
    }
}
