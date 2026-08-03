using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// c8fa5db5 — sampled-diagnostics rescue for bug reports (#634 + the v6.5.0 freeze wave). The
/// plain last-100-line app-log tail was pure startup chatter after the forced relaunch that a
/// hung/hard-reset PC needs to file a report, scrolling the [RES]/[WATCHDOG] timeline out of
/// every report. BuildSampledDiagnostics scans a much wider tail (MaxDiagScanLines), keeps only
/// the marker lines (DiagMarkers), retains the MaxDiagMatches most-recent in order, and caps the
/// joined section at MaxDiagSectionChars. These tests pin that contract headlessly.
/// </summary>
public class BugReportDiagnosticsTests
{
    private static string[] MarkerLine(string marker, int n) =>
        new[] { $"2026-07-24 12:00:{n:D2} {marker} sample n={n}" };

    [Fact]
    public void MarkerLines_Captured_UnmarkedLines_Dropped()
    {
        var lines = new List<string>
        {
            "2026-07-24 12:00:00 INF plain startup line",
            "2026-07-24 12:00:01 INF [RES] user=100 gdi=200",
            "2026-07-24 12:00:02 INF nothing interesting here",
            "2026-07-24 12:00:03 ERR [WATCHDOG] UI thread unresponsive",
            "2026-07-24 12:00:04 INF [BLUR] applied",
            "2026-07-24 12:00:05 INF [VIDEO] play",
            "2026-07-24 12:00:06 INF [VideoDiag] transition",
            "2026-07-24 12:00:07 INF another plain line",
        };

        var result = BugReportService.BuildSampledDiagnostics(lines);

        Assert.Contains("[RES] user=100", result);
        Assert.Contains("[WATCHDOG] UI thread unresponsive", result);
        Assert.Contains("[BLUR] applied", result);
        Assert.Contains("[VIDEO] play", result);
        Assert.Contains("[VideoDiag] transition", result);
        Assert.DoesNotContain("plain startup line", result);
        Assert.DoesNotContain("nothing interesting", result);
        Assert.DoesNotContain("another plain line", result);
        // All five marker lines survived — one per output line.
        Assert.Equal(5, result.Split(new[] { Environment.NewLine }, StringSplitOptions.None).Length);
    }

    [Fact]
    public void NoMatch_YieldsEmpty()
    {
        var lines = Enumerable.Range(0, 50).Select(i => $"line {i} with no markers").ToList();
        Assert.Equal(string.Empty, BugReportService.BuildSampledDiagnostics(lines));
    }

    [Fact]
    public void EmptyOrNull_Input_YieldsEmpty()
    {
        Assert.Equal(string.Empty, BugReportService.BuildSampledDiagnostics(Array.Empty<string>()));
        Assert.Equal(string.Empty, BugReportService.BuildSampledDiagnostics(null!));
    }

    [Fact]
    public void Ordering_Preserved_Chronologically()
    {
        var lines = new List<string>
        {
            "[RES] first",
            "noise",
            "[WATCHDOG] second",
            "noise",
            "[RES] third",
        };

        var result = BugReportService.BuildSampledDiagnostics(lines);
        var outLines = result.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        Assert.Equal(3, outLines.Length);
        Assert.Equal("[RES] first", outLines[0]);
        Assert.Equal("[WATCHDOG] second", outLines[1]);
        Assert.Equal("[RES] third", outLines[2]);
    }

    [Fact]
    public void OnlyLast_MaxDiagScanLines_AreScanned()
    {
        // An [RES] marker sitting OLDER than the scan window must be excluded, even though it
        // matches — it has already scrolled out of the wide tail the reader loads.
        var lines = new List<string>();
        lines.Add("[WATCHDOG] ANCIENT — outside the scan window");     // index 0
        for (int i = 0; i < BugReportService.MaxDiagScanLines; i++)     // pushes the marker out of the last-N window
            lines.Add($"filler startup chatter {i}");
        // total = MaxDiagScanLines + 1, so scan starts at index 1 and the marker at 0 is skipped.

        Assert.Equal(string.Empty, BugReportService.BuildSampledDiagnostics(lines));

        // Sanity: a marker at the very tail (inside the window) IS captured.
        lines.Add("[RES] recent — inside the window");
        var result = BugReportService.BuildSampledDiagnostics(lines);
        Assert.Contains("[RES] recent", result);
        Assert.DoesNotContain("ANCIENT", result);
    }

    [Fact]
    public void AtMost_MaxDiagMatches_Kept_MostRecent()
    {
        int total = BugReportService.MaxDiagMatches + 60; // more matches than the cap
        var lines = Enumerable.Range(0, total)
            .Select(i => $"[RES] sample n={i}")
            .ToList();

        var result = BugReportService.BuildSampledDiagnostics(lines);
        var outLines = result.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        Assert.Equal(BugReportService.MaxDiagMatches, outLines.Length);
        // The kept window is the MOST RECENT matches, in chronological order.
        int firstKept = total - BugReportService.MaxDiagMatches;
        Assert.Equal($"[RES] sample n={firstKept}", outLines[0]);
        Assert.Equal($"[RES] sample n={total - 1}", outLines[^1]);
        Assert.DoesNotContain($"n={firstKept - 1}\r\n", result); // the one just before the window is gone
        Assert.DoesNotContain("n=0 ", result + " ");
    }

