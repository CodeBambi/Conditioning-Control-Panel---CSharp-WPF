using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// main-sync #6 bd95424c8 / 2889ddb23 (WPF polish wave 13): the smoke left the tube art and the app
/// draws its own motes inside the glass. Mirrors WPF TubeMotesTests: where the motes sit (attached
/// and detached chamber), that they run while the tube is not the active window, and that the
/// canvas never takes a click.
/// </summary>
public sealed class TubeMotesTests
{
    [Fact]
    public void TheMotesBoxesMatchWpf()
    {
        var a = AvatarTubeWindow.TubeMotesBox(detached: false);
        Assert.Equal(273.1, a.X, 1);
        Assert.Equal(552.6, a.Y, 1);
        Assert.Equal(125.7, a.Width, 1);
        Assert.Equal(269.3, a.Height, 1);
        var d = AvatarTubeWindow.TubeMotesBox(detached: true);
        Assert.Equal(121.9, d.X, 1);
        Assert.Equal(540.4, d.Y, 1);
    }

    [Fact]
    public Task TheTubeStartsEmbersInItsChamberAndFollowsDetach() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarTubeDetached = false;
        var main = new Window { Width = 1000, Height = 700, Position = new PixelPoint(2000, 200) };
        AvatarTubeWindow? tube = null;
        try
        {
            main.Show();
            tube = new AvatarTubeWindow(main);
            tube.Show();
            Dispatcher.UIThread.RunJobs();

            var motes = tube.FindControl<AmbientFxCanvas>("TubeMotes")!;
            Assert.True(motes.IsVisible);
            Assert.False(motes.IsHitTestVisible);
            Assert.Equal(AmbientFxLayers.Embers, motes.Layers);
            var a = AvatarTubeWindow.TubeMotesBox(false);
            Assert.Equal(a.X, motes.Margin.Left, 3);
            Assert.Equal(a.Width, motes.Width, 3);

            // The tube is almost never the active window: the motes keep rising anyway.
            main.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.False(tube.IsActive);
            Assert.True(AmbientFxCanvas.Env.AllowAmbientLoops);   // default settings: Motion Full
            Assert.True(motes.IsTicking);

            // Embers rise from the floor of the chamber, a quarter second apart.
            var step = typeof(AmbientFxCanvas).GetMethod("StepEmbers", BindingFlags.NonPublic | BindingFlags.Instance)!;
            for (int i = 0; i < 8; i++) step.Invoke(motes, new object[] { 0.3f });
            Assert.True(motes.EmberCount > 0);

            tube.FindControl<MenuItem>("MenuItemDetach")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var d = AvatarTubeWindow.TubeMotesBox(true);
            Assert.Equal(d.X, motes.Margin.Left, 3);
            Assert.Equal(d.Y, motes.Margin.Top, 3);
        }
        finally
        {
            tube?.Close();
            main.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
