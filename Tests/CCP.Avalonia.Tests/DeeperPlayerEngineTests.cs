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
}
