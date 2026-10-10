using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Billboard;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Tonight Board's card host and art, realised offscreen: it builds, shows the deck's first
/// card with one chip per card, moves on, snoozes nothing it should not, and every built-in art
/// view paints, plays, pauses and releases without throwing. Nothing here judges the look.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BillboardCardHostRenderTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 20, 0, 0, DateTimeKind.Utc);

    private sealed class ListProvider : IBillboardProvider
    {
        public readonly List<BillboardCardSpec> Cards = new();
        public string Id => "list";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Cards;
        public void Invoke(string actionTarget) { }
        public event EventHandler? Changed { add { } remove { } }
    }

    private static Grid Realize(FrameworkElement element, double w = 1000, double h = 420)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        return host;
    }

    [Fact]
    public void The_host_shows_the_deck_with_a_chip_per_card_and_walks_it() => WpfRenderHarness.OnStaThread(() =>
    {
        BuiltInArt.Register();
        var list = new ListProvider();
        list.Cards.Add(new BillboardCardSpec("live", BillboardCardKind.Live, 0, "live now", "3 open tables", "join one", "#5fe3ff",
            BuiltInArtKeys.Tables, new Dictionary<string, int> { ["count"] = 3 },
            new BillboardAction(BillboardActionKind.Tab, "availablesubjects", "Join")));
        list.Cards.Add(new BillboardCardSpec("wait", BillboardCardKind.Waiting, 0, "waiting", "Your free spin", "", "#ffc94a",
            BuiltInArtKeys.Wheel, new Dictionary<string, int> { ["done"] = 1, ["total"] = 3 }, BillboardAction.None));
        list.Cards.Add(new BillboardCardSpec("board:4", BillboardCardKind.Board, 0, "", "", "", "#ff4fa8",
            "not-registered", null, BillboardAction.None, BillboardBadge.New, Snoozable: false));
        var house = new HouseProvider(k => k);
        var deck = new BillboardDeck(() => new IBillboardProvider[] { list, house },
            () => new BillboardContext(BillboardTier.Free, Now, Now));

        var cardHost = new BillboardCardHost(deck);
        Realize(cardHost);
        cardHost.Begin();
        cardHost.UpdateLayout();

        // The fresh board post comes first, wearing NEW, and the deck is all here.
        Assert.Equal("board:4", cardHost.CurrentCard!.Spec.Id);
        Assert.True(cardHost.CurrentCard.ShowNew);
        Assert.Equal(deck.Cards.Count, cardHost.ChipButtons.Count);
        Assert.Equal(5, deck.Cards.Count); // board, live, waiting, two house

        cardHost.Advance();
        cardHost.UpdateLayout();
        Assert.Equal("live", cardHost.CurrentCard!.Spec.Id);
        cardHost.Advance();
        Assert.Equal("wait", cardHost.CurrentCard!.Spec.Id);
        Assert.Equal(deck.Cards.Count, cardHost.ChipButtons.Count);
    });

    [Fact]
    public void The_home_page_has_a_slot_for_the_host_inside_the_well() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        Assert.NotNull(page.BillboardHostSlot);
        Assert.Same(page.DashBillboard, VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(page.BillboardHostSlot)));
    });

    public static IEnumerable<object?[]> ArtCases() => new[]
    {
        new object?[] { BuiltInArtKeys.Poster, "billboard/loom.png" },
        new object?[] { BuiltInArtKeys.Tables, new Dictionary<string, int> { ["count"] = 2 } },
        new object?[] { BuiltInArtKeys.Wheel, new Dictionary<string, int> { ["done"] = 2, ["total"] = 5 } },
        new object?[] { BuiltInArtKeys.Spiral, null },
        new object?[] { BuiltInArtKeys.Calendar, new Dictionary<string, int> { ["counted"] = 6, ["need"] = 25, ["days"] = 31, ["today"] = 7 } },
        new object?[] { BuiltInArtKeys.Calendar, new Dictionary<string, int> { ["day"] = 3, ["days"] = 14 } },
        new object?[] { BuiltInArtKeys.Tip, "tip.rightclick" },
        new object?[] { BuiltInArtKeys.Invite, null },
    };

    [Theory]
    [MemberData(nameof(ArtCases))]
    public void Every_built_in_view_paints_and_lives_its_whole_life(string key, object? data) => WpfRenderHarness.OnStaThread(() =>
    {
        BuiltInArt.Register();
        var view = BillboardArt.Create(key, data);
        Assert.NotNull(view);
        Realize(view!, 640, 360);
        var life = Assert.IsAssignableFrom<IBillboardArtView>(view);
        life.Play();
        life.Pause();
        life.Touch(new Point(0.5, 0.5));
        if (view is BillboardVectorArt vector)
        {
            vector.Redraw();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(64, 36, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(view);
        }
        life.Release();
        life.Release();
    });

    [Fact]
    public void Every_vector_art_paints_through_its_whole_loop() => WpfRenderHarness.OnStaThread(() =>
    {
        BuiltInArt.Register();
        foreach (var c in ArtCases())
        {
            if (BillboardArt.Create((string)c[0]!, c[1]) is not BillboardVectorArt view) continue;
            view.Accent = Color.FromRgb(0xff, 0xc9, 0x4a);
            Realize(view, 640, 360);
            for (double t = 0; t <= 8; t += 0.37)
            {
                view.SecondsForTests = t;
                view.Redraw();
            }
            view.Release();
        }
    });

    /// <summary>
    /// Look check, not a judge: with CCP_BOARD_ART_SHOTS set to a folder, every vector art is
    /// written there as PNGs at a few moments, in its real card hue. Does nothing otherwise.
    /// </summary>
    [Fact]
    public void Art_shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_BOARD_ART_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
        BuiltInArt.Register();
        var cases = new (string Name, string Key, object? Data, string Hue)[]
        {
            ("invite", BuiltInArtKeys.Invite, null, "#ffc94a"),
            ("tables", BuiltInArtKeys.Tables, new[] { "Velvet", "mort5366", "Pika" }, "#5fe3ff"),
            ("wheel", BuiltInArtKeys.Wheel, new Dictionary<string, int> { ["done"] = 2, ["total"] = 5 }, "#ffc94a"),
            ("spiral", BuiltInArtKeys.Spiral, null, "#ff4fa8"),
            ("calendar", BuiltInArtKeys.Calendar, new Dictionary<string, int> { ["counted"] = 6, ["need"] = 25, ["days"] = 31, ["today"] = 7 }, "#ff4fa8"),
            ("program", BuiltInArtKeys.Calendar, new Dictionary<string, int> { ["day"] = 3, ["days"] = 14 }, "#ffc94a"),
            ("tip", BuiltInArtKeys.Tip, "tip.rightclick", "#9b7bff"),
        };
        foreach (var c in cases)
        {
            if (BillboardArt.Create(c.Key, c.Data) is not BillboardVectorArt view) continue;
            view.Accent = BillboardVectorArt.ParseHue(c.Hue, Colors.HotPink);
            var host = Realize(view, 1000, 420);
            foreach (var t in new[] { 0.35, 1.6, 2.3, 3.1 })
            {
                view.SecondsForTests = t;
                view.Redraw();
                host.UpdateLayout();
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, 420, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(host);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"{c.Name}-{t.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}.png"));
                enc.Save(f);
            }
            view.Release();
        }
    });
}
