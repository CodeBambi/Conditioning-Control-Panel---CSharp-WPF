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
/// The leash safety pass (2026-09-28): the ask card keeps itself open on a failed answer, the
/// gate can say a video will not play, the holder's let-go only sounds when it landed, the self
/// card offers "Watch it" on a video task, the runner's stop lines, the orphan guard, the hold
/// countdown and "the leash came off" naming who.
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

    // ---- the ask card ------------------------------------------------------------------

    [Theory]
    [InlineData(LeashAnswerResult.Failed)]
    [InlineData(LeashAnswerResult.Off)]
    [InlineData(LeashAnswerResult.Gone)]
    public void A_failed_answer_keeps_the_ask_card_open_and_says_why(LeashAnswerResult result)
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var offer = svc.Snapshot.Offers[0];
            var card = new LeashAskCard(offer, () => svc);
            var answered = new List<bool>();
            var dismissed = 0;
            card.Answered += (_, a, _) => answered.Add(a);
            card.Dismissed += () => dismissed++;

            svc.NextAnswer = result;
            card.AnswerAsync(true).GetAwaiter().GetResult();
            Assert.Empty(answered);
            Assert.Equal(0, dismissed);
            Assert.False(string.IsNullOrEmpty(card.ErrorShown));
            Assert.Null(svc.Snapshot.Me?.Holder.Id == offer.From.Id ? "leashed" : null);

            if (result == LeashAnswerResult.Gone)
            {
                // The offer is gone: "Put it on" is off and "Not now" is a plain close.
                Assert.False(card.PutItOnButton!.IsEnabled);
                card.AnswerAsync(false).GetAwaiter().GetResult();
                Assert.Equal(1, dismissed);
                Assert.Empty(answered);
            }
            else
            {
                // Try again: it goes through this time and the card closes as usual.
                card.AnswerAsync(true).GetAwaiter().GetResult();
                Assert.Equal(new[] { true }, answered);
            }
        });
    }

    [Fact]
    public void Ask_error_keys_exist_for_every_failure()
    {
        Assert.Null(LeashAskCard.ErrorKey(LeashAnswerResult.Done));
        foreach (var r in new[] { LeashAnswerResult.Gone, LeashAnswerResult.Off, LeashAnswerResult.Failed })
            Assert.StartsWith("leash_ask_err_", LeashAskCard.ErrorKey(r));
    }

    // ---- the gate ----------------------------------------------------------------------

    [Fact]
    public void The_gate_says_a_video_will_not_play_and_keeps_pardon_and_cut()
    {
        Run(() =>
        {
            var gate = new LeashGateCard();
            var later = 0;
            gate.LaterRequested += () => later++;
            gate.Present(Pun("p1", PunishKind.Video), pardons: 1, unplayable: true);
            Assert.True(gate.ShowsUnplayable);
            Assert.NotNull(Find(gate, t => t == "leash-gate-unplayable"));
            Assert.Null(Find(gate, t => t == "leash-gate-go"));
            Assert.NotNull(Find(gate, t => t == "leash-gate-pardon"));
            Assert.NotNull(Find(gate, t => t == "leash-gate-cut"));
            Assert.NotNull(Find(gate, t => t == "leash-gate-panic"));
            ((Button)Find(gate, t => t == "leash-gate-later")!).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(1, later);

            gate.Present(Pun("p2"), pardons: 0);
            Assert.False(gate.ShowsUnplayable);
            Assert.NotNull(Find(gate, t => t == "leash-gate-go"));
        });
    }

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

    // ---- the holder's let go -----------------------------------------------------------

    [Fact]
    public void Letting_go_reports_whether_it_landed()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var card = new LeashHolderCard(svc.Snapshot.Holding[0], () => svc);
            svc.ReleaseRefused = true;
            Assert.False(card.ReleaseAsync().GetAwaiter().GetResult());
            Assert.Single(svc.Snapshot.Holding);
            svc.ReleaseRefused = false;
            Assert.True(card.ReleaseAsync().GetAwaiter().GetResult());
            Assert.Empty(svc.Snapshot.Holding);
        });
    }

    // ---- the self card -----------------------------------------------------------------

    [Fact]
    public void Self_card_offers_watch_it_only_on_an_open_video_task()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var video = new Assignment("a9", AssignKind.Video, 1, new LeashWatch("ht", "42", "Pink"), "20260928", AssignStatus.Open, T0);
            var card = new LeashSelfCard(svc.Snapshot.Me! with { Assignment = video }, () => svc);
            Assert.NotNull(Find(card, t => t == "leash-self-watch"));
            // No runner able to watch (the fake): it says so and nothing throws.
            Assert.False(card.StartWatch(video));

            var minutes = new LeashSelfCard(svc.Snapshot.Me!, () => svc);
            Assert.Null(Find(minutes, t => t == "leash-self-watch"));
            var done = new LeashSelfCard(svc.Snapshot.Me! with { Assignment = video with { Status = AssignStatus.Done } }, () => svc);
            Assert.Null(Find(done, t => t == "leash-self-watch"));
        });
    }

    // ---- pure rules --------------------------------------------------------------------

    [Fact]
    public void A_task_that_left_the_queue_is_orphaned()
    {
        var me = FakeLeashService.SampleMine() with { Pending = new[] { Pun("p1") } };
        Assert.False(LeashGateRule.Orphaned(null, null, me));
        Assert.False(LeashGateRule.Orphaned("p1", null, me));
        Assert.True(LeashGateRule.Orphaned("p2", null, me));              // dropped, pardoned, expired
        Assert.True(LeashGateRule.Orphaned("p1", null, null));            // the leash is gone
        Assert.True(LeashGateRule.Orphaned(null, "a1", null));
        var a = new Assignment("a1", AssignKind.Video, 1, new LeashWatch("ht", "1", null), "d", AssignStatus.Open, T0);
        Assert.False(LeashGateRule.Orphaned(null, "a1", me with { Assignment = a }));
        Assert.True(LeashGateRule.Orphaned(null, "a1", me with { Assignment = a with { Aid = "a2" } }));
        Assert.True(LeashGateRule.Orphaned(null, "a1", me with { Assignment = a with { Status = AssignStatus.Missed } }));
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

    [Theory]
    [InlineData(0.0, 5)]
    [InlineData(0.6, 5)]
    [InlineData(1.0, 4)]
    [InlineData(3.2, 2)]
    [InlineData(4.9, 1)]
    [InlineData(7.0, 1)]
    public void Hold_ring_counts_five_to_one(double held, int shown)
        => Assert.Equal(shown, LeashHoldTick.SecondsLeft(TimeSpan.FromSeconds(held)));

    [Fact]
    public void Hold_ring_fraction_fills_over_the_hold()
    {
        Assert.Equal(0, LeashHoldTick.Fraction(TimeSpan.Zero));
        Assert.Equal(0.5, LeashHoldTick.Fraction(TimeSpan.FromSeconds(2.5)), 3);
        Assert.Equal(1, LeashHoldTick.Fraction(TimeSpan.FromSeconds(9)));
    }

    [Fact]
    public void The_leash_came_off_names_who()
    {
        var e = new LeashEvent("e1", LeashEventKind.Ended, FakeLeashService.Vex, T0);
        Assert.Equal("leash_evt_ended", LeashUiRules.EventKey(e));
        foreach (var file in System.IO.Directory.GetFiles(LangDir(), "*.json"))
        {
            var line = System.IO.File.ReadLines(file).Single(l => l.Contains("\"leash_evt_ended\""));
            Assert.Contains("{0}", line);
        }
    }

    [Fact]
    public void Every_new_leash_key_is_in_all_nine_languages()
    {
        var keys = new[]
        {
            "leash_punish_fullscreen_key", "leash_task_close_hint", "leash_hold_ring", "leash_ask_err_gone", "leash_ask_err_off",
            "leash_ask_err_failed", "leash_release_confirm_title", "leash_release_confirm_body", "leash_release_confirm_yes",
            "leash_release_done", "leash_release_failed", "leash_gate_unplayable", "leash_gate_later", "leash_stop_session",
            "leash_stop_bubbles", "leash_stop_video", "leash_stop_no_phrases", "leash_stop_unplayable_skipped",
            "leash_stop_unplayable_marked", "leash_assign_stop_closed", "leash_assign_stop_unplayable", "leash_self_watch",
            "leash_self_watching", "leash_self_watch_busy", "leash_self_watch_cannot",
        };
        var files = System.IO.Directory.GetFiles(LangDir(), "*.json");
        Assert.Equal(10, files.Length);
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
