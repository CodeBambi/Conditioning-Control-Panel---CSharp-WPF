using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The leash safety pass (2026-09-28): a stopped task frees the gate button, and the runner
/// says why it stopped.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LeashSafetyUiTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    private static FrameworkElement? Find(DependencyObject root, Func<string, bool> tag)
    {
        if (root is FrameworkElement fe && fe.Tag is string s && tag(s)) return fe;
        foreach (var c in LogicalTreeHelper.GetChildren(root))
            if (c is DependencyObject d && Find(d, tag) is { } hit) return hit;
        return null;
    }

    private static void Run(Action body) => WpfRenderHarness.OnStaThread(() =>
    {
        LeashFx.ForceStill = true;
        try { body(); } finally { LeashFx.ForceStill = false; }
    });

    private static Punishment Pun(string pid, PunishKind kind = PunishKind.Lines) =>
        new(pid, kind, 3, kind == PunishKind.Video ? new LeashWatch("ht", "1", null) : null,
            FakeLeashService.Vex, T0, T0.AddHours(72));

    // ---- the gate ----------------------------------------------------------------------

    [Fact]
    public void A_stopped_task_frees_the_gate_button_and_keeps_the_pips()
    {
        Run(() =>
        {
            var gate = new LeashGateCard();
            gate.Present(Pun("p1"), 0);
            gate.SetProgress(2, 3, running: true);
            Assert.False(((Button)Find(gate, t => t == "leash-gate-go")!).IsEnabled);
            gate.StopRunning();
            Assert.False(gate.Running);
            Assert.True(((Button)Find(gate, t => t == "leash-gate-go")!).IsEnabled);
            Assert.Equal((2, 3), gate.ProgressShown);
        });
    }

    [Fact]
    public void Stop_lines_say_what_happened()
    {
        Assert.Equal("leash_stop_session", LeashUiRules.StopKey(new("p", false, LeashTaskStop.ActivityStopped), PunishKind.Pink));
        Assert.Equal("leash_stop_bubbles", LeashUiRules.StopKey(new("p", false, LeashTaskStop.ActivityStopped), PunishKind.Bubbles));
        Assert.Equal("leash_stop_video", LeashUiRules.StopKey(new("p", false, LeashTaskStop.ActivityStopped), PunishKind.Video));
        Assert.Equal("leash_stop_no_phrases", LeashUiRules.StopKey(new("p", false, LeashTaskStop.CouldNotContinue), PunishKind.Lines));
        Assert.Equal("leash_stop_unplayable_skipped", LeashUiRules.StopKey(new("p", false, LeashTaskStop.Unplayable), PunishKind.Video, LeashSkipResult.Skipped));
        Assert.Equal("leash_stop_unplayable_marked", LeashUiRules.StopKey(new("p", false, LeashTaskStop.Unplayable), PunishKind.Video, LeashSkipResult.Marked));
        Assert.Equal("leash_stop_video", LeashUiRules.StopKey(new("p", false, LeashTaskStop.Unplayable), PunishKind.Video));
        Assert.Equal("leash_assign_stop_closed", LeashUiRules.StopKey(new("a", true, LeashTaskStop.ActivityStopped), null));
        Assert.Equal("leash_assign_stop_unplayable", LeashUiRules.StopKey(new("a", true, LeashTaskStop.Unplayable), null));
    }

    [Fact]
    public void Every_new_leash_key_is_in_all_nine_languages()
    {
        var keys = new[]
        {
            "leash_stop_session", "leash_stop_bubbles", "leash_stop_video",
            "leash_stop_no_phrases", "leash_stop_unplayable_skipped", "leash_stop_unplayable_marked",
            "leash_assign_stop_closed", "leash_assign_stop_unplayable",
        };
        var files = System.IO.Directory.GetFiles(LangDir(), "*.json");
        Assert.Equal(9, files.Length);
        foreach (var file in files)
        {
            var o = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(file));
            foreach (var k in keys) Assert.False(string.IsNullOrWhiteSpace((string?)o[k]), $"{k} missing in {System.IO.Path.GetFileName(file)}");
        }
    }

    [Fact]
    public void No_lock_card_phrase_means_no_lock_card()
    {
        Assert.False(AppLeashTaskHost.HasEnabledPhrase(null));
        Assert.False(AppLeashTaskHost.HasEnabledPhrase(new Dictionary<string, bool> { ["a"] = false }));
        Assert.True(AppLeashTaskHost.HasEnabledPhrase(new Dictionary<string, bool> { ["a"] = false, ["b"] = true }));
    }

    private static string LangDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var probe = System.IO.Path.Combine(dir, "ConditioningControlPanel", "Localization", "Languages");
            if (System.IO.Directory.Exists(probe)) return probe;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("language folder not found");
    }
}
