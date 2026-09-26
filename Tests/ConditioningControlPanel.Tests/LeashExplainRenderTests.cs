using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Leash.Explain;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The leash explainer, realized offscreen and read back as pixels. Every card is rendered in its
/// resting state (what Motion Off shows and what the animation settles on), and a copy is saved
/// to D:/wt-leash/shots when that folder exists, for the owner's review.
/// </summary>
public class LeashExplainRenderTests
{
    private const string ShotDir = @"D:\wt-leash\shots";

    private static byte[] Render(FrameworkElement root, int w, int h, string? shot)
    {
        var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x10, 0x0A, 0x1E)), Child = root, Padding = new Thickness(12) };
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);
        if (shot != null && Directory.Exists(ShotDir))
        {
            try
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(Path.Combine(ShotDir, shot));
                enc.Save(fs);
            }
            catch { /* a locked shot file must not fail the suite */ }
        }
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return px;
    }

    private static int Count(byte[] px, int w, Rect area, Func<byte, byte, byte, bool> match)
    {
        int n = 0;
        for (int y = (int)area.Top; y < (int)area.Bottom; y++)
            for (int x = (int)area.Left; x < (int)area.Right; x++)
            {
                int i = (y * w + x) * 4;
                if (match(px[i + 2], px[i + 1], px[i])) n++;
            }
        return n;
    }

    private static bool IsMint(byte r, byte g, byte b) => g > 200 && b > 150 && r < 150;
    private static bool IsGold(byte r, byte g, byte b) => r > 220 && g > 170 && b < 140;
    private static bool IsPink(byte r, byte g, byte b) => r > 220 && g < 140 && b > 130;

    private static Rect BoundsIn(FrameworkElement child, FrameworkElement ancestor)
        => child.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, child.ActualWidth, child.ActualHeight));

    [Theory]
    [InlineData(LeashIntroSide.Leashed, "explain-leashed-row.png")]
    [InlineData(LeashIntroSide.Holder, "explain-holder-row.png")]
    public void RowCardDrawsFourPanelsAndTwoLines(LeashIntroSide side, string shot)
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new LeashExplainCard(side, "Vex", LeashExplainLayout.Row) { Width = 600 };
            const int W = 624, H = 260;
            var px = Render(card, W, H, shot);

            Assert.Equal(4, card.Panels.Count);
            Assert.Equal(2, card.Lines.Count);
            foreach (var p in card.Panels) Assert.True(p.ActualWidth > 120 && p.ActualHeight > 90, $"panel too small {p.ActualWidth}x{p.ActualHeight}");

            // Panels sit in one row.
            var tops = card.Panels.Select(p => Math.Round(p.TranslatePoint(new Point(0, 0), card).Y)).Distinct().ToArray();
            Assert.Single(tops);

            var host = (FrameworkElement)card.Parent;
            // Panel 4 is the scissors: mint steel and gold leash.
            var cut = BoundsIn(card.Panels[3], host);
            Assert.True(Count(px, W, cut, IsMint) > 120, "expected mint scissors in panel 4");
            Assert.True(Count(px, W, cut, IsGold) > 40, "expected the cut gold leash in panel 4");
            // Panel 2 has the picked pink bar and the pink person.
            Assert.True(Count(px, W, BoundsIn(card.Panels[1], host), IsPink) > 150, "expected the pink figure and bar in panel 2");
            // The panels are not blank glass.
            foreach (var p in card.Panels)
            {
                int lit = Count(px, W, BoundsIn(p, host), (r, g, b) => r + g + b > 420);
                Assert.True(lit > 80, $"panel {p.Tag} looks empty ({lit} bright pixels)");
            }
        });
    }

    [Fact]
    public void GridCardFitsTheDrawer()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            // The friends drawer is 300 px; the ask card has about 272 px inside its padding.
            var card = new LeashExplainCard(LeashIntroSide.Leashed, "Vex", LeashExplainLayout.Grid) { Width = 272 };
            var px = Render(card, 296, 420, "explain-leashed-grid.png");

            var host = (FrameworkElement)card.Parent;
            Assert.True(card.ActualHeight < 396, $"grid card is {card.ActualHeight} px tall, too tall for an ask card");
            var p0 = card.Panels[0].TranslatePoint(new Point(0, 0), card);
            var p1 = card.Panels[1].TranslatePoint(new Point(0, 0), card);
            var p2 = card.Panels[2].TranslatePoint(new Point(0, 0), card);
            Assert.Equal(Math.Round(p0.Y), Math.Round(p1.Y));
            Assert.True(p2.Y > p0.Y + 50, "panel 3 should start the second row");
            Assert.True(Count(px, 296, BoundsIn(card.Panels[3], host), IsMint) > 60, "expected mint scissors in the drawer card");
        });
    }

    [Fact]
    public void WindowRendersWithGotIt()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var w = LeashExplainer.Build(LeashIntroSide.Leashed, "Vex", "How the leash works", offer: null, out var card);
            var shell = (FrameworkElement)w.Content;
            w.Content = null;
            var px = Render(shell, 700, 420, "explain-window-leashed.png");
            Assert.NotNull(Find(shell, "leash-explain-got-it"));
            Assert.Null(Find(shell, "leash-explain-offer"));
            Assert.Equal(4, card.Panels.Count);
            Assert.True(Count(px, 700, new Rect(0, 0, 700, 420), IsMint) > 300);
        });
    }

    [Fact]
    public void BeforeOfferWindowHasOfferAndNotNow()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var w = LeashExplainer.Build(LeashIntroSide.Holder, "Kit", "Offer Kit the leash", offer: () => { }, out _);
            var shell = (FrameworkElement)w.Content;
            w.Content = null;
            Render(shell, 700, 420, "explain-window-holder-offer.png");
            Assert.NotNull(Find(shell, "leash-explain-offer"));
            Assert.NotNull(Find(shell, "leash-explain-not-now"));
            Assert.Null(Find(shell, "leash-explain-got-it"));
        });
    }

    [Fact]
    public void BeforeOfferSendsAtOnceWhenAlreadySeen()
    {
        int sent = 0;
        var s = new AppSettings { LeashIntroSeenHolder = true };
        Assert.True(LeashExplainer.BeforeOffer(null, "Kit", () => sent++, s));
        Assert.Equal(1, sent);
    }

    [Fact]
    public void AskIntroHoldsPutItOnUntilRead()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var slot = new StackPanel();
            var intensity = new Border { Height = 30, Tag = "intensity" };
            var putOn = new Button { Content = "Put it on" };
            slot.Children.Add(intensity);
            slot.Children.Add(putOn);

            var fresh = new AppSettings();
            var card = LeashAskIntro.Attach(slot, 0, putOn, "Vex", fresh);
            Assert.NotNull(card);
            Assert.Same(card, slot.Children[0]);
            Assert.Same(intensity, slot.Children[1]);
            Assert.False(putOn.IsEnabled);
            Assert.False(card!.IsReadyForAnswer);

            // Answering records the flag; the next ask card carries no explainer.
            LeashAskIntro.Answered(fresh);
            Assert.True(fresh.LeashIntroSeenLeashed);
            var slot2 = new StackPanel();
            var putOn2 = new Button();
            slot2.Children.Add(putOn2);
            Assert.Null(LeashAskIntro.Attach(slot2, 0, putOn2, "Vex", fresh));
            Assert.True(putOn2.IsEnabled);
            Assert.Single(slot2.Children);
        });
    }

    [Fact]
    public void ReadClockFiresAtOnceWhenAlreadySeen()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new LeashExplainCard(LeashIntroSide.Leashed, null, LeashExplainLayout.Grid);
            int fired = 0;
            card.ReadyForAnswer += () => fired++;
            card.StartReadClock(alreadySeen: true);
            card.StartReadClock(alreadySeen: true);
            Assert.True(card.IsReadyForAnswer);
            Assert.Equal(1, fired);
        });
    }

    private static FrameworkElement? Find(DependencyObject root, string tag)
    {
        if (root is FrameworkElement fe && Equals(fe.Tag, tag)) return fe;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (Find(child, tag) is { } hit) return hit;
        return null;
    }
}
