using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The per-trigger ceiling on Chaster time from Awareness keyword triggers
/// (<see cref="KeywordTriggerChasterCap"/>) and the import side that makes a preset's lock time
/// arrive switched off until the player says yes (<see cref="KeywordTriggerChasterImport"/>).
/// </summary>
public class KeywordTriggerChasterCapTests
{
    private static readonly DateTime Morning = new(2026, 9, 24, 9, 0, 0);

    // ---- the cap -------------------------------------------------------------------------

    [Fact]
    public void ThreeBookingsADay_ThenNothing()
    {
        var cap = new KeywordTriggerChasterCap();
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(60, cap.Allowance("t1", 60, Morning));
            cap.Record("t1", 60, Morning);
        }
        Assert.Equal(0, cap.Allowance("t1", 60, Morning.AddHours(5)));
    }

    [Fact]
    public void FifteenMinutesADay_LastBookingIsTrimmed()
    {
        var cap = new KeywordTriggerChasterCap();
        cap.Record("t1", 10 * 60, Morning);
        // 5 minutes left: a 10 minute action books only 5.
        Assert.Equal(5 * 60, cap.Allowance("t1", 10 * 60, Morning));
        cap.Record("t1", 5 * 60, Morning);
        Assert.Equal(0, cap.Allowance("t1", 60, Morning));
    }

    [Fact]
    public void OneBookingNeverExceedsTheDayCap()
    {
        var cap = new KeywordTriggerChasterCap();
        Assert.Equal(KeywordTriggerChasterCap.MaxSecondsPerDay, cap.Allowance("t1", 60 * 60, Morning));
    }

    [Fact]
    public void TriggersHaveSeparateAllowances()
    {
        var cap = new KeywordTriggerChasterCap();
        cap.Record("t1", 15 * 60, Morning);
        Assert.Equal(0, cap.Allowance("t1", 60, Morning));
        Assert.Equal(60, cap.Allowance("t2", 60, Morning));
    }

    [Fact]
    public void ANewLocalDayResets()
    {
        var cap = new KeywordTriggerChasterCap();
        for (int i = 0; i < 3; i++) cap.Record("t1", 60, Morning);
        Assert.Equal(0, cap.Allowance("t1", 60, new DateTime(2026, 9, 24, 23, 59, 0)));
        Assert.Equal(60, cap.Allowance("t1", 60, new DateTime(2026, 9, 25, 0, 1, 0)));
    }

    [Fact]
    public void ABookingThatLandedNothing_IsNotCounted()
    {
        var cap = new KeywordTriggerChasterCap();
        for (int i = 0; i < 10; i++) cap.Record("t1", 0, Morning);
        Assert.Equal(60, cap.Allowance("t1", 60, Morning));
    }

    [Fact]
    public void BadInputBooksNothing()
    {
        var cap = new KeywordTriggerChasterCap();
        Assert.Equal(0, cap.Allowance("", 60, Morning));
        Assert.Equal(0, cap.Allowance("t1", 0, Morning));
        Assert.Equal(0, cap.Allowance("t1", -60, Morning));
    }

    [Fact]
    public void StateSurvivesARestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-cap-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cap = new KeywordTriggerChasterCap();
            cap.Record("preset:builtin.chastity:a", 5 * 60, Morning);
            cap.Record("preset:builtin.chastity:a", 5 * 60, Morning);
            cap.Save(path);

            var back = KeywordTriggerChasterCap.Load(path);
            Assert.Equal(5 * 60, back.Allowance("preset:builtin.chastity:a", 10 * 60, Morning));
            back.Record("preset:builtin.chastity:a", 60, Morning);
            Assert.Equal(0, back.Allowance("preset:builtin.chastity:a", 60, Morning));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AHandEditedFileCannotBuyMoreTime()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-cap-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["t1"] = new { day = KeywordTriggerChasterCap.DayKey(Morning), fires = -50, seconds = -9000 },
            }));
            var cap = KeywordTriggerChasterCap.Load(path);
            Assert.Equal(KeywordTriggerChasterCap.MaxSecondsPerDay, cap.Allowance("t1", 60 * 60, Morning));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AMissingOrBrokenFileStartsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-cap-" + Guid.NewGuid().ToString("N") + ".json");
        Assert.Equal(60, KeywordTriggerChasterCap.Load(path).Allowance("t1", 60, Morning));
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Equal(60, KeywordTriggerChasterCap.Load(path).Allowance("t1", 60, Morning));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OldDaysAreDroppedFromTheFile()
    {
        var cap = new KeywordTriggerChasterCap();
        cap.Record("old", 60, Morning.AddDays(-1));
        cap.Record("new", 60, Morning);
        Assert.False(cap.State.ContainsKey("old"));
        Assert.True(cap.State.ContainsKey("new"));
    }

    // ---- the import side -----------------------------------------------------------------

    private static KeywordTrigger Trigger(params KeywordAction[] actions)
        => new() { Id = Guid.NewGuid().ToString("N"), Keyword = "k", Actions = actions.ToList() };

    [Fact]
    public void Summary_CountsEnabledChasterActionsAndTheirMinutes()
    {
        var triggers = new[]
        {
            Trigger(new ChasterAddTimeAction { Minutes = 5 }, new HighlightAction()),
            Trigger(new ChasterAddTimeAction { Minutes = 10 }),
            Trigger(new ChasterAddTimeAction { Minutes = 30, Enabled = false }),
            Trigger(new HapticAction()),
        };
        var s = KeywordTriggerChasterImport.Summarise(triggers);
        Assert.Equal(2, s.Count);
        Assert.Equal(15, s.MinutesPerFire);
        Assert.True(s.Any);
    }

    [Fact]
    public void Summary_CountsEachActionAtMostTheDayCap()
    {
        var s = KeywordTriggerChasterImport.Summarise(new[] { Trigger(new ChasterAddTimeAction { Minutes = 60 }) });
        Assert.Equal(15, s.MinutesPerFire);
    }

    [Fact]
    public void Summary_EmptyForNoTriggers()
    {
        Assert.False(KeywordTriggerChasterImport.Summarise(null).Any);
        Assert.False(KeywordTriggerChasterImport.Summarise(new[] { Trigger(new HighlightAction()) }).Any);
    }

    [Fact]
    public void DisableAll_TurnsOffOnlyChasterActions()
    {
        var hi = new HighlightAction();
        var ch = new ChasterAddTimeAction { Minutes = 5 };
        var triggers = new[] { Trigger(hi, ch) };

        Assert.Equal(1, KeywordTriggerChasterImport.DisableAll(triggers));
        Assert.False(ch.Enabled);
        Assert.True(hi.Enabled);
        Assert.True(triggers[0].Enabled);
        Assert.False(KeywordTriggerChasterImport.AnyEnabled(triggers));
    }

    [Fact]
    public void BundledChastityPreset_IsCaughtByTheConfirm()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Resources", "AwarenessPresets", "chastity.json"));
        var preset = JsonConvert.DeserializeObject<KeywordTriggerPreset>(json);

        var s = KeywordTriggerChasterImport.Summarise(preset!.Triggers);
        Assert.True(s.Count > 0, "chastity.json carries lock time, so activating it must ask first");

        var clones = preset.Triggers.Select(t => t.Clone()).ToList();
        KeywordTriggerChasterImport.DisableAll(clones);
        Assert.False(KeywordTriggerChasterImport.AnyEnabled(clones));
        // The source preset is untouched by disabling the clones.
        Assert.True(KeywordTriggerChasterImport.AnyEnabled(preset.Triggers));
    }
}
