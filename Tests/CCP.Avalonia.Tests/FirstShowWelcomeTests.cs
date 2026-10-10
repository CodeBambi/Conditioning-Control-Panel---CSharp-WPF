using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.FirstShow;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>EMI's welcome show on this head (WPF Services/FirstShow): the stage's input style (the WPF
/// FirstShowGuideInputTests fact), the beats in order, the effects' IN and OUT, the doors' refusals, the
/// panic stop, and the promise that a demo choice never changes a setting.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps the settings service, the panic list and the show's static seams
public sealed class FirstShowWelcomeTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static string[] Pictures(string dir, int count)
    {
        Directory.CreateDirectory(dir);
        return Enumerable.Range(0, count).Select(i =>
        {
            var path = Path.Combine(dir, "p" + i + ".png");
            using var bmp = new SKBitmap(64, 48);
            bmp.Erase(new SKColor((byte)(40 * i), 90, 160));
            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            File.WriteAllBytes(path, data.ToArray());
            return path;
        }).ToArray();
    }

    // WPF FirstShowGuideInputTests.Painted_guide_passes_native_input_through: the stage's extended style
    // is transparent to input, layered and never activates, and applying it twice changes nothing.
    [Fact]
    public void The_stage_style_is_click_through_layered_and_non_activating()
    {
        uint style = Win32Overlay.Style(0, clickThrough: true, passive: true);
        Assert.NotEqual(0u, style & Win32Overlay.WsExTransparent);
        Assert.NotEqual(0u, style & 0x80000u);       // WS_EX_LAYERED
        Assert.NotEqual(0u, style & 0x08000000u);    // WS_EX_NOACTIVATE
        Assert.NotEqual(0u, style & Win32Overlay.WsExToolWindow);
        Assert.Equal(style, Win32Overlay.Style(style, clickThrough: true, passive: true));
    }

    [Fact]
    public Task Effects_come_in_and_go_out_and_a_bubble_pops() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-fs-test-" + Guid.NewGuid().ToString("N"));
        var cues = new List<string>();
        var fx = new FirstShowEffects(cues.Add, 1280, 720) { Motion = () => MotionLevel.Full, Particles = () => true, Words = k => k };
        try
        {
            await fx.LoadAsync(Pictures(dir, 8));
            Assert.Equal(8, fx.LoadedCount);
            for (double t = 0; t < 20.6; t += .1) fx.Tick(t, .1);
            Assert.Contains("flash", cues);
            Assert.Contains("bubbles", cues);
            var bubble = fx.Bubbles().First();
            Assert.True(fx.Pop(bubble.X, bubble.Y));
            Assert.Contains("pop", cues);
            Assert.False(fx.Pop(-500, -500));
            using (var surface = SKSurface.Create(new SKImageInfo(640, 360)))
                fx.Render(surface.Canvas, 640, 360);   // every layer paints without a throw
            for (double t = 20.6; t < 35; t += .1) fx.Tick(t, .1);
            Assert.Contains("reveal", cues);
            Assert.Equal(0, fx.ActorCount);            // the OUT: everything gathered and gone
            Assert.Empty(fx.Bubbles());
        }
        finally { fx.Dispose(); try { Directory.Delete(dir, true); } catch { } }
    });

    [Fact]
    public Task The_show_plays_its_beats_on_the_real_desk_changes_no_setting_and_panic_stops_it() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var oldPanic = PanicSurfaces.All;
        var (oldSession, oldLock, oldStrict, oldRequire) =
            (FirstShowService.SessionRunning, FirstShowService.LockdownActive, FirstShowService.StrictLock, FirstShowService.RequireClickThrough);
        var dir = Path.Combine(Path.GetTempPath(), "ccp-fs-test-" + Guid.NewGuid().ToString("N"));
        var svc = EmiDeskService.Instance;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;   // no entrance to wait for
            CoreSettings.Current.MasterVolume = 0;
            FirstShowService.RequireClickThrough = false;          // headless has no native window to shape
            FirstShowService.LockdownActive = () => false;
            FirstShowService.StrictLock = () => false;

            // The doors refuse over a session, Lockdown and Strict Lock.
            FirstShowService.SessionRunning = () => true;
            Assert.False(FirstShowService.Open());
            FirstShowService.SessionRunning = () => false;
            FirstShowService.LockdownActive = () => true;
            Assert.False(FirstShowService.Open());
            FirstShowService.LockdownActive = () => false;
            FirstShowService.StrictLock = () => true;
            Assert.False(FirstShowService.Open());
            FirstShowService.StrictLock = () => false;
            Assert.False(FirstShowService.IsActive);

            string before = System.Text.Json.JsonSerializer.Serialize(new
            {
                CoreSettings.Current.MediaSource, CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.PanicKey,
                CoreSettings.Current.MasterVolume, CoreSettings.Current.FlashEnabled, CoreSettings.Current.SpiralEnabled,
                CoreSettings.Current.PinkFilterEnabled, CoreSettings.Current.SubliminalEnabled, CoreSettings.Current.BubblesEnabled
            });

            Assert.True(FirstShowService.Open());
            Dispatcher.UIThread.RunJobs();
            var show = FirstShowService.Window!;
            var desk = svc.Window!;
            Assert.True(desk.PresentationActive);      // the REAL widget, in presentation mode
            Assert.True(desk.IsVisible);
            double t = 0;
            show.Clock = () => t;
            show.BundledPictures = () => Pictures(dir, 4);
            show.FetchFeed = (_, _, _) => Task.FromResult<IReadOnlyList<ConditioningControlPanel.Services.Fyp.FypAssetManifest.Entry>>(
                Array.Empty<ConditioningControlPanel.Services.Fyp.FypAssetManifest.Entry>());

            void Step(double to, double by = .25) { for (; t < to; t += by) { show.Frame(); Dispatcher.UIThread.RunJobs(); } }
            void Click(string key)
            {
                var label = FirstShowStageWindow.Text(key);
                var button = show.ChoiceButtons.First(b => (b.Content as string) == label);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }
            async Task Until(Func<bool> done)
            {
                for (int i = 0; i < 400 && !done(); i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
                Assert.True(done());
            }
            bool Has(string key) => show.ChoiceButtons.Any(b => (b.Content as string) == FirstShowStageWindow.Text(key));

            // Greeting.
            Step(1.5);
            Assert.Equal(FirstShowStageWindow.Text("greeting"), show.SpeechText);
            Assert.True(Has("yes")); Assert.True(Has("later"));

            // Choose: six presets and a way out.
            Click("yes");
            Assert.Equal(FirstShowPresets.All.Length + 1, show.ChoiceButtons.Count);

            // Load: a dry feed lands on the error card with the bundled fallback.
            show.ChoiceButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => show.SpeechText == FirstShowStageWindow.Text("error"));
            Assert.True(Has("bundled"));

            // Bundled fallback, then Confirm with the panic key named.
            Click("bundled");
            await Until(() => Has("ready"));
            Assert.Contains("Esc", show.SpeechText);
            Assert.False(show.Playing);

            // Start: the beats, the effects, the logo's IN, then the verdict.
            Click("ready");
            Assert.True(show.Playing);
            double start = t;
            Step(start + 22);
            Assert.True(show.Effects!.ActorCount > 0);
            Step(start + 34);
            Assert.True(show.LogoOpacity > 0);
            Step(start + 36);
            Assert.False(show.Playing); Assert.True(show.Ending);
            Assert.Equal(FirstShowStageWindow.Text("verdict"), show.SpeechText);
            Assert.True(Has("liked")); Assert.True(Has("showoff"));

            // Nothing the demo did touched a setting.
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(new
            {
                CoreSettings.Current.MediaSource, CoreSettings.Current.EmiDeskEnabled, CoreSettings.Current.PanicKey,
                CoreSettings.Current.MasterVolume, CoreSettings.Current.FlashEnabled, CoreSettings.Current.SpiralEnabled,
                CoreSettings.Current.PinkFilterEnabled, CoreSettings.Current.SubliminalEnabled, CoreSettings.Current.BubblesEnabled
            }));

            // Panic: the runtime surface is first in the list and stops the show at once.
            FirstShowService.HookPanic();
            FirstShowService.HookPanic();
            Assert.Equal(FirstShowService.PanicSurfaceId, PanicSurfaces.All[0].Id);
            Assert.Single(PanicSurfaces.All, s => s.Id == FirstShowService.PanicSurfaceId);
            PanicSurfaces.All.First(s => s.Id == FirstShowService.PanicSurfaceId).Stop(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(FirstShowService.IsActive);
            Assert.False(show.IsVisible);
            Assert.False(show.SpeechWindow.IsVisible);
            Assert.False(desk.PresentationActive);     // she is handed back
            Assert.Null(show.Effects);
        }
        finally
        {
            FirstShowService.Stop();
            Dispatcher.UIThread.RunJobs();
            try { svc.Window?.ShutDown(); } catch { }
            Dispatcher.UIThread.RunJobs();
            PanicSurfaces.All = oldPanic;
            (FirstShowService.SessionRunning, FirstShowService.LockdownActive, FirstShowService.StrictLock, FirstShowService.RequireClickThrough) =
                (oldSession, oldLock, oldStrict, oldRequire);
            CoreSettings.ServiceProvider = oldSettings;
            try { Directory.Delete(dir, true); } catch { }
        }
    });
}
