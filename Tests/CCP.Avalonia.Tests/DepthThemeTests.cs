using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Depth;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The depth law in the head (parity wave 2, lane D2): Theme/Depth.axaml carries Core DepthPalette
/// key by key, PinkButton / SecondaryButton opt into the plank through ctrl:HudPlank.Kind exactly
/// as WPF 7.1.5 Theme/Controls.xaml does, the face travels by HudPlankRules (hover lifts 2, press
/// drops 2 in 90 ms, release springs 140 ms past rest by 1), and a plain SecondaryButton keeps its
/// 0.97 squash. The render test draws a raised and a pressed plank: the pressed face sits 2 px
/// lower and its shadow is gone. Set CCP_D2_PNG to a path to keep the frame.
/// </summary>
public sealed class DepthThemeTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public long Now = TimeSpan.FromSeconds(10).Ticks;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Color C(uint argb) => DepthPaint.ToColor(argb);

    [Fact]
    public Task DepthAxamlCarriesEveryCorePaletteKeyVerbatim() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var app = Application.Current!;
        Assert.Equal(28, DepthPalette.All.Count);
        foreach (var d in DepthPalette.All)
        {
            Assert.True(app.TryGetResource(d.Key, ThemeVariant.Dark, out var res), d.Key + " missing from Depth.axaml");
            switch (res)
            {
                case Color c:
                    Assert.Equal(C(d.Color), c);
                    break;
                case ISolidColorBrush s:
                    Assert.Equal(DepthBrushKind.Solid, d.Kind);
                    Assert.Equal(C(d.Color), s.Color);
                    break;
                case IGradientBrush g:
                    Assert.Equal(d.Stops.Count, g.GradientStops.Count);
                    for (int i = 0; i < d.Stops.Count; i++)
                    {
                        Assert.Equal(C(d.Stops[i].Color), g.GradientStops[i].Color);
                        Assert.Equal(d.Stops[i].Offset, g.GradientStops[i].Offset, 6);
                    }
                    if (g is ILinearGradientBrush l)
                    {
                        Assert.Equal(DepthBrushKind.Linear, d.Kind);
                        Assert.Equal(new RelativePoint(d.Start.X, d.Start.Y, RelativeUnit.Relative), l.StartPoint);
                        Assert.Equal(new RelativePoint(d.End.X, d.End.Y, RelativeUnit.Relative), l.EndPoint);
                    }
                    else if (g is IRadialGradientBrush r)
                    {
                        Assert.Equal(DepthBrushKind.Radial, d.Kind);
                        Assert.Equal(new RelativePoint(d.Start.X, d.Start.Y, RelativeUnit.Relative), r.Center);
                        Assert.Equal(new RelativePoint(d.End.X, d.End.Y, RelativeUnit.Relative), r.GradientOrigin);
                        Assert.Equal(d.RadiusX, r.RadiusX.Scalar, 6);
                        Assert.Equal(d.RadiusY, r.RadiusY.Scalar, 6);
                    }
                    else Assert.Fail(d.Key + " is an unexpected gradient");
                    break;
                default:
                    Assert.Fail(d.Key + " is " + res?.GetType().Name);
                    break;
            }
        }
        return Task.CompletedTask;
    });

    private static Button Make(string theme, HudPlankKind kind)
    {
        var b = new Button
        {
            Theme = (ControlTheme)Application.Current!.FindResource(theme)!,
            Content = "START", Width = 120, Height = 50,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        if (kind != HudPlankKind.None) HudPlank.SetKind(b, kind);
        return b;
    }

    private static T Part<T>(Button b, string name) where T : Control =>
        b.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [Theory]
    [InlineData("PinkButton", HudPlankKind.None)]
    [InlineData("PinkButton", HudPlankKind.Plank)]
    [InlineData("PinkButton", HudPlankKind.Chunky)]
    [InlineData("SecondaryButton", HudPlankKind.None)]
    [InlineData("SecondaryButton", HudPlankKind.Plank)]
    public Task TheThemesOptInExactlyAsControlsXamlDoes(string theme, HudPlankKind kind) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var b = Make(theme, kind);
        var w = new Window { Width = 300, Height = 200, Content = b };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        var app = Application.Current!;

        bool on = kind != HudPlankKind.None;
        Assert.Equal(on, Part<Border>(b, "DepthDrop").IsVisible);
        Assert.Equal(on, Part<Border>(b, "DepthSheen").IsVisible);
        Assert.Equal(on, Part<Border>(b, "DepthBevel").IsVisible);
        if (theme == "PinkButton")
            Assert.Equal(on ? 0 : 1.5, Part<Border>(b, "border").BorderThickness.Top);
        if (kind == HudPlankKind.Chunky)
        {
            Assert.Same(app.FindResource("DepthPlankSheen"), Part<Border>(b, "DepthSheen").Background);
            Assert.Same(app.FindResource("DepthPlankBevel"), Part<Border>(b, "DepthBevel").BorderBrush);
        }
        else if (on)
        {
            Assert.Same(app.FindResource("DepthRaisedSheen"), Part<Border>(b, "DepthSheen").Background);
            Assert.Same(app.FindResource("DepthRaisedBevel"), Part<Border>(b, "DepthBevel").BorderBrush);
            var drop = Part<Border>(b, "DepthDrop");
            Assert.Equal(HudPlankRules.DropBaseHeight(kind), drop.Height);
            Assert.Equal(-HudPlankRules.DropBaseHeight(kind), drop.Margin.Bottom);
            Assert.Equal(DepthRules.RaisedPx / HudPlankRules.DropBaseHeight(kind),
                ((ScaleTransform)drop.RenderTransform!).ScaleY, 6);
        }
        w.Close();
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Off)]
    public Task HoverLiftsPressDropsAndReleaseSpringsPastRest(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var clock = new SteppedClock();
        var oldLevel = HudPlank.Level;
        HudPlank.Time = clock;
        HudPlank.Level = () => level;
        try
        {
            var b = Make("PinkButton", HudPlankKind.Plank);
            b.Margin = new Thickness(20);
            var w = new Window { Width = 300, Height = 200, Content = b };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var slide = (TranslateTransform)Part<Panel>(b, "DepthFace").RenderTransform!;
            var scale = (ScaleTransform)Part<Border>(b, "DepthDrop").RenderTransform!;
            Assert.Equal(0, slide.Y);

            double min = 0, max = 0;
            void Run(int ms)
            {
                for (int t = 0; t < ms; t += 10)
                {
                    clock.Now += TimeSpan.FromMilliseconds(10).Ticks;
                    HudPlank.Step(b);
                    min = Math.Min(min, slide.Y); max = Math.Max(max, slide.Y);
                }
            }

            var centre = new Point(80, 45);
            w.MouseMove(centre, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            if (level == MotionLevel.Off) Assert.Equal(-DepthRules.HoverLiftPx, slide.Y);
            else Assert.True(slide.Y > -DepthRules.HoverLiftPx, "hover should glide, not snap");
            Run(DepthRules.HoverMs);
            Assert.Equal(-DepthRules.HoverLiftPx, slide.Y, 6);
            Assert.Equal(1.0, scale.ScaleY, 6);   // hovered: the longest shadow

            w.MouseDown(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Run(DepthRules.PressMs);
            Assert.Equal(DepthRules.PressTravelPx, slide.Y, 6);
            Assert.Equal(0, scale.ScaleY, 6);     // pressed: no shadow

            min = 0;
            w.MouseUp(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Run(DepthRules.ReleaseMs + 20);
            Assert.Equal(-DepthRules.HoverLiftPx, slide.Y, 6);
            if (level == MotionLevel.Off) Assert.Equal(-DepthRules.HoverLiftPx, min, 6);
            else Assert.Equal(-DepthRules.HoverLiftPx - DepthRules.ReleaseOvershootPx, min, 1);   // the spring
            w.Close();
        }
        finally { HudPlank.Time = TimeProvider.System; HudPlank.Level = oldLevel; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(HudPlankKind.None)]
    [InlineData(HudPlankKind.Plank)]
    public Task APlainSecondaryButtonSquashesAndAPlankDoesNot(HudPlankKind kind) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var clock = new SteppedClock();
        PressSquish.Time = clock;
        try
        {
            var b = Make("SecondaryButton", kind);
            var w = new Window { Width = 300, Height = 200, Content = b };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var plate = Part<Border>(b, "border");
            double Scale() => (plate.RenderTransform as ScaleTransform)?.ScaleX ?? 1;
            double peak = 1;
            void Run(int ms)
            {
                for (int t = 0; t < ms; t += 10)
                {
                    clock.Now += TimeSpan.FromMilliseconds(10).Ticks;
                    PressSquish.Step(b);
                    peak = Math.Max(peak, Scale());
                }
            }
            var centre = new Point(60, 25);
            w.MouseMove(centre, RawInputModifiers.None);
            w.MouseDown(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Run(PressSquish.DownMs);
            Assert.Equal(kind == HudPlankKind.None ? 0.97 : 1.0, Scale(), 6);
            w.MouseUp(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Run(PressSquish.ReleaseMs + 20);
            Assert.Equal(1.0, Scale(), 6);
            if (kind == HudPlankKind.None) Assert.True(peak > 1.0, "the release should spring past 1.0");
            else Assert.Equal(1.0, peak);
            w.Close();
        }
        finally { PressSquish.Time = TimeProvider.System; }
        return Task.CompletedTask;
    });

    private static uint[] Pixels(WriteableBitmap bmp, out int width)
    {
        using var fb = bmp.Lock();
        width = fb.Size.Width;
        var px = new int[fb.Size.Width * fb.Size.Height];
        for (int y = 0; y < fb.Size.Height; y++)
            Marshal.Copy(fb.Address + y * fb.RowBytes, px, y * fb.Size.Width, fb.Size.Width);
        return px.Select(p => (uint)p).ToArray();
    }

    [Fact]
    public Task APressedPlankSits2PxLowerAndLosesItsShadow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var oldLevel = HudPlank.Level;
        HudPlank.Level = () => MotionLevel.Off;   // the rest pose, no clock
        try
        {
            var raised = Make("PinkButton", HudPlankKind.Plank);
            var pressed = Make("PinkButton", HudPlankKind.Plank);
            raised.Margin = new Thickness(30, 40, 0, 0);
            pressed.Margin = new Thickness(190, 40, 0, 0);
            var bg = Color.FromRgb(0x70, 0x70, 0x80);
            var w = new Window
            {
                Width = 340, Height = 130, Background = new SolidColorBrush(bg),
                Content = new Panel { Children = { raised, pressed } },
            };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.MouseMove(new Point(250, 65), RawInputModifiers.None);
            w.MouseDown(new Point(250, 65), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(pressed.IsPressed);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            var frame = w.CaptureRenderedFrame()!;
            if (Environment.GetEnvironmentVariable("CCP_D2_PNG") is { Length: > 0 } png)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png))!);
                frame.Save(png);
            }
            var px = Pixels(frame, out int width);
            bool Pink(uint p) => Math.Max((p >> 16) & 0xFF, Math.Max((p >> 8) & 0xFF, p & 0xFF)) > 0xD0;   // the plate (any pink, lit or hovered: one channel near full); bg and shadow sit far below
            int Lum(uint p) => (int)(((p >> 16) & 0xFF) + ((p >> 8) & 0xFF) + (p & 0xFF));
            int bgLum = bg.R + bg.G + bg.B;
            int Top(int x) { for (int y = 0; y < 130; y++) if (Pink(px[y * width + x])) return y; return -1; }
            int Bottom(int x) { for (int y = 129; y >= 0; y--) if (Pink(px[y * width + x])) return y; return -1; }

            int rx = 90, prx = 250;
            int rTop = Top(rx), pTop = Top(prx), rBot = Bottom(rx), pBot = Bottom(prx);
            Assert.True(rTop >= 0 && pTop >= 0, $"no plate found ({rTop}, {pTop})");
            Assert.InRange(rTop, 40, 41);          // the plate starts at the button's top (bevel row aside)
            Assert.True(pTop - rTop == 2, $"tops {rTop} {pTop} bottoms {rBot} {pBot}");          // PressTravelPx
            Assert.True(pBot - rBot == 2, $"bottoms {rBot} {pBot} raised " + string.Join(",", Enumerable.Range(84, 12).Select(y => px[y * width + rx].ToString("X8"))) + " pressed " + string.Join(",", Enumerable.Range(84, 12).Select(y => px[y * width + prx].ToString("X8"))));
            // The buttons span rows 40..89. The raised plank throws its RaisedPx (3) shadow on rows
            // 90..92 and nothing past it; the pressed face covers 42..91 and throws no shadow at all.
            int L(int x, int y) => Lum(px[y * width + x]);
            Assert.True(L(rx, 90) < bgLum - 60 && L(rx, 91) < bgLum - 60, $"raised plank has no shadow ({L(rx, 90)}, {L(rx, 91)} vs {bgLum})");
            Assert.True(Math.Abs(L(rx, 94) - bgLum) < 8, "the shadow runs past RaisedPx");
            Assert.True(Math.Abs(L(prx, 92) - bgLum) < 8 && Math.Abs(L(prx, 93) - bgLum) < 8, "pressed plank still throws a shadow");
            w.Close();
        }
        finally { HudPlank.Level = oldLevel; }
        return Task.CompletedTask;
    });
}
