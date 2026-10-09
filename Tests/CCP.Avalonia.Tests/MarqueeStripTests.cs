using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Home marquee (owner, 2026-10-09: stuck on the "v5.2.1 IS OUT" XAML placeholder). Pins the
/// WPF StartMarqueeAnimation port: the strip shows the saved message, scrolls at Full motion and
/// parks one segment at 0 when ambient loops are off.
/// </summary>
public sealed class MarqueeStripTests
{
    private const string Saved = "Parity marquee check";

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static (Window Window, Canvas Canvas, TextBlock Text) Host()
    {
        var text = new TextBlock
        {
            Text = "V5.2.1 IS OUT placeholder",
            FontSize = 28,
            FontWeight = FontWeight.Bold,
            RenderTransform = new TranslateTransform(),
        };
        Canvas.SetTop(text, 8);
        var canvas = new Canvas { ClipToBounds = true, Height = 48 };
        canvas.Children.Add(text);
        var w = new Window { Width = 900, Height = 60, Content = canvas };
        return (w, canvas, text);
    }

    [Fact]
    public Task ScrollsTheSavedMessageAtFullMotion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var before = CoreSettings.Current.MarqueeMessage;
        CoreSettings.Current.MarqueeMessage = Saved;
        MarqueeScroller.AmbientOverride = () => true;
        var (w, canvas, text) = Host();
        w.Show();
        try
        {
            Pump();
            var strip = new MarqueeScroller(canvas, text);
            strip.Start();

            Assert.StartsWith(Saved.ToUpperInvariant() + MarqueeScroller.Separator, text.Text);
            Assert.DoesNotContain("5.2.1", text.Text);
            Assert.True(strip.IsScrolling);
            Assert.True(strip.SegmentWidth > 0);

            var t = strip.Transform;
            Assert.Equal(0, t.X);
            for (int i = 0; i < 6; i++) { Thread.Sleep(40); Pump(); }
            var x1 = t.X;
            Assert.True(x1 < 0, $"strip did not move: X={x1}");
            for (int i = 0; i < 4; i++) { Thread.Sleep(40); Pump(); }
            Assert.True(t.X < x1 || t.X > -strip.SegmentWidth + 1, $"strip stalled at {t.X}");
            Assert.True(t.X <= 0 && t.X > -strip.SegmentWidth);
            strip.Stop();
        }
        finally
        {
            MarqueeScroller.AmbientOverride = null;
            CoreSettings.Current.MarqueeMessage = before;
            w.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ParksOneSegmentWhenAmbientLoopsAreOff() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var before = CoreSettings.Current.MarqueeMessage;
        CoreSettings.Current.MarqueeMessage = Saved;
        MarqueeScroller.AmbientOverride = () => false;
        var (w, canvas, text) = Host();
        w.Show();
        try
        {
            Pump();
            var strip = new MarqueeScroller(canvas, text);
            strip.Start();
            var seg = Saved.ToUpperInvariant() + MarqueeScroller.Separator;
            Assert.Equal(seg + seg, text.Text);
            Assert.False(strip.IsScrolling);
            for (int i = 0; i < 3; i++) { Thread.Sleep(40); Pump(); }
            Assert.Equal(0, strip.Transform.X);
        }
        finally
        {
            MarqueeScroller.AmbientOverride = null;
            CoreSettings.Current.MarqueeMessage = before;
            w.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void BlankMessageFallsBackToTheDefault()
    {
        var fallback = MarqueeScroller.ResolveDefaultMarqueeMessage();
        Assert.False(string.IsNullOrWhiteSpace(fallback));
    }

    [Fact]
    public void ServerBodyParsesTheWpfDto()
    {
        Assert.Equal("HELLO", MainShellWindow.ParseMarqueeMessage("{\"message\":\"HELLO\"}"));
        Assert.Null(MainShellWindow.ParseMarqueeMessage("not json"));
        Assert.Null(MainShellWindow.ParseMarqueeMessage("{}"));
    }

    [Fact]
    public Task HomePageMarqueeShowsTheSavedMessage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var before = CoreSettings.Current.MarqueeMessage;
        CoreSettings.Current.MarqueeMessage = Saved;
        MarqueeScroller.AmbientOverride = () => true;
        var page = new SettingsTabView();
        var w = new Window { Width = 1400, Height = 900, Content = page };
        w.Show();
        try
        {
            for (int i = 0; i < 4; i++) Pump();
            var text = page.FindControl<TextBlock>("MarqueeText")!;
            Assert.StartsWith(Saved.ToUpperInvariant(), text.Text);
            Assert.DoesNotContain("5.2.1", text.Text);
            for (int i = 0; i < 6; i++) { Thread.Sleep(40); Pump(); }
            Assert.True(page.Marquee.Transform.X < 0, $"page strip did not move: X={page.Marquee.Transform.X}");
        }
        finally
        {
            MarqueeScroller.AmbientOverride = null;
            CoreSettings.Current.MarqueeMessage = before;
            w.Close();
        }
        return Task.CompletedTask;
    });
}
