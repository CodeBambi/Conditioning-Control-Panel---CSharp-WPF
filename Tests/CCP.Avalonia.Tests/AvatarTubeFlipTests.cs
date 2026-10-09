using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Owner reports 2026-10-09: the tube art did not mirror when she docked on main's right;
/// she vanished at random; her bubble showed no text.</summary>
public sealed class AvatarTubeFlipTests
{
    private static bool Mirrored(Visual v)
    {
        for (Visual? x = v; x != null; x = x.GetVisualParent())
            if (x.RenderTransform is ScaleTransform { ScaleX: < 0 }) return true;
        return false;
    }

    [Fact]
    public Task RightDock_MirrorsOnlyTheArt_AndTheBubbleFollows() => Run((main, tube) =>
    {
        var screen = tube.Screens.ScreenFromWindow(main);
        Assert.NotNull(screen);   // headless reports one screen; main sits on it
        var frame = tube.FindControl<Image>("ImgTubeFrame")!;
        var bubble = tube.FindControl<Border>("SpeechBubble")!;

        // Left dock (room on the left): nothing mirrored, bubble anchored right at the seam.
        main.Position = new PixelPoint(700, 200);
        Dispatcher.UIThread.RunJobs();
        Assert.False(tube.TubeArtFlipped);
        Assert.False(Mirrored(frame));
        Assert.True(tube.Position.X < main.Position.X);

        // Main near the screen's left edge: she docks on its RIGHT and the art mirrors.
        main.Position = new PixelPoint(screen!.WorkingArea.X + 20, 200);
        Dispatcher.UIThread.RunJobs();
        Assert.True(tube.TubeArtFlipped);
        Assert.True(Mirrored(frame));
        Assert.True(tube.Position.X > main.Position.X);
        tube.GigglePriority("hello from the right", playSound: false, aiGenerated: false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(HorizontalAlignment.Left, bubble.HorizontalAlignment);
        // Text, her sprite and the name plate never read backwards.
        foreach (var name in new[] { "TxtSpeech", "TxtAvatarTitle", "ImgAvatar", "AvatarBorder", "TitleBox" })
            Assert.False(Mirrored(tube.FindControl<Control>(name)!), name + " is mirrored");

        // Back with room on the left: unmirrored again, bubble back on the seam side.
        main.Position = new PixelPoint(700, 200);
        Dispatcher.UIThread.RunJobs();
        Assert.False(tube.TubeArtFlipped);
        Assert.False(Mirrored(frame));
        Assert.Equal(HorizontalAlignment.Right, bubble.HorizontalAlignment);

        // Detach always reads unmirrored.
        main.Position = new PixelPoint(screen.WorkingArea.X + 20, 200);
        Dispatcher.UIThread.RunJobs();
        Assert.True(tube.TubeArtFlipped);
        tube.Detach();
        Assert.False(tube.TubeArtFlipped);
        Assert.False(Mirrored(frame));
    });

    [Fact]
    public Task MinimisedParkingSpot_NeverDragsHerOffTheDesktop() => Run((main, tube) =>
    {
        main.Position = new PixelPoint(700, 200);
        Dispatcher.UIThread.RunJobs();
        var docked = tube.Position;
        main.Position = new PixelPoint(-32000, -32000);   // where Windows parks a minimised window
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(docked, tube.Position);
    });

    [Fact]
    public Task ShownBubble_HasReadableText() => Run((main, tube) =>
    {
        main.Position = new PixelPoint(700, 200);
        Dispatcher.UIThread.RunJobs();
        tube.GigglePriority("Hello, you came back", playSound: false, aiGenerated: false);
        Dispatcher.UIThread.RunJobs();
        var bubble = tube.FindControl<Border>("SpeechBubble")!;
        var text = tube.FindControl<TextBlock>("TxtSpeech")!;
        Assert.True(bubble.IsEffectivelyVisible);
        Assert.Equal("Hello, you came back", text.Text);
        Assert.True(text.IsEffectivelyVisible);
        Assert.True(text.Bounds.Width > 20 && text.Bounds.Height > 10, $"text bounds {text.Bounds}");
        var fg = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;
        Assert.True(fg.A > 200, $"foreground alpha {fg.A}");
        // The fill's darkest/lightest stops vs the text: WCAG-ish luminance gap.
        static double L(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        var fills = bubble.Background switch
        {
            ISolidColorBrush s => new[] { s.Color },
            IGradientBrush g => g.GradientStops.Select(x => x.Color).ToArray(),
            _ => Array.Empty<Color>(),
        };
        Assert.NotEmpty(fills);
        Assert.All(fills, c => Assert.True(Math.Abs(L(c) - L(fg)) > 0.4, $"fill {c} vs text {fg}"));

        // And it actually paints: some pixel inside the text box is near the foreground colour.
        var frame = tube.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var origin = text.TranslatePoint(new Point(0, 0), tube)!.Value;
        double k = frame!.PixelSize.Width / tube.Bounds.Width;
        var r = new PixelRect((int)(origin.X * k), (int)(origin.Y * k),
            Math.Max(1, (int)(text.Bounds.Width * k)), Math.Max(1, (int)(text.Bounds.Height * k)));
        Assert.True(PaintsNear(frame, r, fg), $"no text-coloured pixel in {r}");
    });

    /// <summary>Owner, 2026-10-09: typed to her, got an empty bubble. The send path opens the chat box
    /// and shows the thinking phrase while the reply is out.</summary>
    [Fact]
    public Task ThinkingBubble_WhileChatIsOpen_ShowsItsPhrase() => Run((main, tube) =>
    {
        tube.OpenChatInput();
        Dispatcher.UIThread.RunJobs();
        tube.StartThinkingAnimation();
        Dispatcher.UIThread.RunJobs();
        var bubble = tube.FindControl<Border>("SpeechBubble")!;
        var text = tube.FindControl<TextBlock>("TxtSpeech")!;
        try
        {
            Assert.True(bubble.IsEffectivelyVisible);
            Assert.False(string.IsNullOrEmpty(text.Text));
            Assert.True(text.IsEffectivelyVisible, "TxtSpeech hidden");
            Assert.True(text.Bounds.Width > 20 && text.Bounds.Height > 10, $"text bounds {text.Bounds} bubble {bubble.Bounds}");
            var frame = tube.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (Environment.GetEnvironmentVariable("CCP_TUBE_PNG") is { Length: > 0 } png) frame!.Save(png);
            var fg = ((ISolidColorBrush)text.Foreground!).Color;
            var origin = text.TranslatePoint(new Point(0, 0), tube)!.Value;
            double k = frame!.PixelSize.Width / tube.Bounds.Width;
            var r = new PixelRect((int)(origin.X * k), (int)(origin.Y * k),
                Math.Max(1, (int)(text.Bounds.Width * k)), Math.Max(1, (int)(text.Bounds.Height * k)));
            Assert.True(PaintsNear(frame, r, fg), $"no text-coloured pixel in {r}");
        }
        finally { tube.StopThinkingAnimation(); }
    });

    private static bool PaintsNear(global::Avalonia.Media.Imaging.WriteableBitmap bmp, PixelRect r, Color want)
    {
        using var fb = bmp.Lock();
        var row = new byte[fb.RowBytes];
        for (int y = Math.Max(0, r.Y); y < Math.Min(fb.Size.Height, r.Bottom); y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes, row, 0, row.Length);
            for (int x = Math.Max(0, r.X); x < Math.Min(fb.Size.Width, r.Right); x++)
            {
                int i = x * 4;   // BGRA
                if (Math.Abs(row[i + 2] - want.R) < 40 && Math.Abs(row[i + 1] - want.G) < 40 && Math.Abs(row[i] - want.B) < 40 && row[i + 3] > 200)
                    return true;
            }
        }
        return false;
    }

    private static Task Run(Action<Window, AvatarTubeWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarTubeDetached = false;
        CoreSettings.Current.AvatarMuted = false;
        var main = new Window { Width = 1000, Height = 700, Position = new PixelPoint(700, 200) };
        AvatarTubeWindow? tube = null;
        try
        {
            main.Show();
            tube = new AvatarTubeWindow(main);
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            body(main, tube);
        }
        finally
        {
            tube?.Close();
            main.Close();
            Dispatcher.UIThread.RunJobs();
            service.SealForReset();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
