using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Logging;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// One log file per RUN, and what bounds it.
///
/// <para>The daily <c>app-YYYYMMDD.log</c> was the wrong unit: a user who has to relaunch after a
/// freeze in order to file a report was appending the relaunch to the very file that held the
/// evidence. A session file makes "the log" and "the thing that went wrong" the same object - but
/// only if the folder cannot grow without bound and the file says, at the top and the bottom, which
/// run it was and what that run cost.</para>
/// </summary>
public class SessionLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-sess-" + Guid.NewGuid().ToString("N"));
    private static readonly MessageTemplateParser Parser = new();

    public SessionLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* swallow: temp dir */ }
    }

    private void Session(string stamp, int bytes = 16)
        => File.WriteAllText(Path.Combine(_dir, "session-" + stamp + ".log"), new string('x', bytes));

    private string[] Sessions() => Directory.GetFiles(_dir, "session-*.log");

    [Fact]
    public void Prune_KeepsTheNewestTwenty()
    {
        for (int i = 0; i < 25; i++) Session($"202609{i:D2}-000000");

        SessionLog.Prune(_dir);

        var left = Sessions().Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(SessionLog.MaxSessionFiles, left.Length);
        // Oldest went, newest stayed. A retention that kept the oldest 20 would be worse than none.
        Assert.DoesNotContain("session-20260900-000000.log", left);
        Assert.Contains("session-20260924-000000.log", left);
    }

    [Fact]
    public void Prune_AlsoCapsTotalSize()
    {
        // Twenty files is a fine cap until a crash loop makes each one enormous: 5 x 8 MB is
        // already the whole allowance, so the count limit alone would let the folder reach 160 MB.
        for (int i = 0; i < 5; i++) Session($"202609{i:D2}-000000", bytes: 6 * 1024 * 1024);

        SessionLog.Prune(_dir);

        long total = Sessions().Sum(f => new FileInfo(f).Length);
        Assert.True(total <= SessionLog.MaxSessionBytes, $"folder still holds {total} bytes");
        Assert.True(Sessions().Length < 5);
        // The newest survives the size sweep, because it is the one anyone would read.
        Assert.Contains(Sessions(), f => f.EndsWith("session-20260904-000000.log", StringComparison.Ordinal));
    }

    [Fact]
    public void SweepLegacyAppLogs_RemovesTheOldDailyFilesOnly()
    {
        var stale = Path.Combine(_dir, "app-20260101.log");
        var fresh = Path.Combine(_dir, "app-20260903.log");
        File.WriteAllText(stale, "old");
        File.WriteAllText(fresh, "recent");
        File.SetLastWriteTime(stale, DateTime.Now.AddDays(-SessionLog.LegacyAppLogDays - 1));
        Session("20260904-000000");

        SessionLog.SweepLegacyAppLogs(_dir);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));   // inside the window: an upgrading user keeps recent history
        Assert.Single(Sessions());          // and the sweep never touches session files
    }

    [Fact]
    public void Prepare_WritesTheHeaderAndReturnsThisSessionsPath()
    {
        var path = SessionLog.Prepare(_dir, "20260904-101112", "6.9.2", "en", "bambisleep");

        Assert.EndsWith("session-20260904-101112.log", path);
        var header = File.ReadAllLines(path);
        Assert.StartsWith("== CCP v6.9.2 session ", header[0]);
        Assert.Contains(" pid ", header[0]);
        Assert.Contains("lang=en", header[1]);
        Assert.Contains("mod=bambisleep", header[1]);
        // Paths appear as the redactor's own tokens. The header is the first thing a user pastes
        // into Discord, so it is the last place to print C:\Users\<their real name>.
        Assert.Contains("install=%APP%", header[1]);
        Assert.Contains("data=%DATA%", header[1]);
        Assert.DoesNotContain(":\\Users\\", header[1]);
    }

    [Fact]
    public void Footer_StatesWhatTheRunCost()
    {
        var path = SessionLog.Prepare(_dir, "20260904-101112", "6.9.2", "en", "none");
        SessionLog.WriteFooter(_dir, "20260904-101112", TimeSpan.FromSeconds(3725),
            warnings: 4, errors: 2, suppressed: 380, swallowed: 91);

        var last = File.ReadAllLines(path)[^1];
        // "warn=0 err=0" on the last line answers a support thread without opening the file, and
        // "swallowed=" is the only place the deliberately-ignored exceptions are ever counted: past
        // their per-site budget they stop being logged at all.
        Assert.Equal("== end: uptime 1:02:05 warn=4 err=2 suppressed=380 swallowed=91 ==", last);
    }

    [Fact]
    public void Footer_ListsTheBusiestSwallowSites_WhileTheListIsShort()
    {
        var path = SessionLog.Prepare(_dir, "20260904-101113", "6.9.2", "en", "none");
        SessionLog.WriteFooter(_dir, "20260904-101113", TimeSpan.FromSeconds(60),
            warnings: 0, errors: 0, suppressed: 0, swallowed: 12,
            swallowSummary: "VideoService.cs:412 9\nApp.xaml.cs:1204 3");

        var lines = File.ReadAllLines(path);
        Assert.Contains("swallowed=12", lines[^3]);
        Assert.Equal("   swallowed at VideoService.cs:412 9", lines[^2]);
        Assert.Equal("   swallowed at App.xaml.cs:1204 3", lines[^1]);
    }

    [Fact]
    public void Footer_DropsTheSiteListRatherThanTruncateIt()
    {
        var path = SessionLog.Prepare(_dir, "20260904-101114", "6.9.2", "en", "none");
        var many = string.Join("\n", Enumerable.Range(0, SessionLog.MaxSwallowSites + 1).Select(i => $"F{i}.cs:1 {i}"));
        SessionLog.WriteFooter(_dir, "20260904-101114", TimeSpan.FromSeconds(60),
            warnings: 0, errors: 0, suppressed: 0, swallowed: 99, swallowSummary: many);

        // A truncated top-five tells a reader less than the count already did, so the footer stays
        // one line.
        var lines = File.ReadAllLines(path);
        Assert.Contains("swallowed=99", lines[^1]);
        Assert.DoesNotContain(lines, l => l.Contains("swallowed at"));
    }

    [Fact]
    public void CountingSink_CountsWarningsAndErrorsSeparately()
    {
        var sink = new CountingSink();
        sink.Emit(Event(LogEventLevel.Information));
        sink.Emit(Event(LogEventLevel.Warning));
        sink.Emit(Event(LogEventLevel.Warning));
        sink.Emit(Event(LogEventLevel.Error));
        sink.Emit(Event(LogEventLevel.Fatal));   // a Fatal is an error, not a category of its own

        Assert.Equal(2, sink.Warnings);
        Assert.Equal(2, sink.Errors);
    }

    [Fact]
    public void ThrottleReportsWhatTheWholeSessionDropped()
    {
        // The per-template summaries are scattered through the file and do not add themselves up;
        // the footer needs one number.
        var sink = new ThrottlingSink(new CountingSink());
        for (int i = 0; i < ThrottlingSink.BurstLimit + 7; i++)
            sink.Emit(Event(LogEventLevel.Information, "[EMOTE] xfade -> {Emote}"));

        Assert.Equal(7, sink.TotalSuppressed);
    }

    private static LogEvent Event(LogEventLevel level, string template = "[TEST] line") =>
        new(DateTimeOffset.Now, level, null, Parser.Parse(template), Array.Empty<LogEventProperty>());
}
