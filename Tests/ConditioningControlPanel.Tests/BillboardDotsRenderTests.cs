using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Tonight Board's dot row (owner, 2026-10-07: "the pills under the slideshow should just be
/// dots"): one small dot per card, the current one a wider capsule that the hold fills, every dot
/// named after its card, and no words on the row. With CCP_BOARD_DOT_SHOTS set to a folder the
/// row is also written there as PNGs for a look check.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BillboardDotsRenderTests
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

    private static (BillboardCardHost Host, Grid Root, BillboardDeck Deck) Build(double w = 1000, double h = 460)
    {
        BuiltInArt.Register();
        var list = new ListProvider();
        list.Cards.Add(new BillboardCardSpec("live", BillboardCardKind.Live, 0, "live now", "3 open tables", "join one", "#5fe3ff",
            BuiltInArtKeys.Tables, new Dictionary<string, int> { ["count"] = 3 },
            new BillboardAction(BillboardActionKind.Tab, "availablesubjects", "Join")));
        list.Cards.Add(new BillboardCardSpec("wait", BillboardCardKind.Waiting, 0, "waiting", "Your free spin", "", "#ffc94a",
            BuiltInArtKeys.Wheel, new Dictionary<string, int> { ["done"] = 1, ["total"] = 3 }, BillboardAction.None));
        list.Cards.Add(new BillboardCardSpec("resume", BillboardCardKind.Resume, 0, "resume", "Pick up where you left", "", "#ff4fa8",
            BuiltInArtKeys.Spiral, null, BillboardAction.None));
        var house = new HouseProvider(k => k);
        var deck = new BillboardDeck(() => new IBillboardProvider[] { list, house },
            () => new BillboardContext(BillboardTier.Free, Now, Now));
        var cardHost = new BillboardCardHost(deck);
        var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        root.Children.Add(cardHost);
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        cardHost.Begin();
        root.UpdateLayout();
        return (cardHost, root, deck);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    /// <summary>The dot's face: the plate Border that holds the fill (its Width is a local value).</summary>
    private static Border Face(Button dot) =>
        LogicalDescendants<Border>((DependencyObject)dot.Content).First(b => b.Child is Grid);

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject node) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            if (child is T t) yield return t;
            foreach (var d in LogicalDescendants<T>(child)) yield return d;
        }
    }

    [Fact]
    public void Every_card_gets_a_named_dot_and_the_current_one_is_a_capsule() => WpfRenderHarness.OnStaThread(() =>
    {
        var (host, _, deck) = Build();
        var dots = host.ChipButtons.OfType<Button>().ToList();
        Assert.Equal(deck.Cards.Count, dots.Count);

        for (int i = 0; i < dots.Count; i++)
        {
            var dot = dots[i];
            var name = BillboardCardHost.ChipLabel(deck.Cards[i]);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.Equal(name, dot.ToolTip as string);
            Assert.Equal(name, AutomationProperties.GetName(dot));
            // Dots carry no words.
            Assert.Empty(LogicalDescendants<TextBlock>((DependencyObject)dot.Content));

            double width = (double)Face(dot).ReadLocalValue(FrameworkElement.WidthProperty);
            Assert.Equal(i == deck.Index ? BillboardCardHost.DotCurrentPx : BillboardCardHost.DotPx, width);
            Assert.Equal(BillboardCardHost.DotPx, Face(dot).Height);
        }
        Assert.True(BillboardCardHost.DotCurrentPx > BillboardCardHost.DotPx * 2.5);
    });

    [Fact]
    public void The_capsule_moves_with_the_deck() => WpfRenderHarness.OnStaThread(() =>
    {
        var (host, root, deck) = Build();
        host.Advance();
        root.UpdateLayout();
        var dots = host.ChipButtons.OfType<Button>().ToList();
        Assert.Equal(1, deck.Index);
        Assert.Equal(BillboardCardHost.DotCurrentPx, (double)Face(dots[1]).ReadLocalValue(FrameworkElement.WidthProperty));
        Assert.Equal(BillboardCardHost.DotPx, (double)Face(dots[0]).ReadLocalValue(FrameworkElement.WidthProperty));
        // A new card starts its hold from nothing.
        Assert.Equal(0, host.HoldProgress, 3);
    });

    /// <summary>Look check, not a judge. Does nothing without CCP_BOARD_DOT_SHOTS.</summary>
    [Fact]
    public void Dot_shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_BOARD_DOT_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
        var (host, root, _) = Build();
        var fillField = typeof(BillboardCardHost).GetField("_fill", BindingFlags.NonPublic | BindingFlags.Instance)!;

        void Shot(string name, double progress)
        {
            root.UpdateLayout();
            // Pin every running animation to its end so the still reads like the live row.
            foreach (var b in LogicalDescendants<Border>(host))
            {
                b.BeginAnimation(FrameworkElement.WidthProperty, null);
            }
            foreach (var s in Descendants<FrameworkElement>(host).Select(e => e.RenderTransform).OfType<ScaleTransform>())
            {
                s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
            if (fillField.GetValue(host) is ScaleTransform fill)
            {
                fill.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                fill.ScaleX = progress;
            }
            root.UpdateLayout();
            var bmp = new RenderTargetBitmap(2000, 920, 192, 192, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var f = System.IO.File.Create(System.IO.Path.Combine(dir, name + ".png"))) enc.Save(f);
            // A close crop of the row itself.
            var row = host.ChipButtons.OfType<FrameworkElement>().First();
            var at = row.TranslatePoint(new Point(0, 0), root);
            int y = Math.Clamp((int)((at.Y - 14) * 2), 0, 920 - 80);
            var crop = new CroppedBitmap(bmp, new Int32Rect(700, y, 600, 80));
            var big = new TransformedBitmap(crop, new ScaleTransform(2, 2));
            var enc2 = new PngBitmapEncoder();
            enc2.Frames.Add(BitmapFrame.Create(big));
            using var f2 = System.IO.File.Create(System.IO.Path.Combine(dir, name + "-row.png"));
            enc2.Save(f2);
        }

        Shot("dots-0-start", 0.05);
        Shot("dots-0-half", 0.5);
        host.Advance();
        Shot("dots-1-late", 0.85);
        host.Advance();
        host.Advance();
        Shot("dots-3-quarter", 0.25);
    });
}
