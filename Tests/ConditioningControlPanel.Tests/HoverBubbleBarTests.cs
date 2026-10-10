using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 4 made the five Home pills (Webcam &amp; Mic, System, Scheduler + Intensity
/// Ramp, CCP Catalogue, App Info &amp; Data) round hover bubbles; wave 5 (owner, 2026-10-06:
/// compact the account strip and the bubble row into ONE line) put them on the account strip
/// beside the name, Link phone, Logout, the Discord pill and the Rich Presence switch, which is
/// a bubble too. Realizes the REAL SettingsTabView at the page width and holds: the strip is one
/// line, the order reads name | phone, logout | Discord, status | the five | ?, every bubble is
/// 34 px and 8 px from the next, Logout wears the pink fill, the switch lights up when checked,
/// phone and Logout follow the signed-in face, one label opens at a time and is fully readable.
/// Set <c>CCP_NAV_PNG_DIR</c> to write strip-out.png, strip-in.png and strip-open.png.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class HoverBubbleBarTests
{
    private const double PageWidth = 1585 - 96;
    private const double PageHeight = 865;

    private static Grid Realize(FrameworkElement element)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(PageWidth, PageHeight));
        host.Arrange(new Rect(0, 0, PageWidth, PageHeight));
        host.UpdateLayout();
        return host;
    }

    private static void MaybeRender(FrameworkElement host, Rect crop, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var full = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        full.Render(host);
        var r = Rect.Intersect(crop, new Rect(0, 0, full.PixelWidth, full.PixelHeight));
        var cropped = new CroppedBitmap(full, new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(cropped));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    private static Rect Bounds(FrameworkElement e, Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    [Fact]
    public void The_strip_is_one_line_in_order_and_one_bubble_opens_at_a_time() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new SettingsTabView();
        var bar = page.HomeBubbleBar;
        ButtonBase[] expected =
        {
            page.BtnLinkPhone, page.BtnQuickLogout, page.ChkQuickDiscordRichPresence,
            page.VelvetBtnWebcam, page.VelvetBtnSystem, page.VelvetBtnSchedulerRamp,
            page.VelvetBtnCatalogue, page.VelvetBtnAppInfo,
        };
        Assert.Equal(expected, bar.Bubbles.ToArray());
        ButtonBase[] five = { page.VelvetBtnWebcam, page.VelvetBtnSystem, page.VelvetBtnSchedulerRamp,
                              page.VelvetBtnCatalogue, page.VelvetBtnAppInfo };

        var host = Realize(page);
        var strip = Bounds(page.AccountStrip, host);
        Assert.InRange(page.AccountStrip.ActualHeight, 40, 56);   // one line: a 34 px bubble plus the padding

        // Signed out: the Login pill on the left, Link phone and Logout (and their divider) gone.
        Assert.Equal(Visibility.Collapsed, page.LoggedInStatusPanel.Visibility);
        Assert.Equal(Visibility.Collapsed, page.BtnLinkPhone.Visibility);
        Assert.Equal(Visibility.Collapsed, page.BtnQuickLogout.Visibility);
        Assert.Equal(Visibility.Collapsed, page.AccountStripDivider.Visibility);
        Assert.Equal(Visibility.Visible, page.BtnUnifiedLogin.Visibility);

        void HoldsTheLine(string when)
        {
            var sr = Bounds(page.AccountStrip, host);
            foreach (var b in bar.Bubbles.Where(b => b.Visibility == Visibility.Visible))
            {
                var r = Bounds(b, host);
                Assert.Equal(HoverBubbleBar.BubbleSize, r.Width, 1);
                Assert.Equal(HoverBubbleBar.BubbleSize, r.Height, 1);
                Assert.True(r.Top >= sr.Top && r.Bottom <= sr.Bottom + 0.5, $"{when}: a bubble leaves the line");
            }
            var rects = five.Select(b => Bounds(b, host)).ToArray();
            for (int i = 1; i < rects.Length; i++)
                Assert.Equal(HoverBubbleBar.Gap, rects[i].Left - rects[i - 1].Right, 1);

            // Order: name face | Discord pill | status bubble | the five | ?
            var face = Bounds(page.BtnUnifiedLogin.Visibility == Visibility.Visible ? page.BtnUnifiedLogin : page.LoggedInStatusPanel, host);
            var pill = Bounds(page.BtnJoinDiscord, host);
            var status = Bounds(page.ChkQuickDiscordRichPresence, host);
            var help = Bounds(page.HelpBtnQuickLinks, host);
            Assert.True(face.Right < pill.Left, $"{when}: the Discord pill is not right of the name");
            Assert.True(pill.Right < status.Left, $"{when}: the status bubble is not right of the pill");
            Assert.True(status.Right < rects[0].Left, $"{when}: the five are not right of the status bubble");
            Assert.True(rects[^1].Right < help.Left, $"{when}: the ? is not last");
            Assert.True(help.Right <= sr.Right, $"{when}: the ? runs past the strip");
            Assert.Equal(34, pill.Height, 1);
        }
        HoldsTheLine("signed out");
        var crop = new Rect(strip.Left - 4, strip.Top - 8, strip.Width + 8, strip.Height + 16);
        MaybeRender(host, crop, "strip-out.png");

        // Signed in (MainWindow.Login.cs flips exactly these): the starred name, then phone + Logout.
        page.LoggedInStatusPanel.Visibility = Visibility.Visible;
        page.BtnUnifiedLogin.Visibility = Visibility.Collapsed;
        page.TxtLoggedInName.Text = "⭐ CodeBambi";
        host.UpdateLayout();
        Assert.Equal(Visibility.Visible, page.BtnLinkPhone.Visibility);
        Assert.Equal(Visibility.Visible, page.BtnQuickLogout.Visibility);
        Assert.Equal(Visibility.Visible, page.AccountStripDivider.Visibility);
        Assert.InRange(page.AccountStrip.ActualHeight, 40, 56);
        var name = Bounds(page.LoggedInStatusPanel, host);
        var phone = Bounds(page.BtnLinkPhone, host);
        var logout = Bounds(page.BtnQuickLogout, host);
        Assert.True(name.Right < phone.Left, "Link phone is not right of the name");
        Assert.Equal(HoverBubbleBar.Gap, logout.Left - phone.Right, 1);
        Assert.True(logout.Right < Bounds(page.BtnJoinDiscord, host).Left, "Logout is not left of the Discord pill");
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), bar.RestFillOf(page.BtnQuickLogout));   // the dangerous one stays pink
        Assert.Equal(HoverBubbleBar.RestFill, bar.RestFillOf(page.BtnLinkPhone));
        Assert.Equal("Logged in as", page.LoggedInStatusPanel.ToolTip);
        HoldsTheLine("signed in");
        MaybeRender(host, crop, "strip-in.png");

        // Labels.
        Assert.Equal("Link phone", bar.LabelOf(page.BtnLinkPhone));
        Assert.Equal("Logout", bar.LabelOf(page.BtnQuickLogout));
        Assert.Equal("Show in Discord status", bar.LabelOf(page.ChkQuickDiscordRichPresence));
        Assert.Equal("Webcam & Mic", bar.LabelOf(page.VelvetBtnWebcam));
        Assert.Equal("System", bar.LabelOf(page.VelvetBtnSystem));
        Assert.Equal("Scheduler + Intensity Ramp", bar.LabelOf(page.VelvetBtnSchedulerRamp));
        Assert.Equal("CCP Catalogue", bar.LabelOf(page.VelvetBtnCatalogue));
        Assert.Equal("App Info & Data", bar.LabelOf(page.VelvetBtnAppInfo));
        Assert.Equal("System", AutomationProperties.GetName(page.VelvetBtnSystem));
        Assert.Equal("Opens app.cclabs.app/catalogue in your browser", page.VelvetBtnCatalogue.ToolTip);

        // The status bubble lights up with the CheckBox the AccountShell mirror writes.
        Assert.False(bar.IsLit(page.ChkQuickDiscordRichPresence));
        page.ChkQuickDiscordRichPresence.IsChecked = true;
        Assert.True(bar.IsLit(page.ChkQuickDiscordRichPresence));
        page.ChkQuickDiscordRichPresence.IsChecked = false;
        Assert.False(bar.IsLit(page.ChkQuickDiscordRichPresence));

        // Open the widest one (forcing the hover state), then another: only one stays open.
        var rightEnd = Bounds(page.VelvetBtnAppInfo, host).Right;
        var nameBefore = Bounds(page.LoggedInStatusPanel, host);
        var leftNeighbour = Bounds(page.VelvetBtnSystem, host);
        bar.Expand(page.VelvetBtnAppInfo, animate: false);
        bar.Expand(page.VelvetBtnSchedulerRamp, animate: false);
        host.UpdateLayout();
        Assert.True(bar.IsExpanded(page.VelvetBtnSchedulerRamp));
        Assert.False(bar.IsExpanded(page.VelvetBtnAppInfo));
        var label = (TextBlock)FindLabel(page.VelvetBtnSchedulerRamp);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var plate = (Border)page.VelvetBtnSchedulerRamp.Content;
        Assert.True(plate.ActualWidth >= HoverBubbleBar.BubbleSize + label.DesiredSize.Width - 1,
            $"open plate {plate.ActualWidth} does not fit its label {label.DesiredSize.Width}");
        // The label FLOATS: the button's layout stays one bubble wide, so nothing on the line
        // moves (the glyph end, the name, the neighbours) and the plate reaches left over the
        // bubble beside it, opaque, on top.
        Assert.Equal(HoverBubbleBar.BubbleSize, page.VelvetBtnSchedulerRamp.ActualWidth, 1);
        Assert.Equal(rightEnd, Bounds(page.VelvetBtnAppInfo, host).Right, 1);
        Assert.Equal(nameBefore, Bounds(page.LoggedInStatusPanel, host));
        Assert.Equal(leftNeighbour, Bounds(page.VelvetBtnSystem, host));
        var plateRect = Bounds(plate, host);
        Assert.True(plateRect.Left < leftNeighbour.Right, "the open plate does not reach over its left neighbour");
        Assert.Equal(1, Panel.GetZIndex(page.VelvetBtnSchedulerRamp));
        Assert.Equal(255, ((SolidColorBrush)plate.Background).Color.A);
        Assert.InRange(page.AccountStrip.ActualHeight, 40, 56);
        MaybeRender(host, crop, "strip-open.png");

        bar.Collapse(page.VelvetBtnSchedulerRamp, animate: false);
        host.UpdateLayout();
        Assert.False(bar.IsExpanded(page.VelvetBtnSchedulerRamp));
        Assert.Equal(HoverBubbleBar.BubbleSize, page.VelvetBtnSchedulerRamp.ActualWidth, 1);
    });

    private static FrameworkElement FindLabel(ButtonBase b)
    {
        var plate = (Border)b.Content;
        var row = (StackPanel)plate.Child;
        return (FrameworkElement)((Border)row.Children[0]).Child;
    }

    [Theory]
    [InlineData("⚙ System", "System")]
    [InlineData("📅 Scheduler", "Scheduler")]
    [InlineData("App Info & Data", "App Info & Data")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Leading_emoji_leave_the_label(string? raw, string expected) =>
        Assert.Equal(expected, HoverBubbleBar.StripLeadingGlyph(raw));
}
