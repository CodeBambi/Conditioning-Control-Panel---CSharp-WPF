using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The leash's pure rules: grammar, DND, the poll cadence, the gate, the assignment and
/// the day report, and what a leash may put on Circe's tab.</summary>
public class LeashRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly LeashPerson Vex = new("u_vex", "Vex", null);

    private static Punishment Pun(string pid, PunishKind kind, int size, DateTimeOffset at, LeashWatch? w = null) =>
        new(pid, kind, size, w, Vex, at, at.AddHours(72));

    // ---- grammar ----

    [Theory]
    [InlineData(PunishKind.Lines, LeashIntensity.Soft, true)]
    [InlineData(PunishKind.Pink, LeashIntensity.Soft, true)]
    [InlineData(PunishKind.Bubbles, LeashIntensity.Soft, false)]
    [InlineData(PunishKind.Bubbles, LeashIntensity.Standard, true)]
    [InlineData(PunishKind.Detention, LeashIntensity.Standard, true)]
    [InlineData(PunishKind.Video, LeashIntensity.Soft, false)]
    [InlineData(PunishKind.Chaster, LeashIntensity.Standard, false)]
    [InlineData(PunishKind.Chaster, LeashIntensity.Strict, true)]
    public void Intensity_decides_which_punishments_exist(PunishKind kind, LeashIntensity at, bool allowed)
        => Assert.Equal(allowed, LeashGrammar.Allowed(kind, at));

    [Fact]
    public void Only_preset_sizes_and_the_two_watch_kinds_pass()
    {
        Assert.True(LeashGrammar.ValidPunish(PunishKind.Lines, 5, null));
        Assert.False(LeashGrammar.ValidPunish(PunishKind.Lines, 4, null));
        Assert.False(LeashGrammar.ValidPunish(PunishKind.Video, 1, null));
        Assert.True(LeashGrammar.ValidPunish(PunishKind.Video, 1, new LeashWatch("ht", "12345", null)));
        Assert.False(LeashGrammar.ValidPunish(PunishKind.Video, 1, new LeashWatch("flavour", "pink", null)));
        Assert.False(LeashGrammar.ValidPunish(PunishKind.Video, 1, new LeashWatch("ht", "12a", null)));
        Assert.False(LeashGrammar.ValidPunish(PunishKind.Lines, 3, new LeashWatch("ht", "1", null)));
        Assert.True(LeashGrammar.ValidAssign(AssignKind.Quests, 3, null));
        Assert.False(LeashGrammar.ValidAssign(AssignKind.Minutes, 45, null));
        Assert.True(LeashGrammar.ValidReward(RewardKind.Sticker, "heart", null));
        Assert.False(LeashGrammar.ValidReward(RewardKind.Sticker, "free text", null));
        Assert.True(LeashGrammar.ValidReward(RewardKind.Credit, null, 1800));
        Assert.False(LeashGrammar.ValidReward(RewardKind.Credit, null, 3600));
        Assert.True(LeashGrammar.ValidReward(RewardKind.Praise, "proud", null));
    }

    // ---- DND ----

    [Fact]
    public void Dnd_today_ends_at_local_midnight_and_never_past_a_day()
    {
        var local = new DateTimeOffset(2026, 9, 26, 22, 30, 0, TimeSpan.FromHours(2));
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.FromHours(2)), LeashDndRule.Until(LeashDnd.Today, local));
        Assert.Equal(local.AddHours(1), LeashDndRule.Until(LeashDnd.OneHour, local));
        Assert.Equal(local.AddHours(4), LeashDndRule.Until(LeashDnd.FourHours, local));
        Assert.Null(LeashDndRule.Until(LeashDnd.Off, local));
        Assert.Equal("today", LeashDndRule.ToWire(LeashDnd.Today));
        Assert.Equal("off", LeashDndRule.ToWire(LeashDnd.Off));
        Assert.True(LeashDndRule.IsOn(T0.AddMinutes(1), T0));
        Assert.False(LeashDndRule.IsOn(T0, T0));
        Assert.False(LeashDndRule.IsOn(null, T0));
    }

    // ---- poll ----

    [Theory]
    [InlineData(0, true, 0)]
    [InlineData(120, true, 20)]
    [InlineData(120, false, 120)]
    [InlineData(20, true, 20)]
    public void Leash_only_ever_speeds_the_friends_poll_up(int friends, bool active, int expected)
        => Assert.Equal(expected, LeashPollRule.NextIntervalSeconds(friends, active));

    // ---- gate ----

    [Fact]
    public void Gate_never_lands_on_a_busy_player()
    {
        var idle = new LeashGateInputs(true, false, false, false, false, false, false, false);
        Assert.True(LeashGateRule.ShouldShow(idle));
        Assert.False(LeashGateRule.ShouldShow(idle with { Due = false }));
        Assert.False(LeashGateRule.ShouldShow(idle with { SessionRunning = true }));
        Assert.False(LeashGateRule.ShouldShow(idle with { GameUp = true }));
        Assert.False(LeashGateRule.ShouldShow(idle with { LockCardOpen = true }));
        Assert.False(LeashGateRule.ShouldShow(idle with { FullscreenEffect = true }));
        Assert.False(LeashGateRule.ShouldShow(idle with { ModalUp = true }));
        Assert.False(LeashGateRule.ShouldShow(idle with { PanelAway = true }));
        Assert.False(LeashGateRule.ShouldShow(idle with { Watching = true }));
    }

    [Fact]
    public void The_leash_gate_goes_before_homework()
    {
        Assert.Equal(GateChoice.Leash, LeashGateRule.Pick(true, true));
        Assert.Equal(GateChoice.Homework, LeashGateRule.Pick(false, true));
        Assert.Equal(GateChoice.None, LeashGateRule.Pick(false, false));
    }

    [Fact]
    public void Gate_shows_the_oldest_live_non_chaster_punishment()
    {
        var list = new List<Punishment>
        {
            Pun("c", PunishKind.Chaster, 900, T0.AddHours(-5)),
            Pun("old", PunishKind.Lines, 3, T0.AddHours(-80)),       // expired
            Pun("b", PunishKind.Pink, 10, T0.AddHours(-2)),
            Pun("a", PunishKind.Bubbles, 50, T0.AddHours(-3)),
        };
        Assert.Equal("a", LeashGateRule.Due(list, T0)!.Pid);
        Assert.Equal("b", LeashGateRule.Due(list, T0, new HashSet<string> { "a" })!.Pid);
        Assert.Null(LeashGateRule.Due(null, T0));
        Assert.Null(LeashGateRule.Due(new[] { list[0] }, T0));
    }

    // ---- assignment ----

    [Fact]
    public void Assignment_is_met_only_on_its_own_day()
    {
        var a = new Assignment("a1", AssignKind.Minutes, 30, null, "20260926", AssignStatus.Open, T0);
        Assert.True(LeashAssignRule.Met(a, "20260926", 30, 0, false));
        Assert.False(LeashAssignRule.Met(a, "20260926", 29, 0, false));
        Assert.False(LeashAssignRule.Met(a, "20260927", 60, 0, false));
        Assert.False(LeashAssignRule.Met(a with { Status = AssignStatus.Done }, "20260926", 60, 0, false));
        var q = a with { Kind = AssignKind.Quests, Size = 2 };
        Assert.True(LeashAssignRule.Met(q, "20260926", 0, 2, false));
        var v = a with { Kind = AssignKind.Video, Size = 1, Watch = new LeashWatch("ht", "1", null) };
        Assert.False(LeashAssignRule.Met(v, "20260926", 99, 9, false));
        Assert.True(LeashAssignRule.Met(v, "20260926", 0, 0, true));
        Assert.Equal((15, 30), LeashAssignRule.Progress(a, 15, 0, false));
        Assert.Equal("20260926", LeashAssignRule.DayKey(new DateTime(2026, 9, 26, 23, 59, 0)));
    }

    // ---- report ----

    [Fact]
    public void Report_carries_chaster_only_when_linked_and_keeps_a_hidden_timer_hidden()
    {
        var local = new DateTime(2026, 9, 26, 12, 0, 0);
        var w = new LeashDayInputs(local, 42, 2, 3, 7, false, T0.UtcDateTime.AddHours(2), false, 300);
        var r = LeashReportBuilder.Build(w, null, false, T0);
        Assert.Equal("20260926", r.Day);
        Assert.Equal(42, r.Minutes);
        Assert.Equal(2, r.QuestsDone);
        Assert.Equal(3, r.QuestsTotal);
        Assert.Equal(7, r.Streak);
        Assert.Null(r.LockLeftSeconds);
        Assert.Null(r.TabSeconds);
        Assert.False(r.AssignDone);

        var linked = LeashReportBuilder.Build(w with { ChasterLinked = true }, null, false, T0);
        Assert.Equal(7200, linked.LockLeftSeconds);
        Assert.Equal(300, linked.TabSeconds);
        var hidden = LeashReportBuilder.Build(w with { ChasterLinked = true, LockTimerHidden = true }, null, false, T0);
        Assert.Null(hidden.LockLeftSeconds);

        var wire = LeashReportBuilder.ToWire(linked);
        Assert.Equal("20260926", (string?)wire["day"]);
        Assert.Equal(7200, (int)wire["lock_left_s"]!);
        Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, LeashReportBuilder.ToWire(r)["lock_left_s"]!.Type);
    }

    [Fact]
    public void Report_says_assign_done_when_the_rule_says_so()
    {
        var local = new DateTime(2026, 9, 26, 12, 0, 0);
        var a = new Assignment("a1", AssignKind.Quests, 2, null, "20260926", AssignStatus.Open, T0);
        var w = new LeashDayInputs(local, 0, 2, 3, 0, false, null, false, null);
        Assert.True(LeashReportBuilder.Build(w, a, false, T0).AssignDone);
        Assert.False(LeashReportBuilder.Build(w with { QuestsDone = 1 }, a, false, T0).AssignDone);
        // Quests done never reads more than there are.
        Assert.Equal(3, LeashReportBuilder.Build(w with { QuestsDone = 9 }, null, false, T0).QuestsDone);
    }

    // ---- the tab ----

    [Fact]
    public void A_leash_books_chaster_time_only_at_strict_and_in_preset_sizes()
    {
        Assert.Equal(1800, LeashChasterRule.PunishSeconds(1800, LeashIntensity.Strict));
        Assert.Equal(0, LeashChasterRule.PunishSeconds(1800, LeashIntensity.Standard));
        Assert.Equal(0, LeashChasterRule.PunishSeconds(1800, null));
        Assert.Equal(0, LeashChasterRule.PunishSeconds(7200, LeashIntensity.Strict));
        Assert.Equal(900, LeashChasterRule.CreditSeconds(900));
        Assert.Equal(0, LeashChasterRule.CreditSeconds(3600));
        Assert.Equal(0, LeashChasterRule.CreditSeconds(null));
    }

    [Fact]
    public void Leash_rows_are_opt_in_and_the_cut_is_never_priced()
    {
        Assert.Contains("leash_cut", TabPrices.NeverPriced);
        Assert.Equal(TabPriceGate.Free, TabPrices.Find("leash")!.Gate);
        Assert.True(TabPrices.Find("leash")!.Seconds > 0);
        Assert.True(TabPrices.Find("leash_credit")!.Seconds < 0);
        Assert.Equal(0, TabPrices.Resolve("leash", new HashSet<string>()));
        Assert.Equal(0, TabPrices.Resolve("leash_cut", new HashSet<string> { "leash_cut" }));
    }

    [Fact]
    public void Watch_meter_counts_only_real_forward_playback()
    {
        var m = new LeashWatchMeter();
        m.Sample(0, 100, true);
        for (var t = 1; t <= 50; t++) m.Sample(t, 100, true);
        Assert.Equal(50, m.WatchedSeconds, 3);
        m.Sample(95, 100, true);                 // a seek
        Assert.Equal(50, m.WatchedSeconds, 3);
        for (var t = 96; t <= 100; t++) m.Sample(t, 100, false); // hidden
        Assert.Equal(50, m.WatchedSeconds, 3);
        Assert.False(m.IsComplete);
        var n = new LeashWatchMeter();
        for (var t = 0; t <= 90; t++) n.Sample(t, 100, true);
        Assert.True(n.IsComplete);
        Assert.Equal(100, n.Percent);
        var shortClip = new LeashWatchMeter();
        for (var t = 0; t <= 20; t++) shortClip.Sample(t, 20, true);
        Assert.False(shortClip.IsComplete);
    }
}
