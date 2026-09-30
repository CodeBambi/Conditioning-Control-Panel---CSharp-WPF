using System;
using System.Collections.Generic;
using System.IO;
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
