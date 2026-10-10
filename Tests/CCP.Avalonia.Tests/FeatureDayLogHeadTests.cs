using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The head's readers of the feature day log (WPF ChasterService.App.cs:62 MinutesFromDayLog,
/// LeashService.AppDayInputs): no log at all = nobody can tell (null); a day with no entry had no
/// minutes (0). Swaps the process-wide service, so it runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FeatureDayLogHeadTests
{
    [Fact]
    public void TheIdleDayReader_SaysNullWithoutALog_ZeroForAnEmptyDay_AndTheMinutesOtherwise()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-daylog-head-" + Guid.NewGuid().ToString("N"));
        var before = FeatureDayLogService.Current;
        try
        {
            FeatureDayLogService.Current = null;
            Assert.Null(ChasterHead.MinutesFromDayLog("2026-10-09"));

            var counters = new Dictionary<string, double> { ["cm"] = 0 };
            using var log = new FeatureDayLogService(Path.Combine(dir, "feature_day_log.json"), () => counters, () => new DateTime(2026, 10, 9), startTimer: false);
            FeatureDayLogService.Current = log;
            Assert.Equal(0, ChasterHead.MinutesFromDayLog("2026-10-09"));

            counters["cm"] = 25.4;
            log.Tick();
            Assert.Equal(25, ChasterHead.MinutesFromDayLog("2026-10-09"));
            Assert.Equal(0, ChasterHead.MinutesFromDayLog("2026-10-08"));
        }
        finally
        {
            FeatureDayLogService.Current = before;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
