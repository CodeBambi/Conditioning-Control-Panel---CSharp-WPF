using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>User report: the tube never moved on Detach and never sat on main's left edge.</summary>
public sealed class AvatarTubeWindowingTests
{
    /// <summary>WPF's left dock: tube's right art edge (353*s in, less 3 px daylight) on main's left edge.</summary>
    private static PixelPoint Docked(Window main, AvatarTubeWindow tube)
    {
        double s = tube.Width / 780;
        int w = (int)Math.Round(tube.Width), h = (int)Math.Round(tube.Height);
        int mainH = (int)Math.Round(main.ClientSize.Height);
        return new PixelPoint(main.Position.X - w + (int)Math.Round(353 * s - 3),
                              (int)Math.Round(main.Position.Y + (mainH - h) / 2.0) + (int)Math.Round(20 * s));
    }

    [Fact]
    public Task AttachedDocksLeftFollowsAndToggles() => Run(detached: false, (main, tube) =>
    {
        Assert.False(tube.Topmost);
        Assert.Equal(Docked(main, tube), tube.Position);

        main.Position = new PixelPoint(2100, 300);   // follow a shell move
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Docked(main, tube), tube.Position);

        tube.FindControl<MenuItem>("MenuItemDetach")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(tube.IsDetached);
        Assert.True(tube.Topmost);
        Assert.True(CoreSettings.Current.AvatarTubeDetached);
        Assert.True(tube.FindControl<MenuItem>("MenuItemAttach")!.IsVisible);

        tube.Position = new PixelPoint(50, 60);      // what the WM drag ends in
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, CoreSettings.Current.AvatarTubeLeft, 3);
        Assert.Equal(60, CoreSettings.Current.AvatarTubeTop, 3);

        new CompanionHeroCardViewModel().DetachCommand.Execute(null);   // hero chip re-attaches
        Assert.False(tube.IsDetached);
        Assert.False(tube.Topmost);
        Assert.False(CoreSettings.Current.AvatarTubeDetached);
        Assert.Equal(Docked(main, tube), tube.Position);
    });

    [Fact]
    public Task DetachedRestoresSavedPlacement() => Run(detached: true, (main, tube) =>
    {
        Assert.True(tube.IsDetached);
        Assert.True(tube.Topmost);
        Assert.Equal(new PixelPoint(123, 45), tube.Position);
    });

    private static Task Run(bool detached, Action<Window, AvatarTubeWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarTubeDetached = detached;
        CoreSettings.Current.AvatarTubeLeft = 123;
        CoreSettings.Current.AvatarTubeTop = 45;
        var main = new Window { Width = 1000, Height = 700, Position = new PixelPoint(2000, 200) };
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
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