    [Fact]
    public void Section_Capped_At_MaxDiagSectionChars()
    {
        // MaxDiagMatches lines, each long enough that the joined section blows the char budget.
        string big = new string('x', 800);
        var lines = Enumerable.Range(0, BugReportService.MaxDiagMatches)
            .Select(i => $"[RES] {big} {i}")
            .ToList();

        var result = BugReportService.BuildSampledDiagnostics(lines);

        Assert.True(result.Length <= BugReportService.MaxDiagSectionChars,
            $"section length {result.Length} must not exceed {BugReportService.MaxDiagSectionChars}");
        // The overflow was real, so the cap actually bit and kept exactly the budget.
        Assert.Equal(BugReportService.MaxDiagSectionChars, result.Length);
        // Newest bytes are retained: the tail keeps the last line's trailing index.
        Assert.EndsWith($"{BugReportService.MaxDiagMatches - 1}", result);
    }

    [Fact]
    public void CarriageReturns_Trimmed()
    {
        var lines = new[] { "[RES] windows line\r" };
        var result = BugReportService.BuildSampledDiagnostics(lines);
        Assert.Equal("[RES] windows line", result);
        Assert.DoesNotContain("\r", result);
    }

    // ------------------------------------------------------------------
    // #769 — remembered report numbers (AppSettings.RecentBugReports)
    // ------------------------------------------------------------------

    [Fact]
    public void RecentReports_EntryFormat_IsTokenPipeStampPipeKind()
    {
        var list = new List<string>();
        var when = new DateTime(2026, 8, 3, 14, 30, 0, DateTimeKind.Utc);

        BugReportService.AppendRecentReport(list, "BUG-0123456789", when, BugReportService.ReportKind.Bug);
        BugReportService.AppendRecentReport(list, "BUG-9876543210", when, BugReportService.ReportKind.Suggestion);

        Assert.Equal(2, list.Count);
        Assert.StartsWith("BUG-0123456789|2026-08-03T14:30:00", list[0]);
        Assert.EndsWith("|bug", list[0]);
        Assert.EndsWith("|suggestion", list[1]);
    }

    [Fact]
    public void RecentReports_Capped_At_MaxRecentReports_OldestTrimmed()
    {
        var list = new List<string>();
        int total = BugReportService.MaxRecentReports + 15;
        for (int i = 0; i < total; i++)
            BugReportService.AppendRecentReport(list, $"BUG-{i:D10}", DateTime.UtcNow, BugReportService.ReportKind.Bug);

        Assert.Equal(BugReportService.MaxRecentReports, list.Count);
        // The kept window is the NEWEST entries, still oldest-first in storage order.
        int firstKept = total - BugReportService.MaxRecentReports;
        Assert.StartsWith($"BUG-{firstKept:D10}|", list[0]);
        Assert.StartsWith($"BUG-{total - 1:D10}|", list[^1]);
        Assert.DoesNotContain(list, e => e.StartsWith($"BUG-{firstKept - 1:D10}|", StringComparison.Ordinal));
    }

    [Fact]
    public void RecentReports_BlankToken_And_NullList_Ignored()
    {
        var list = new List<string>();
        BugReportService.AppendRecentReport(list, null, DateTime.UtcNow, BugReportService.ReportKind.Bug);
        BugReportService.AppendRecentReport(list, "   ", DateTime.UtcNow, BugReportService.ReportKind.Bug);
        Assert.Empty(list);

        // Null list must not throw.
        BugReportService.AppendRecentReport(null!, "BUG-0000000001", DateTime.UtcNow, BugReportService.ReportKind.Bug);
    }

    [Fact]
    public void RecentReports_Parsed_NewestFirst_WithKindAndStamp()
    {
        var list = new List<string>();
        BugReportService.AppendRecentReport(list, "BUG-1111111111",
            new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc), BugReportService.ReportKind.Bug);
        BugReportService.AppendRecentReport(list, "BUG-2222222222",
            new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc), BugReportService.ReportKind.Suggestion);

        var rows = BugReportService.ParseRecentReports(list);

        Assert.Equal(2, rows.Count);
        Assert.Equal("BUG-2222222222", rows[0].Token);           // newest first
        Assert.Equal(BugReportService.ReportKind.Suggestion, rows[0].Kind);
        Assert.Equal(new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc), rows[0].TimestampUtc);
        Assert.Equal("BUG-1111111111", rows[1].Token);
        Assert.Equal(BugReportService.ReportKind.Bug, rows[1].Kind);
    }

    [Fact]
    public void RecentReports_Parse_Tolerates_Legacy_And_Junk_Entries()
    {
        var rows = BugReportService.ParseRecentReports(new[]
        {
            "BUG-BARE0000001",              // legacy: token only, no stamp/kind
            "   ",                          // blank — dropped
            "BUG-BADSTAMP01|not-a-date|bug",
            "|2026-08-01T10:00:00.0000000Z|bug", // empty token — dropped
        });

        Assert.Equal(2, rows.Count);
        Assert.Equal("BUG-BADSTAMP01", rows[0].Token);
        Assert.Null(rows[0].TimestampUtc);
        Assert.Equal("BUG-BARE0000001", rows[1].Token);
        Assert.Null(rows[1].TimestampUtc);
        Assert.Equal(BugReportService.ReportKind.Bug, rows[1].Kind);

        Assert.Empty(BugReportService.ParseRecentReports(null));
    }
}
