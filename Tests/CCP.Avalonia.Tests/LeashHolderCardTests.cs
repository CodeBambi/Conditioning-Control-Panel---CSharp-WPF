using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 5 r10: the WPF 7.1.5 holder card on this head (figures, task, week, the four
/// buttons and their preset sheets, receipts, replay seam), the video cap slider and the hold ring.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class LeashHolderCardTests
{
    private sealed class Svc : ILeashService
    {
        public readonly List<string> Calls = new();
        public LeashSendResult Reply = new(LeashSendStatus.Sent);
        public bool Receipts;
        public IReadOnlyList<LeashSentItem> Sent = Array.Empty<LeashSentItem>();
        public bool Available => true;
        public LeashSnapshot Snapshot => throw new NotSupportedException();
        public event Action<LeashSnapshot>? SnapshotChanged { add { } remove { } }
        public event Action<LeashEvent>? EventArrived { add { } remove { } }
        public Punishment? GateDue => null;
        public Task<LeashSendResult> OfferAsync(string friendId) => Task.FromResult(Reply);
        public Task<bool> ReleaseAsync(string leashedId) { Calls.Add("release:" + leashedId); return Task.FromResult(true); }
        public Task<LeashSendResult> AssignAsync(string id, AssignKind kind, int size, LeashWatch? watch = null) { Calls.Add($"assign:{kind}:{size}:{watch?.Kind}:{watch?.Id}"); return Task.FromResult(Reply); }
        public Task<LeashSendResult> PunishAsync(string id, PunishKind kind, int size, LeashWatch? watch = null) { Calls.Add($"punish:{kind}:{size}:{watch?.Kind}:{watch?.Id}"); return Task.FromResult(Reply); }
        public Task<LeashSendResult> RewardAsync(string id, RewardKind kind, string? s = null, int? size = null) { Calls.Add($"reward:{kind}:{s}:{size}"); return Task.FromResult(Reply); }
        public Task<LeashSendResult> TugAsync(string id) { Calls.Add("tug"); return Task.FromResult(Reply); }
        public Task<LeashAnswerResult> AnswerAsync(string holderId, bool accept, LeashIntensity intensity) => throw new NotSupportedException();
        public Task CutAsync() => Task.CompletedTask;
        public Task SetIntensityAsync(LeashIntensity intensity) => Task.CompletedTask;
        public Task SetDndAsync(LeashDnd dnd) => Task.CompletedTask;
        public Task SetRemoteModeAsync(LeashRemoteMode mode) => Task.CompletedTask;
        public Task SetVideoMaxAsync(int minutes) => Task.CompletedTask;
        public Task CompleteAsync(string pid) => Task.CompletedTask;
        public Task<bool> PardonAsync(string pid) => Task.FromResult(true);
        public bool ReceiptsSupported => Receipts;
        public IReadOnlyList<LeashSentItem> SentTo(string leashedId) => Sent;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static HeldLeash Held(LeashIntensity level = LeashIntensity.Standard, bool chaster = false, DateTimeOffset? dnd = null,
        int punishedToday = 0, Assignment? task = null, int videoMax = 30)
    {
        var report = new DayReport("20261009", 12, 1, 3, 4, chaster, chaster ? 3600 : null, chaster ? 900 : null, false, DateTimeOffset.UtcNow);
        var week = Enumerable.Range(3, 7).Select(d => new WeekDay("202610" + d.ToString("00"), d == 9 ? WeekMark.Today : d % 2 == 0 ? WeekMark.Did : WeekMark.Idle)).ToList();
        return new HeldLeash(new LeashPerson("k9", "Kit", null), true, level, DateTimeOffset.UtcNow.AddDays(-2), 3, dnd, report, week,
            Array.Empty<Punishment>(), task, punishedToday) { VideoMax = videoMax };
    }

    private static IEnumerable<string> Tags(Control root) =>
        root.GetLogicalDescendants().OfType<Control>().Select(c => c.Tag as string).Where(t => t != null)!;

    private static Button ButtonTagged(Control root, string tag) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => (b.Tag as string) == tag);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public async Task Card_DrawsFiguresTaskWeekAndFourButtons_InWpfOrder()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var task = new Assignment("a1", AssignKind.Minutes, 30, null, "20261009", AssignStatus.Open, DateTimeOffset.UtcNow);
            var card = new LeashHolderCard(Held(task: task), () => new Svc());
            var top = card.Body.Children.Select(c => c.Tag as string).ToList();
            Assert.Equal(new[] { null, "leash-figures", "leash-holder-task", "leash-week", "leash-holder-buttons" }, top);
            var tags = Tags(card).ToList();
            Assert.Contains("leash-fig-minutes", tags);
            Assert.Contains("leash-fig-quests", tags);
            Assert.Contains("leash-fig-streak", tags);
            Assert.DoesNotContain("leash-fig-lock", tags);
            Assert.Equal(7, tags.Count(t => t.StartsWith("leash-week:")));
            Assert.Contains("leash-week:Today", tags);
            foreach (var b in new[] { "assign", "reward", "punish", "tug" }) Assert.Contains("leash-btn:" + b, tags);
            Assert.Contains("leash-ring-arc", tags);   // 12 of 30 minutes draws an arc
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Chaster_ShowsLockFigure_AndQuiet_DisablesAssignPunishTugButNotReward()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var card = new LeashHolderCard(Held(chaster: true, dnd: DateTimeOffset.UtcNow.AddHours(1)), () => new Svc());
            var tags = Tags(card).ToList();
            Assert.Contains("leash-fig-lock", tags);
            Assert.Contains("leash-holder-dnd", tags);
            Assert.False(ButtonTagged(card, "leash-btn:assign").IsEnabled);
            Assert.False(ButtonTagged(card, "leash-btn:punish").IsEnabled);
            Assert.False(ButtonTagged(card, "leash-btn:tug").IsEnabled);
            Assert.True(ButtonTagged(card, "leash-btn:reward").IsEnabled);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PunishSheet_HidesKindsAboveTheirLevel_AndChasterNeedsStrictAndALink()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var soft = new LeashHolderCard(Held(LeashIntensity.Soft), () => new Svc());
            soft.ToggleSheet("punish");
            Assert.Equal("punish", soft.OpenSheet);
            var kinds = Tags(soft).Where(t => t.StartsWith("leash-punish:")).Select(t => t.Split(':')[1]).Distinct().ToList();
            Assert.Equal(new[] { "lines", "pink" }, kinds);

            var strict = new LeashHolderCard(Held(LeashIntensity.Strict, chaster: true), () => new Svc());
            strict.ToggleSheet("punish");
            Assert.Contains("leash-punish:chaster:900", Tags(strict));
            Assert.Contains("leash-punish:video:1", Tags(strict).Select(t => t)); // the video row's one "pick" chip

            var capped = new LeashHolderCard(Held(punishedToday: 3), () => new Svc());
            capped.ToggleSheet("punish");
            Assert.Contains("leash-sheet-cap", Tags(capped));
            Assert.DoesNotContain(Tags(capped), t => t.StartsWith("leash-punish:"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PunishChip_SendsThePreset_ClosesTheSheet_AndWordsTheResult()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var svc = new Svc();
            var card = new LeashHolderCard(Held(), () => svc);
            card.ToggleSheet("punish");
            Assert.Equal(LeashSendStatus.Sent, await card.SendPunishAsync(PunishKind.Lines, 5, null));
            Assert.Equal("punish:Lines:5::", svc.Calls.Single());
            Assert.Null(card.OpenSheet);
            Assert.Contains("leash-result", Tags(card));
            Assert.False(string.IsNullOrEmpty(card.ResultText));

            svc.Reply = new LeashSendResult(LeashSendStatus.NotAllowed);
            card.ToggleSheet("punish");
            Assert.Equal(LeashSendStatus.NotAllowed, await card.SendPunishAsync(PunishKind.Detention, 10, null));
            Assert.Equal("punish", card.OpenSheet);   // a refusal keeps the sheet open
        });
    }

    [Fact]
    public async Task VideoPunish_OpensCapSliderStoppedAtTheirMax_AndPicker()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var svc = new Svc();
            var card = new LeashHolderCard(Held(videoMax: 20), () => svc);
            card.ToggleSheet("punish");
            await card.OpenVideo("punish");
            var cap = card.GetLogicalDescendants().OfType<LeashCapSlider>().Single();
            Assert.Equal(20, cap.Ceiling);
            Assert.True(cap.Value <= 20);
            Assert.Contains("leash-video-picker", Tags(card));
            var box = card.GetLogicalDescendants().OfType<TextBox>().Single(t => (t.Tag as string) == "leash-ht-box");
            var send = ButtonTagged(card, "leash-ht-send");
            Assert.False(send.IsEnabled);
            box.Text = "ab12x3";
            Assert.Equal("123", box.Text);   // digits only cross the wire
            Assert.True(send.IsEnabled);
        });
    }

    [Fact]
    public async Task RewardAndAssignSheets_OfferThePresets()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var svc = new Svc();
            var card = new LeashHolderCard(Held(chaster: true), () => svc);
            card.ToggleSheet("reward");
            var tags = Tags(card).ToList();
            foreach (var s in LeashUiRules.Stickers) Assert.Contains("leash-reward:sticker:" + s, tags);
            Assert.Contains("leash-reward:praise:proud", tags);
            Assert.Contains("leash-reward:pardon", tags);
            Assert.Contains("leash-reward:credit:900", tags);
            Click(ButtonTagged(card, "leash-reward:sticker:star"));
            await Task.Delay(20);
            Assert.Equal("reward:Sticker:star:", svc.Calls.Last());

            card.ToggleSheet("assign");
            tags = Tags(card).ToList();
            Assert.Contains("leash-assign:minutes:15", tags);
            Assert.Contains("leash-assign:quests:3", tags);
            Assert.Contains("leash-assign:video:1", tags);
        });
    }

    [Fact]
    public async Task Tug_IsThrottledOnTheClient_TenSecondsPerFriend()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var old = LeashHolderCard.TugThrottle;
            LeashHolderCard.TugThrottle = new LeashTugThrottle();
            try
            {
                var svc = new Svc();
                var card = new LeashHolderCard(Held(), () => svc);
                Assert.Equal(LeashSendStatus.Sent, await card.TugAsync());
                Assert.Equal(LeashSendStatus.TooFast, await card.TugAsync());
                Assert.Equal(new[] { "tug" }, svc.Calls);
            }
            finally { LeashHolderCard.TugThrottle = old; }
        });
    }

    [Fact]
    public async Task Receipts_DrawTheSentRowsWithSteps_OnlyWhenTheServerSpeaksThem()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var now = DateTimeOffset.UtcNow;
            var svc = new Svc
            {
                Sent = new[]
                {
                    new LeashSentItem("k1", LeashItemKind.Punish, "k9", "p1", LeashStep.Seen, now.AddMinutes(-5), now.AddMinutes(-1)) { Punish = PunishKind.Lines },
                    new LeashSentItem("k2", LeashItemKind.Tug, "k9", "t1", LeashStep.Arrived, now.AddMinutes(-3), now.AddMinutes(-2)),
                },
            };
            var off = new LeashHolderCard(Held(), () => svc);
            Assert.DoesNotContain("leash-sent", Tags(off));
            svc.Receipts = true;
            var on = new LeashHolderCard(Held(), () => svc);
            var tags = Tags(on).ToList();
            Assert.Contains("leash-sent", tags);
            Assert.Contains("leash-sent-row:punish:seen", tags);
            Assert.Contains("leash-sent-row:tug:arrived", tags);
            Assert.Contains(tags, t => t.StartsWith("leash-step-dot:0:on"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Menu_ReplayFirstThenLetGo_ReplayGoesThroughTheSeam_AndPreviewHasNoTools()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var card = new LeashHolderCard(Held(), () => new Svc());
            var items = card.Menu().Items.OfType<MenuItem>().Select(m => m.Tag as string).ToList();
            Assert.Equal(new[] { "leash-menu-replay", "leash-menu-release" }, items);
            HeldLeash? got = null;
            var old = LeashHolderCard.ReplaySnap;
            LeashHolderCard.ReplaySnap = h => got = h;
            try { card.RequestReplay(); } finally { LeashHolderCard.ReplaySnap = old; }
            Assert.Equal("k9", got?.Who.Id);

            var preview = new LeashHolderCard(Held(), () => new Svc(), readOnly: true);
            var tags = Tags(preview).ToList();
            Assert.Contains("leash-preview-buttons", tags);
            Assert.DoesNotContain("leash-holder-buttons", tags);
            Assert.DoesNotContain("leash-holder-more", tags);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CapSlider_BandsKeysCeilingAndClicks_AsWpf()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            Assert.Equal(LeashCapSlider.Blue, LeashCapSlider.ColorOf(30));
            Assert.Equal(LeashCapSlider.Yellow, LeashCapSlider.ColorOf(31));
            Assert.Equal(LeashCapSlider.Red, LeashCapSlider.ColorOf(61));
            Assert.Equal(1, LeashCapSlider.StepFor(Key.Right));
            Assert.Equal(-5, LeashCapSlider.StepFor(Key.PageDown));
            Assert.Equal(LeashVideoCap.Min, LeashCapSlider.ValueAt(0, 300));
            Assert.Equal(LeashVideoCap.Max, LeashCapSlider.ValueAt(300, 300));

            var s = new LeashCapSlider("cap", 45, "t");
            int committed = 0;
            s.Committed += v => committed = v;
            s.Ceiling = 40;
            Assert.Equal(40, s.Value);
            s.Nudge(5);
            Assert.Equal(40, s.Value);   // never past the ceiling
            s.Nudge(-5);
            Assert.Equal(35, committed);
            Assert.Contains("35", s.ReadoutText);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task HoldRing_CountsDownOncePerSecond_AndDismisses()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var old = LeashHoldRing.ShowWindows;
            LeashHoldRing.ShowWindows = false;
            try
            {
                LeashHoldRing.ShowHeld(TimeSpan.FromMilliseconds(200));
                Assert.True(LeashHoldRing.IsShown);
                Assert.Equal(LeashHoldTick.SecondsLeft(TimeSpan.FromMilliseconds(200)), LeashHoldRing.Number);
                LeashHoldRing.ShowHeld(TimeSpan.FromMilliseconds(2200));
                Assert.Equal(LeashHoldTick.SecondsLeft(TimeSpan.FromMilliseconds(2200)), LeashHoldRing.Number);
                LeashHoldRing.Dismiss();
                Assert.False(LeashHoldRing.IsShown);
            }
            finally { LeashHoldRing.ShowWindows = old; LeashHoldRing.Dismiss(); }
            return Task.CompletedTask;
        });
    }
}
