using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MantraWindow.xaml.cs over the Core MantraService: a typed line counts a rep (XP,
/// memory favourite), the streak tone and completion tone play, the drone follows the gain ramp,
/// the completion overlay shows WPF's stats and closing leaves the service's events.</summary>
public sealed class MantraWindowSessionTests
{
    private sealed class FakeDrone : LayeredAudio.ILayerPlayer
    {
        public readonly List<int> Volumes = new();
        public bool Disposed;
        public int Volume { set => Volumes.Add(value); }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public Task TypedMantraCompletesTheSessionWithTonesDroneAndMemorySignal() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var dir = Directory.CreateTempSubdirectory("ccp-mantra-test-").FullName;
        var (play, opener, xp0) = (CoreAudio.PlayOneShotProvider, MantraWindow.DroneOpener, CoreProgression.AddXPProvider);
        var (factory, sources, sent) = (MemoryStore.SignalMirrorFactory, MemorySignalWriter.SourcesHook, CompanionBrain.UserMessageSent);
        var tones = new List<string>();
        var xp = new List<double>();
        var drone = new FakeDrone();
        CoreAudio.PlayOneShotProvider = (p, v, tag, _, done) => { tones.Add($"{Path.GetFileName(p)} {v.ToString(System.Globalization.CultureInfo.InvariantCulture)} {tag}"); done?.Invoke(); };
        MantraWindow.DroneOpener = p => { Assert.True(File.Exists(p)); return drone; };
        CoreProgression.AddXPProvider = (a, _) => xp.Add(a);
        MantraWindow? win = null;
        try
        {
            AvApp.SeedMemorySignals();
            using var store = new MemoryStore(Path.Combine(dir, "memory.json"));
            using var writer = MemoryStore.SignalMirrorFactory!(store)!;

            AvApp.Mantra.StartSession(1);
            var line = AvApp.Mantra.CurrentMantra!;
            win = new MantraWindow();
            win.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("/1", win.FindControl<TextBlock>("TxtTarget")!.Text);
            Assert.Equal(41, Assert.Single(drone.Volumes));   // WPF StartDrone: raw 0.05, MantraDroneVolume not applied until the ramp
            var box = win.FindControl<TextBox>("TxtInput")!;
            box.Focus();

            Thread.Sleep(1600);   // MantraService's 1.5 s anti-cheat floor
            win.KeyTextInput(line.ToUpperInvariant());   // case-insensitive, as WPF
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { 35.0 }, xp);
            Assert.Equal(1, store.FeatureUsage[MemorySignalWriter.FeatureMantra]);
            Assert.True(win.FindControl<Border>("CompletionOverlay")!.IsVisible);
            Assert.Equal("1 repetitions  |  Best streak: 1", win.FindControl<TextBlock>("TxtCompletionStats")!.Text);
            Assert.Equal(new[] { "tone-420-150.wav 0.15 mantra", "tone-523-400.wav 0.15 mantra" }, tones);

            var ramp = typeof(MantraWindow).GetMethod("FloatTimer_Tick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            for (var i = 0; i < 200; i++) ramp.Invoke(win, new object?[] { null, EventArgs.Empty });
            // First ramp step: (0.05 + (0.0733 - 0.05) x 0.02) x 30% drone volume -> 28 (cubic), then up with the streak.
            Assert.Equal(28, drone.Volumes[1]);
            Assert.True(drone.Volumes[^1] > 28, "the drone did not ramp up with the streak");

            win.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "");
            Dispatcher.UIThread.RunJobs();
            Assert.True(drone.Disposed);
            Assert.False(AvApp.Mantra.IsActive);
            Assert.Null(typeof(ConditioningControlPanel.Services.MantraService)
                .GetField("SessionComplete", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(AvApp.Mantra));
        }
        finally
        {
            win?.Close();
            (CoreAudio.PlayOneShotProvider, MantraWindow.DroneOpener, CoreProgression.AddXPProvider) = (play, opener, xp0);
            (MemoryStore.SignalMirrorFactory, MemorySignalWriter.SourcesHook, CompanionBrain.UserMessageSent) = (factory, sources, sent);
            AvApp.StopMantra(Array.Empty<Window>());   // deletes ToneWav's temp folder
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Ms(double ms) => Now += (long)(ms * TimeSpan.TicksPerMillisecond);
    }

    /// <summary>The five WPF Storyboards (MantraWindow.xaml:14-58) on a stepped clock: the glow breathes
    /// 0 -> 0.3 -> 0 over 4 s, a wrong letter shakes 4 px at 30 ms, a right one pulses 1.02 at 80 ms,
    /// a completed line pulses 1.06 at 150 ms and a broken streak shakes 8 px at 50 ms; all settle.</summary>
    [Fact]
    public Task StoryboardsStepOnTheClock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var (opener, clock0, play) = (MantraWindow.DroneOpener, MantraWindow.Clock, CoreAudio.PlayOneShotProvider);
        var clock = new SteppedClock { Now = 1_000_000 };
        MantraWindow.DroneOpener = _ => new FakeDrone();
        MantraWindow.Clock = clock;
        CoreAudio.PlayOneShotProvider = (_, _, _, _, done) => done?.Invoke();
        MantraWindow? win = null;
        try
        {
            AvApp.Mantra.StartSession(3);
            var line = AvApp.Mantra.CurrentMantra!;
            win = new MantraWindow();
            win.Show();
            Dispatcher.UIThread.RunJobs();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            void Tick(double ms) { clock.Ms(ms); typeof(MantraWindow).GetMethod("FloatTimer_Tick", flags)!.Invoke(win, new object?[] { null, EventArgs.Empty }); }
            void Call(string m) => typeof(MantraWindow).GetMethod(m, flags)!.Invoke(win, null);
            var mantra = win.FindControl<TextBlock>("TxtMantra")!;
            var group = (global::Avalonia.Media.TransformGroup)mantra.RenderTransform!;
            var scale = (global::Avalonia.Media.ScaleTransform)group.Children[0];
            var move = (global::Avalonia.Media.TranslateTransform)group.Children[1];
            var glow = win.FindControl<Border>("GlowOverlay")!;

            Tick(1000);
            Assert.Equal(0.15, glow.Opacity, 3);
            Tick(2000);
            Assert.Equal(0.15, glow.Opacity, 3);   // reversing

            var box = win.FindControl<TextBox>("TxtInput")!;
            box.Text = line[0] == '#' ? "%" : "#";   // WrongShakeStoryboard
            Dispatcher.UIThread.RunJobs();
            Tick(30);
            Assert.Equal(4, move.X, 3);
            Tick(200);
            Assert.Equal(0, move.X, 3);

            box.Text = "";
            Dispatcher.UIThread.RunJobs();
            box.Text = line[..1];                  // LetterPulseStoryboard
            Dispatcher.UIThread.RunJobs();
            Tick(80);
            Assert.Equal(1.02, scale.ScaleX, 3);
            Tick(100);
            Assert.Equal(1, scale.ScaleY, 3);

            Call("OnMantraCompleted");             // PulseStoryboard
            Tick(150);
            Assert.Equal(1.06, scale.ScaleY, 3);
            Tick(200);
            Assert.Equal(1, scale.ScaleX, 3);

            Call("OnStreakBroken");                // ShakeStoryboard
            Tick(50);
            Assert.Equal(8, move.X, 3);
            Tick(25);
            Assert.Equal(0, move.X, 3);
            Tick(300);
            Assert.Equal(0, move.X, 3);
        }
        finally
        {
            win?.Close();
            (MantraWindow.DroneOpener, MantraWindow.Clock, CoreAudio.PlayOneShotProvider) = (opener, clock0, play);
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF #1230: a Mantra task's "open" button on the Programs tab opens the typed game at the
    /// task's rep count (BtnProgramOpenMantras_Click -> StartMantraSession); a second press re-uses it.</summary>
    [Fact]
    public Task ProgramTaskDoorOpensTheMantraLabAtTheTaskReps() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var opener = MantraWindow.DroneOpener;
        MantraWindow.DroneOpener = _ => new FakeDrone();
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.ProgramsTabView>("ProgramsTab")!;

            tab.BtnProgramOpenMantras_Click(new Button { Tag = 4 }, new global::Avalonia.Interactivity.RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();

            var lab = Assert.Single(shell.OwnedWindows.OfType<MantraWindow>());
            Assert.True(lab.IsVisible);
            Assert.True(AvApp.Mantra.IsActive);
            Assert.Equal(4, AvApp.Mantra.TargetCount);
            Assert.Equal("/4", lab.FindControl<TextBlock>("TxtTarget")!.Text);
            lab.Close();
            Assert.False(AvApp.Mantra.IsActive);
        }
        finally
        {
            foreach (var w in shell?.OwnedWindows.ToArray() ?? Array.Empty<Window>()) w.Close();
            shell?.Close();
            MantraWindow.DroneOpener = opener;
        }
        return Task.CompletedTask;
    });

    /// <summary>Panic (PanicSurfaces "mantra", WPF KillAllAudio -> Mantra?.Dispose()): the session ends,
    /// the open window goes inert and its drone stops.</summary>
    [Fact]
    public Task PanicStopsTheDroneAndEndsTheSession() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var (opener, all) = (MantraWindow.DroneOpener, PanicSurfaces.All);
        var drone = new FakeDrone();
        MantraWindow.DroneOpener = _ => drone;
        MantraWindow? win = null;
        try
        {
            PanicSurfaces.All = PanicSurfaces.All.Where(s => s.Id == "mantra").ToArray();
            AvApp.Mantra.StartSession(3);
            win = new MantraWindow();
            win.Show();
            Dispatcher.UIThread.RunJobs();

            PanicSurfaces.StopAll("test");

            Assert.True(drone.Disposed);
            Assert.False(AvApp.Mantra.IsActive);
        }
        finally
        {
            win?.Close();
            (MantraWindow.DroneOpener, PanicSurfaces.All) = (opener, all);
        }
        return Task.CompletedTask;
    });

    /// <summary>Tray Exit / any shutdown runs App.StopMantra from OnDesktopExit: the Mantra Lab closes, its
    /// drone is disposed, the session ends and the synthesised WAVs are deleted.</summary>
    [Fact]
    public Task ExitClosesTheWindowStopsTheDroneAndDeletesTheWavs() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var opener = MantraWindow.DroneOpener;
        var drone = new FakeDrone();
        string? wav = null;
        MantraWindow.DroneOpener = p => { wav = p; return drone; };
        try
        {
            AvApp.Mantra.StartSession(3);
            var win = new MantraWindow();
            win.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(File.Exists(wav));

            AvApp.StopMantra(new Window[] { win });
            Dispatcher.UIThread.RunJobs();

            Assert.False(win.IsVisible);
            Assert.True(drone.Disposed);
            Assert.False(AvApp.Mantra.IsActive);
            Assert.False(Directory.Exists(Path.GetDirectoryName(wav)));
        }
        finally { MantraWindow.DroneOpener = opener; }
        return Task.CompletedTask;
    });
}
