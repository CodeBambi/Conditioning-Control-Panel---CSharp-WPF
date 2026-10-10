using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Hunt IB5 (HB17): the Deeper player's and editor's page zoom (buttons and Ctrl+wheel) and page
/// fullscreen, through the script channel and the page -> host message the port's web host has.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class DeeperPageBridgeTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Invoke(object o, string name, params object?[] args) =>
        o.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(o, args);

    [Fact]
    public void Zoom_StepsTenPercent_ClampedLikeWpf()
    {
        Assert.Equal(1.1, DeeperPageBridge.Next(1.0, +0.10), 6);
        Assert.Equal(0.9, DeeperPageBridge.Next(1.0, -0.10), 6);
        Assert.Equal(5.0, DeeperPageBridge.Next(5.0, +0.10), 6);
        Assert.Equal(0.25, DeeperPageBridge.Next(0.3, -0.10), 6);
        double z = 1;
        for (int i = 0; i < 7; i++) z = DeeperPageBridge.Next(z, +0.10);
        Assert.Equal(1.7, z, 9);                                       // no float drift over many presses
        Assert.Contains("style.zoom='1.7'", DeeperPageBridge.Script(1.7));
        Assert.Contains("style.zoom='1'", DeeperPageBridge.Script(1));
        Assert.Contains("invokeCSharpAction", DeeperPageBridge.Script(1));
    }

    [Fact]
    public void Bridge_ZoomsByButtonAndWheel_ReappliesAfterNavigation_AndFollowsPageFullscreen() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var host = new WebHost();
        var window = new Window { Content = host, Width = 400, Height = 300 };
        var scripts = new List<string>();
        var states = new List<bool>();
        var bridge = new DeeperPageBridge(host, window) { Invoke = s => { scripts.Add(s); return Task.FromResult<string?>("ok"); } };
        bridge.FullscreenChanged += states.Add;
        try
        {
            window.Show();
            bridge.Adjust(+0.10);
            Assert.Equal(1.1, bridge.Zoom, 6);
            Assert.Contains("style.zoom='1.1'", scripts[^1]);

            host.OnWebMessage(DeeperPageBridge.ZoomIn);                // Ctrl+wheel in the page
            host.OnWebMessage("\"" + DeeperPageBridge.ZoomIn + "\"");  // the engine may JSON-quote the string
            Assert.Equal(1.3, bridge.Zoom, 6);
            host.OnWebMessage(DeeperPageBridge.ZoomOut);
            Assert.Equal(1.2, bridge.Zoom, 6);

            int before = scripts.Count;
            host.OnNavigationCompleted(new Uri("https://example.com/watch"));
            Assert.Equal(before + 1, scripts.Count);                   // a new document gets the zoom and listeners back
            Assert.Contains("style.zoom='1.2'", scripts[^1]);

            window.WindowState = WindowState.Maximized;
            host.OnWebMessage(DeeperPageBridge.FullscreenOn);
            Assert.True(bridge.PageFullscreen);
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            host.OnWebMessage(DeeperPageBridge.FullscreenOn);          // said twice: one change
            host.OnWebMessage(DeeperPageBridge.FullscreenOff);
            Assert.False(bridge.PageFullscreen);
            Assert.Equal(WindowState.Maximized, window.WindowState);   // back to what it was
            Assert.Equal(new[] { true, false }, states);

            host.OnWebMessage(DeeperPageBridge.FullscreenOn);
            host.OnNavigationCompleted(new Uri("https://example.com/next"));   // the fullscreen document is gone
            Assert.False(bridge.PageFullscreen);
            Assert.Equal(WindowState.Maximized, window.WindowState);

            host.OnWebMessage(DeeperPageBridge.FullscreenOn);
            bridge.LeaveFullscreen();
            Assert.Equal(DeeperPageBridge.ExitFullscreenScript, scripts[^1]);
            Assert.Equal(WindowState.Maximized, window.WindowState);

            host.OnWebMessage("something else");                       // an unknown message changes nothing
            Assert.Equal(1.2, bridge.Zoom, 6);

            host.OnWebMessage(DeeperPageBridge.FullscreenOn);
            bridge.Dispose();                                          // released: out of fullscreen, deaf to the page
            Assert.Equal(WindowState.Maximized, window.WindowState);
            host.OnWebMessage(DeeperPageBridge.ZoomIn);
            host.OnWebMessage(DeeperPageBridge.FullscreenOn);
            Assert.Equal(1.2, bridge.Zoom, 6);
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }
        finally { bridge.Dispose(); window.Close(); }
    });

    [Fact]
    public void Player_ShowsTheZoomButtonsInVideoMode_AndTheyZoom() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var player = new EnhancementPlayerWindow(null, null);
        try
        {
            player.Show();
            var cluster = player.FindControl<StackPanel>("BrowserZoomCluster")!;
            Invoke(player, "ShowMediaPaneFor", MediaTypes.Audio);
            Assert.False(cluster.IsVisible);
            Invoke(player, "ShowMediaPaneFor", MediaTypes.Video);
            Assert.True(cluster.IsVisible);

            player.FindControl<Button>("BtnZoomIn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1.1, player.PageBridge.Zoom, 6);
            player.FindControl<Button>("BtnZoomOut")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            player.FindControl<Button>("BtnZoomOut")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0.9, player.PageBridge.Zoom, 6);

            player.FindControl<WebHost>("VideoBrowser")!.OnWebMessage(DeeperPageBridge.FullscreenOn);
            Assert.Equal(WindowState.FullScreen, player.WindowState);
            player.FindControl<WebHost>("VideoBrowser")!.OnWebMessage(DeeperPageBridge.FullscreenOff);
            Assert.NotEqual(WindowState.FullScreen, player.WindowState);
        }
        finally { player.Close(); }
    });

    [Fact]
    public void Editor_ZoomClusterFollowsThePreview_AndZooms() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var editor = new DeeperEditorWindow();
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            var cluster = editor.FindControl<StackPanel>("PreviewZoomCluster")!;
            var preview = editor.FindControl<WebHost>("BrowserPreview")!;
            preview.IsVisible = false;
            Assert.False(cluster.IsVisible);
            preview.IsVisible = true;
            Assert.True(cluster.IsVisible);

            editor.FindControl<Button>("BtnPreviewZoomIn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1.1, editor.PreviewBridge.Zoom, 6);
            preview.OnWebMessage(DeeperPageBridge.ZoomOut);
            Assert.Equal(1.0, editor.PreviewBridge.Zoom, 6);
        }
        finally { editor.Close(); }
    });
}
