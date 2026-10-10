using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HB1 / HB2 / HB5 / HB6: the player binds Core's rule engine to its own clock, plays audio
/// on the transport, stops both on panic and close; the editor's Preview opens the shared player; the
/// eye tracking button asks before it opens a camera. Swaps process-wide seams, so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class DeeperPlayerEngineTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Invoke(object o, string name, params object?[] args) =>
        o.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(o, args);

    private static void Tick(EnhancementPlayerWindow w) => Invoke(w, "UiTimer_Tick", null, EventArgs.Empty);

    private sealed class FakeAudio : IDeeperLocalAudio
    {
        public bool Playing, Disposed;
        public double Level = -1;
        public double DurationSeconds { get; set; } = 95;
        public double PositionSeconds { get; set; }
        public double Volume { set => Level = value; }
        public void Play() => Playing = true;
        public void Pause() => Playing = false;
        public event Action? Ended;
        public void End() { Playing = false; Ended?.Invoke(); }
        public void Dispose() => Disposed = true;
    }

    private sealed class Sink : IActionDispatcher
    {
        public List<EnhancementAction> Fired = new();
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        { Fired.Add(action); return Task.CompletedTask; }
    }

    private static Enhancement Shake(double at) => new()
    {
        MediaType = MediaTypes.Audio,
        MediaSource = "clip.mp3",
        Rules = { new EnhancementRule { Trigger = new TimeReachedTrigger { Time = at }, Action = new ScreenShakeAction(), Enabled = true } },
    };

    [Fact]
    public Task AudioPlaysTheRulesFireAndPanicAndCloseStopEverything() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-player-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        var fake = new FakeAudio();
        var sink = new Sink();
        var open = DeeperLocalAudio.Open;
        var factory = EnhancementHostService.DispatcherFactory;
        DeeperLocalAudio.Open = p => Task.FromResult<IDeeperLocalAudio?>(p == path ? fake : null);
        EnhancementHostService.DispatcherFactory = () => sink;
        var player = new EnhancementPlayerWindow(null, null);
        try
        {
            player.Show();
            player.OpenLocalMediaFile(path);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(fake, player.Audio);
            Assert.True(fake.Playing);                       // WPF: the file plays once it is open
            Assert.Equal("⏸", player.FindControl<TextBlock>("TxtPlayPauseGlyph")!.Text);

            player.LoadEnhancementFromMemory(Shake(1), "test");
            fake.PositionSeconds = 0.5;
            Tick(player);
            Assert.True(player.Host.IsRunning);              // bound to the window's clock
            Assert.Empty(sink.Fired);
            fake.PositionSeconds = 1.2;
            Tick(player);
            Assert.IsType<ScreenShakeAction>(Assert.Single(sink.Fired));

            player.FindControl<Slider>("SliderVolume")!.Value = 50;
            Assert.Equal(0.5, fake.Level, 3);

            Invoke(player, "AudioSeek", 30.0);               // what a timeline or scrub click calls
            Assert.Equal(30, fake.PositionSeconds);

            PanicSurfaces.All.Single(s => s.Id == "deeper-player").Stop(null);
            Assert.False(player.Host.IsRunning);
            Assert.False(fake.Playing);
            Tick(player);
            Assert.False(player.Host.IsRunning);              // a panic is not undone by the next tick

            Invoke(player, "BtnPlayPause_Click");
            Assert.True(fake.Playing);
            Tick(player);
            Assert.True(player.Host.IsRunning);
        }
        finally
        {
            player.Close();
            DeeperLocalAudio.Open = open;
            EnhancementHostService.DispatcherFactory = factory;
            Directory.Delete(dir, true);
        }
        Assert.False(player.Host.IsRunning);
        Assert.True(fake.Disposed);
        Assert.DoesNotContain(player, EnhancementPlayerWindow.Open);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditorPreviewOpensTheSharedPlayerWithItsEnhancement() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        foreach (var w in EnhancementPlayerWindow.Open.ToList()) w.Close();
        var enh = Shake(3);
        enh.Metadata.Name = "preview me";
        var editor = new DeeperEditorWindow(enh, null);
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            Invoke(editor, "BtnPreview_Click", null, new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            var player = Assert.Single(EnhancementPlayerWindow.Open);
            Assert.Equal("preview me", player.FindControl<TextBlock>("TxtEnhName")!.Text);

            Invoke(editor, "BtnPreview_Click", null, new RoutedEventArgs());   // reused, never a second window
            Assert.Same(player, Assert.Single(EnhancementPlayerWindow.Open));
            player.Close();
        }
        finally { editor.Close(); }
        Assert.Empty(EnhancementPlayerWindow.Open);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EyeTrackingAsksFirstAndNeverOpensACameraWithoutConsent() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var (isRunning, start, stop, consent, confirm, notice) = (EnhancementPlayerWindow.EyeIsRunning, EnhancementPlayerWindow.EyeStart,
            EnhancementPlayerWindow.EyeStop, EnhancementPlayerWindow.EyeConsentCurrent, EnhancementPlayerWindow.EyeConfirm, EnhancementPlayerWindow.EyeNotice);
        bool running = false, consented = false, answer = false;
        int starts = 0, stops = 0, notices = 0, asks = 0;
        EnhancementPlayerWindow.EyeIsRunning = () => running;
        EnhancementPlayerWindow.EyeStart = () => { starts++; running = true; return Task.FromResult(true); };
        EnhancementPlayerWindow.EyeStop = () => { stops++; running = false; return Task.CompletedTask; };
        EnhancementPlayerWindow.EyeConsentCurrent = () => consented;
        EnhancementPlayerWindow.EyeConfirm = (_, _, _) => { asks++; return Task.FromResult(answer); };
        EnhancementPlayerWindow.EyeNotice = (_, _, _) => { notices++; return Task.CompletedTask; };
        var player = new EnhancementPlayerWindow(null, null);
        try
        {
            player.Show();
            var label = player.FindControl<TextBlock>("TxtEyeTracking")!;

            await player.ToggleEyeTrackingAsync();           // no consent on file: point at setup
            Assert.Equal((1, 0, 0), (notices, asks, starts));

            consented = true;
            await player.ToggleEyeTrackingAsync();           // asked, answered no
            Assert.Equal((1, 1, 0), (notices, asks, starts));

            answer = true;
            await player.ToggleEyeTrackingAsync();
            Assert.Equal(1, starts);
            Assert.Equal(Loc.Get("deeper_player_btn_eye_tracking_stop"), label.Text);

            await player.ToggleEyeTrackingAsync();           // running: one press stops, no question
            Assert.Equal((1, 2), (stops, asks));
            Assert.Equal(Loc.Get("deeper_player_btn_eye_tracking_start"), label.Text);
        }
        finally
        {
            player.Close();
            (EnhancementPlayerWindow.EyeIsRunning, EnhancementPlayerWindow.EyeStart, EnhancementPlayerWindow.EyeStop,
                EnhancementPlayerWindow.EyeConsentCurrent, EnhancementPlayerWindow.EyeConfirm, EnhancementPlayerWindow.EyeNotice)
                = (isRunning, start, stop, consent, confirm, notice);
        }
    });

    [Fact]
    public void ImportCopiesIntoTheLibraryOnceAndSkipsWhatIsNotAnEnhancement()
    {
        var root = Directory.CreateTempSubdirectory("ccp-deeper-import-").FullName;
        var lib = Path.Combine(root, "library");
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        var enh = Shake(2);
        enh.Metadata.Name = "Night Drift";
        var good = Path.Combine(src, "night.ccpenh.json");
        // Written as the editor writes it (a load + save round trip), so the library copy is byte-for-meaning the same file.
        File.WriteAllText(good, EnhancementSerializer.Save(enh));
        File.WriteAllText(good, EnhancementSerializer.Save(EnhancementSerializer.LoadFromFile(good)));
        var other = Path.Combine(src, "settings.json");
        File.WriteAllText(other, "{\"theme\":\"dark\"}");
        var (notify, remember) = (DeeperImport.Notify, DeeperImport.RememberDirectory);
        var toasts = new List<(string Text, DeeperImport.Tone Tone)>();
        string? remembered = null;
        DeeperImport.Notify = (text, tone) => toasts.Add((text, tone));
        DeeperImport.RememberDirectory = d => remembered = d;
        try
        {
            var saved = DeeperImport.ImportFiles(new[] { good, other }, lib);
            Assert.Equal(Path.Combine(lib, "Night Drift.ccpenh.json"), saved);
            Assert.True(File.Exists(saved));
            Assert.Equal(src, remembered);
            Assert.Equal(DeeperImport.Tone.Success, Assert.Single(toasts).Tone);
            Assert.Equal(Loc.GetF("deeper_import_done_one_fmt", "Night Drift.ccpenh.json"), toasts[0].Text);

            toasts.Clear();
            Assert.Equal(saved, DeeperImport.ImportFiles(new[] { good }, lib));   // same content: no "(2)" copy
            Assert.Single(Directory.GetFiles(lib));
            Assert.Equal(Loc.Get("deeper_import_already_in_library"), Assert.Single(toasts).Text);

            toasts.Clear();
            Assert.Null(DeeperImport.ImportFiles(new[] { other }, lib));
            Assert.Equal(Loc.Get("deeper_import_skipped_not_enh"), Assert.Single(toasts).Text);
        }
        finally
        {
            (DeeperImport.Notify, DeeperImport.RememberDirectory) = (notify, remember);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public Task APinkBandHoldsTheTintWithoutTheUsersSwitchAndStopHandsItBack() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var (host, refresh) = (RealActionDispatcher.HostProvider, RealActionDispatcher.PinkRefresh);
        var window = new Window();
        RealActionDispatcher.HostProvider = () => window;
        RealActionDispatcher.PinkRefresh = _ => { };
        var ctx = new EnhancementDispatchContext(new Enhancement(), new NoSource(), 0, null);
        TriggerEffectAction Band(EffectPhase phase, double opacity, string kind = OverlayKinds.PinkFilter) => new()
        { EffectType = EffectTypes.Overlay, OverlayKind = kind, Opacity = opacity, Phase = phase, EffectId = "item:a", DurationMs = 4000 };
        try
        {
            var d = new RealActionDispatcher();
            Assert.Null(PinkFilterOverlay.BandHold);
            await d.DispatchAsync(Band(EffectPhase.Start, 0.4), ctx);
            Assert.Equal(0.4, PinkFilterOverlay.BandHold);
            await d.DispatchAsync(Band(EffectPhase.Update, 0.6), ctx);      // the opacity ramp
            Assert.Equal(0.6, PinkFilterOverlay.BandHold);
            await d.DispatchAsync(Band(EffectPhase.Stop, 0.6), ctx);
            Assert.Null(PinkFilterOverlay.BandHold);

            await d.DispatchAsync(Band(EffectPhase.Start, 0.3), ctx);
            d.ResetOverlayBands();                                          // engine Stop's safety belt
            Assert.Null(PinkFilterOverlay.BandHold);

            // A spiral band and a screen shake have surfaces on this head now (k12): neither is logged as missing.
            var (shakeDoor, shakes) = (RealActionDispatcher.ShakeDoor, new System.Collections.Generic.List<(double, int)>());
            RealActionDispatcher.ShakeDoor = (i, ms) => shakes.Add((i, ms));
            var (spiralHold, spiralRelease) = (RealActionDispatcher.SpiralHold, RealActionDispatcher.SpiralRelease);
            (RealActionDispatcher.SpiralHold, RealActionDispatcher.SpiralRelease) = ((_, _) => { }, _ => { });
            try
            {
                await d.DispatchAsync(Band(EffectPhase.Start, 0.3, OverlayKinds.Spiral), ctx);
                await d.DispatchAsync(new ScreenShakeAction { Intensity = 0.4, DurationMs = 300 }, ctx);
                Assert.Empty(d.NoTwin);
                Assert.Equal(new[] { (0.4, 300) }, shakes);
                await d.DispatchAsync(Band(EffectPhase.Stop, 0.3, OverlayKinds.Spiral), ctx);
            }
            finally
            {
                RealActionDispatcher.ShakeDoor = shakeDoor;
                (RealActionDispatcher.SpiralHold, RealActionDispatcher.SpiralRelease) = (spiralHold, spiralRelease);
            }
            Assert.Null(PinkFilterOverlay.BandHold);
        }
        finally
        {
            PinkFilterOverlay.BandHold = null;
            (RealActionDispatcher.HostProvider, RealActionDispatcher.PinkRefresh) = (host, refresh);
        }
    });

    private sealed class NoSource : IPlaybackTimeSource
    {
        public event Action<double>? PlaybackTimeChanged { add { } remove { } }
        public double GetCurrentTimeSeconds() => 0;
        public double GetDurationSeconds() => 0;
        public bool IsPlaying => false;
        public void Seek(double seconds) { }
        public void Pause() { }
        public void Play() { }
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
    }
}
