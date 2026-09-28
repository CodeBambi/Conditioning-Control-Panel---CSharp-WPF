using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The leash surfaces against <see cref="FakeLeashService"/>: which punishments a holder may see
/// (hidden, never greyed), the Offer chip's states, that Panic and Cut are always on the gate,
/// that the ask card lights what each level allows, and that every leash_* key the surfaces use
/// is in all nine language files. Drawn offscreen on the shared STA thread; with
/// <c>CCP_LEASH_SHOTS</c> set to a folder, each surface is also saved there as a PNG.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LeashUiTests
{
    // ---- pure rules --------------------------------------------------------------------

    [Fact]
    public void Soft_shows_only_lines_and_pink()
        => Assert.Equal(new[] { PunishKind.Lines, PunishKind.Pink }, LeashUiRules.VisiblePunishments(LeashIntensity.Soft, true));

    [Fact]
    public void Standard_hides_chaster_time()
    {
        var v = LeashUiRules.VisiblePunishments(LeashIntensity.Standard, true);
        Assert.DoesNotContain(PunishKind.Chaster, v);
        Assert.Contains(PunishKind.Video, v);
        Assert.Equal(5, v.Count);
    }

    [Fact]
    public void Strict_shows_chaster_only_with_a_chaster_link()
    {
        Assert.Contains(PunishKind.Chaster, LeashUiRules.VisiblePunishments(LeashIntensity.Strict, true));
        Assert.DoesNotContain(PunishKind.Chaster, LeashUiRules.VisiblePunishments(LeashIntensity.Strict, false));
    }

    [Fact]
    public void Offer_chip_states()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(LeashUiRules.OfferChip.Hidden, LeashUiRules.OfferState(false, false, false, true, now, now, 0));
        Assert.Equal(LeashUiRules.OfferChip.Holding, LeashUiRules.OfferState(true, true, false, true, now, now, 1));
        Assert.Equal(LeashUiRules.OfferChip.Sent, LeashUiRules.OfferState(true, false, true, true, now, now, 0));
        Assert.Equal(LeashUiRules.OfferChip.Offer, LeashUiRules.OfferState(true, false, false, false, now.AddDays(-6), now, 0));
        Assert.Equal(LeashUiRules.OfferChip.Greyed, LeashUiRules.OfferState(true, false, false, false, now.AddDays(-8), now, 0));
        Assert.Equal(LeashUiRules.OfferChip.Greyed, LeashUiRules.OfferState(true, false, false, true, now, now, 5));
    }

    [Theory]
    [InlineData(900, "15:00")]
    [InlineData(3600, "1:00:00")]
    [InlineData(75, "1:15")]
    public void Clock_format(int s, string expected) => Assert.Equal(expected, LeashUiRules.Clock(s));

    [Fact]
    public void Lock_and_tab_figures()
    {
        Assert.Equal("3d 4h", LeashUiRules.LockLeft(3 * 86400 + 4 * 3600 + 59));
        Assert.Equal("5h 12m", LeashUiRules.LockLeft(5 * 3600 + 12 * 60));
        Assert.Equal("+22:30", LeashUiRules.Tab(1350));
        Assert.Equal("-5:00", LeashUiRules.Tab(-300));
    }

    [Fact]
    public void Gate_waits_for_an_idle_moment()
    {
        var idle = new LeashGateWorld(true, false, false, false, false, false, false, false);
        Assert.True(LeashUiRules.ShouldShow(idle));
        Assert.False(LeashUiRules.ShouldShow(idle with { SessionRunning = true }));
        Assert.False(LeashUiRules.ShouldShow(idle with { LockCardOpen = true }));
        Assert.False(LeashUiRules.ShouldShow(idle with { Snoozed = true }));
        Assert.False(LeashUiRules.ShouldShow(idle with { Due = false }));
    }

    [Fact]
    public void Thud_ease_overshoots_and_lands()
    {
        var e = new BezierEase();
        Assert.Equal(0, e.Ease(0), 3);
        Assert.Equal(1, e.Ease(1), 3);
        Assert.True(Enumerable.Range(1, 19).Select(i => e.Ease(i / 20.0)).Max() > 1.0);
    }

    // ---- surfaces ----------------------------------------------------------------------

    private static FrameworkElement? Find(DependencyObject root, Func<string, bool> tag)
    {
        if (root is FrameworkElement fe && fe.Tag is string s && tag(s)) return fe;
        foreach (var c in LogicalTreeHelper.GetChildren(root))
            if (c is DependencyObject d && Find(d, tag) is { } hit) return hit;
        return null;
    }

    private static List<string> Tags(DependencyObject root, string prefix)
    {
        var list = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is FrameworkElement fe && fe.Tag is string s && s.StartsWith(prefix, StringComparison.Ordinal)) list.Add(s);
            foreach (var c in LogicalTreeHelper.GetChildren(d)) if (c is DependencyObject x) Walk(x);
        }
        Walk(root);
        return list;
    }

    private static void Run(Action body) => WpfRenderHarness.OnStaThread(() =>
    {
        LeashFx.ForceStill = true;
        try { body(); } finally { LeashFx.ForceStill = false; }
    });

    [Fact]
    public void Holder_card_hides_what_the_level_does_not_allow()
    {
        Run(() =>
        {
            var svc = new FakeLeashService(new LeashSnapshot(null, new[] { FakeLeashService.SampleHeld(LeashIntensity.Soft) }, Array.Empty<LeashOffer>()));
            var card = new LeashHolderCard(svc.Snapshot.Holding[0], () => svc);
            card.ToggleSheet("punish");
            var rows = Tags(card, "leash-punish:").Select(t => t.Split(':')[1]).Distinct().ToList();
            Assert.Equal(new[] { "lines", "pink" }, rows);
            Shot(card, "holder-punish-soft", 300);
        });
    }

    [Fact]
    public void Holder_card_punish_goes_through_and_is_worded()
    {
        Run(() =>
        {
            var svc = new FakeLeashService(new LeashSnapshot(null, new[] { FakeLeashService.SampleHeld(LeashIntensity.Strict) }, Array.Empty<LeashOffer>()));
            var card = new LeashHolderCard(svc.Snapshot.Holding[0], () => svc);
            card.ToggleSheet("punish");
            Assert.NotNull(Find(card, t => t == "leash-punish:chaster:900"));
            var status = card.SendPunishAsync(PunishKind.Lines, 5, null).GetAwaiter().GetResult();
            Assert.Equal(LeashSendStatus.Sent, status);
            Assert.Contains("punish:u_mika:Lines:5", svc.Calls);
            Assert.NotNull(card.ResultText);
            Assert.Null(card.OpenSheet);
        });
    }

    [Fact]
    public void Holder_card_renders_figures_week_and_four_buttons()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var card = new LeashHolderCard(svc.Snapshot.Holding[0], () => svc);
            Assert.NotNull(Find(card, t => t == "leash-fig-minutes"));
            Assert.NotNull(Find(card, t => t == "leash-fig-lock"));
            Assert.Equal(7, Tags(card, "leash-week:").Count);
            foreach (var b in new[] { "assign", "reward", "punish", "tug" })
                Assert.NotNull(Find(card, t => t.StartsWith("leash-btn:" + b, StringComparison.Ordinal)));
            Assert.NotNull(Find(card, t => t == "leash-help:holder"));
            Shot(card, "holder-card", 300);
            card.ToggleSheet("reward");
            Shot(card, "holder-reward", 300);
            card.ToggleSheet("assign");
            Shot(card, "holder-assign", 300);
        });
    }

    [Fact]
    public void Self_card_cuts_in_one_click_and_sets_its_own_level()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var card = new LeashSelfCard(svc.Snapshot.Me!, () => svc);
            Assert.NotNull(Find(card, t => t == "leash-cut"));
            Assert.NotNull(Find(card, t => t == "leash-help:leashed"));
            Shot(card, "self-card", 300);
            card.SetLevelAsync(LeashIntensity.Soft).GetAwaiter().GetResult();
            Assert.Contains("intensity:Soft", svc.Calls);
            Assert.NotNull(Find(card, t => t == "leash-level:0:on"));
            bool pressed = false;
            card.CutPressed += () => pressed = true;
            card.CutAsync().GetAwaiter().GetResult();
            Assert.True(pressed);
            Assert.Contains("cut", svc.Calls);
            Assert.Null(svc.Snapshot.Me);
        });
    }

    [Fact]
    public void Self_card_preview_draws_what_the_holder_sees()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var prior = LeashLocator.LocalReport;
            LeashLocator.LocalReport = () => FakeLeashService.SampleReport();
            try
            {
                var card = new LeashSelfCard(svc.Snapshot.Me!, () => svc);
                var toggle = (Button)Find(card, t => t == "leash-preview-toggle")!;
                toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.True(card.PreviewOpen);
                Assert.NotNull(Find(card, t => t == "leash-preview"));
                Shot(card, "self-preview", 300);
            }
            finally { LeashLocator.LocalReport = prior; }
        });
    }

    [Fact]
    public void Gate_always_carries_panic_and_cut()
    {
        Run(() =>
        {
            var gate = new LeashGateCard();
            gate.Present(FakeLeashService.SamplePunishment(), pardons: 0);
            Assert.True(gate.IsUp);
            Assert.NotNull(Find(gate, t => t == "leash-gate-panic"));
            Assert.NotNull(Find(gate, t => t == "leash-gate-cut"));
            Assert.NotNull(Find(gate, t => t == "leash-help:gate"));
            Assert.Null(Find(gate, t => t == "leash-gate-pardon"));
            Assert.Equal(5, Tags(gate, "leash-pip").Count);

            gate.SetProgress(2, 5, running: true);
            Assert.Equal(2, Tags(gate, "leash-pip-done").Count);
            var go = (Button)Find(gate, t => t == "leash-gate-go")!;
            Assert.False(go.IsEnabled);
            // A task in progress never takes the ways out with it.
            Assert.True(((Button)Find(gate, t => t == "leash-gate-panic")!).IsEnabled);
            Assert.True(((Button)Find(gate, t => t == "leash-gate-cut")!).IsEnabled);

            gate.Present(FakeLeashService.SamplePunishment(), pardons: 1);
            Assert.NotNull(Find(gate, t => t == "leash-gate-pardon"));
            Shot(gate, "gate-lines", 560, 640);

            var g2 = new LeashGateCard();
            g2.Present(FakeLeashService.SamplePunishment(PunishKind.Detention, 20) with { Pid = "p2" }, 0);
            g2.SetProgress(1, 3, true);
            Assert.NotNull(Find(g2, t => t == "leash-gate-bar"));
            Shot(g2, "gate-detention", 560, 600);
        });
    }

    [Fact]
    public void Ask_card_lights_what_each_level_allows_and_answers()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var offer = svc.Snapshot.Offers[0];
            var card = new LeashAskCard(offer, () => svc);
            Assert.NotNull(card.ExplainerSlot.Child);
            Assert.NotNull(Find(card, t => t == "leash-help:ask"));
            Assert.Equal(5, Tags(card, "leash-allow:").Count(t => t.EndsWith(":on")));
            Shot(card, "ask-card", 420);
            card.SetLevel(LeashIntensity.Soft);
            Assert.Equal(2, Tags(card, "leash-allow:").Count(t => t.EndsWith(":on")));
            card.SetLevel(LeashIntensity.Strict);
            Assert.Equal(6, Tags(card, "leash-allow:").Count(t => t.EndsWith(":on")));

            bool? accepted = null;
            card.Answered += (_, a, _) => accepted = a;
            card.AnswerAsync(true).GetAwaiter().GetResult();
            Assert.True(accepted);
            Assert.Contains($"answer:{offer.From.Id}:True:Strict", svc.Calls);
            Assert.Equal(LeashIntensity.Strict, svc.Snapshot.Me!.Intensity);
        });
    }

    [Fact]
    public void Snap_card_draws_both_sides()
    {
        Run(() =>
        {
            var card = new LeashSnapCard(FakeLeashService.Vex, FakeLeashService.Mika);
            Assert.NotNull(Find(card, t => t == "leash-snap-title"));
            Assert.NotNull(Find(card, t => t == "leash-chain"));
            Shot(card, "snap", 420);
        });
    }

    [Fact]
    public void Drawer_section_pins_offer_self_and_holder_cards()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var section = new LeashDrawerSection(() => svc);
            Assert.Equal(Visibility.Visible, section.Visibility);
            Assert.NotNull(Find(section, t => t == "leash-offer-row:u_juno"));
            Assert.NotNull(section.SelfCard);
            Assert.Single(section.HolderCards);

            var friend = new Friend("u_kit", "Kit", null, 0, true, new FriendPresence(PresenceActivity.Panel, null, DateTimeOffset.UtcNow), false);
            var chip = section.OfferChipFor(friend)!;
            Assert.Equal("leash-offer-chip:offer", chip.Tag);
            Assert.NotNull(Find(chip, t => t == "leash-help:offer"));
            var held = new Friend("u_mika", "Mika", null, 0, true, new FriendPresence(PresenceActivity.Panel, null, DateTimeOffset.UtcNow), false);
            Assert.Equal("leash-offer-chip:holding", section.OfferChipFor(held)!.Tag);

            Assert.Equal(LeashSendStatus.Sent, section.OfferAsync("u_kit", "Kit").GetAwaiter().GetResult());
            Assert.Equal("leash-offer-chip:sent", section.OfferChipFor(friend)!.Tag);
            LeashSurfaces.SentOffers.Remove("u_kit");

            var host = new StackPanel { Width = 290 };
            host.Children.Add(chip);
            Shot(host, "offer-chip", 290);

            svc.CutAsync().GetAwaiter().GetResult();
            Assert.Null(section.SelfCard);
            svc.Snapshot = LeashSnapshot.Empty;
            Assert.Equal(Visibility.Collapsed, section.Visibility);
        });
    }

    [Fact]
    public void Offer_chip_is_absent_while_the_leash_is_off()
    {
        Run(() =>
        {
            var svc = new FakeLeashService { Available = false };
            var section = new LeashDrawerSection(() => svc);
            var friend = new Friend("u_kit", "Kit", null, 0, true, new FriendPresence(PresenceActivity.Panel, null, DateTimeOffset.UtcNow), false);
            Assert.Null(section.OfferChipFor(friend));
            Assert.Equal(Visibility.Collapsed, section.Visibility);
        });
    }

    [Fact]
    public void Explainer_button_calls_the_host()
    {
        Run(() =>
        {
            LeashExplainRole? got = null;
            void H(LeashExplainRole r) => got = r;
            LeashExplainHost.Requested += H;
            try
            {
                var b = LeashLook.Help(LeashExplainRole.Gate);
                b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(LeashExplainRole.Gate, got);
            }
            finally { LeashExplainHost.Requested -= H; }
        });
    }

    // ---- loc ---------------------------------------------------------------------------

    [Fact]
    public void Every_leash_key_is_in_all_nine_languages()
    {
        var root = RepoRoot();
        var keys = new HashSet<string>();
        var sources = Directory.GetFiles(Path.Combine(root, "ConditioningControlPanel", "Controls", "Leash"), "*.cs").ToList();
        sources.Add(Path.Combine(root, "ConditioningControlPanel", "MainWindow", "MainWindow.Leash.cs"));
        foreach (var f in sources)
            foreach (Match m in Regex.Matches(File.ReadAllText(f), "\"(leash_[a-z0-9_]*[a-z0-9])\""))
                keys.Add(m.Groups[1].Value);
        foreach (var k in LeashUiRules.PunishOrder) { keys.Add(LeashUiRules.Key(k)); keys.Add(LeashUiRules.GateTitle(FakeLeashService.SamplePunishment(k)).Key); }
        foreach (var k in LeashUiRules.AssignOrder) keys.Add(LeashUiRules.Key(k));
        foreach (var k in Enum.GetValues<RewardKind>()) keys.Add(LeashUiRules.Key(k));
        foreach (var k in Enum.GetValues<LeashIntensity>()) keys.Add(LeashUiRules.Key(k));
        foreach (var s in Enum.GetValues<LeashSendStatus>()) keys.Add(LeashUiRules.ResultKey(s));
        foreach (var e in Enum.GetValues<LeashEventKind>())
            foreach (var acc in new bool?[] { true, false })
                foreach (var rk in Enum.GetValues<RewardKind>())
                    if (LeashUiRules.EventKey(new LeashEvent("e", e, FakeLeashService.Vex, DateTimeOffset.UtcNow, Reward: rk, Accepted: acc)) is { } ek) keys.Add(ek);
        foreach (var s in LeashUiRules.Stickers) keys.Add("leash_rew_sticker_" + s);
        foreach (var p in LeashUiRules.Praise) keys.Add("leash_praise_" + p);
        Assert.True(keys.Count > 120, "found only " + keys.Count);

        var dir = Path.Combine(root, "ConditioningControlPanel", "Localization", "Languages");
        foreach (var lang in new[] { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var raw = File.ReadAllBytes(Path.Combine(dir, lang + ".json"));
            Assert.False(raw.Length > 2 && raw[0] == 0xEF && raw[1] == 0xBB, lang + ".json has a BOM");
            var json = Newtonsoft.Json.Linq.JObject.Parse(System.Text.Encoding.UTF8.GetString(raw));
            foreach (var k in keys) Assert.True(json[k] != null, $"{lang}.json is missing {k}");
            foreach (var p in json.Properties().Where(p => p.Name.StartsWith("leash_", StringComparison.Ordinal)))
            {
                var v = (string?)p.Value ?? "";
                Assert.DoesNotContain("—", v);
                Assert.DoesNotContain("–", v);
                Assert.DoesNotContain("!", v);
            }
        }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "ConditioningControlPanel", "Localization"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    // ---- shots -------------------------------------------------------------------------

    /// <summary>Saves <paramref name="el"/> as a PNG in CCP_LEASH_SHOTS (if set), on the drawer's
    /// ground so the glass reads as it does in the app.</summary>
    private static void Shot(FrameworkElement el, string name, double width, double? height = null)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_LEASH_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var frame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x0A, 0x1E)),
            Padding = new Thickness(14),
            Width = width + 28,
        };
        if (height is double h) frame.Height = h;
        if (el.Parent is Panel p) p.Children.Remove(el);
        else if (el.Parent is Decorator dec) dec.Child = null;
        el.Width = el is LeashGateCard ? double.NaN : width;
        frame.Child = el;
        frame.Measure(new Size(frame.Width, height ?? double.PositiveInfinity));
        frame.Arrange(new Rect(frame.DesiredSize));
        frame.UpdateLayout();
        var w = (int)Math.Ceiling(frame.ActualWidth * 2);
        var hh = (int)Math.Ceiling(frame.ActualHeight * 2);
        var bmp = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, hh), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(frame);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, "leash-" + name + ".png"));
        enc.Save(fs);
        frame.Child = null;
    }
}
