using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.HelpLoops;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HelpVideoWindow hosts the topic's drawn help loop like WPF TryShowLoop (help-loops-d):
/// the Session Editor's ? opens it with the SessionEditor loop playing; a topic without a loop
/// keeps WPF's caption fallback.</summary>
public sealed class HelpVideoWindowTests
{
    [Fact]
    public Task SessionEditorHelpOpensTheWindowWithTheLoopPlaying() => Run(() =>
    {
        var editor = new SessionEditorWindow();
        editor.Show();
        try
        {
            editor.FindControl<Button>("BtnHelp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var win = Current();
            Assert.NotNull(win);
            Assert.False(editor.FindControl<Control>("TutorialOverlay")!.IsVisible);

            var view = win!.GetVisualDescendants().OfType<HelpLoopView>().Single();
            Assert.Equal("SessionEditor", view.Scene.Id);
            Assert.True(win.FindControl<Border>("VideoContainer")!.IsVisible);
            Assert.False(win.FindControl<TextBlock>("TxtCaption")!.IsVisible);
            Assert.Single(win.GetVisualDescendants().OfType<HelpLoopSteps>());
            Assert.True(view.IsRunning);
            win.CaptureRenderedFrame();
            Assert.False(view.Failed);
            win.Close();
            Assert.Null(Current());
            Assert.False(view.IsRunning);
        }
        finally { Current()?.Close(); editor.Close(); }
    });

    [Fact]
    public Task WebcamCalibrationTopicHostsItsSliceCLoop() => Run(() =>
    {
        // What WebcamCalibrationWindow's ? passes (WebcamCalibrationWindow.axaml.cs:239).
        HelpVideoWindow.Show(HelpContentService.GetContent("WebcamCalibration"), null, topmost: true);
        var win = Current()!;
        try
        {
            var view = win.GetVisualDescendants().OfType<HelpLoopView>().Single();
            Assert.Equal("WebcamCalibration", view.Scene.Id);
            Assert.False(win.FindControl<TextBlock>("TxtCaption")!.IsVisible);
            win.CaptureRenderedFrame();
            Assert.False(view.Failed);
        }
        finally { win.Close(); }
    });

    [Fact]
    public Task TopicWithoutLoopKeepsTheCaptionFallback() => Run(() =>
    {
        var content = HelpContentService.GetContent("Modding");
        Assert.False(HelpLoopRegistry.Has(content.SectionId));
        HelpVideoWindow.Show(content, null);
        var win = Current()!;
        try
        {
            Assert.False(win.FindControl<Border>("VideoContainer")!.IsVisible);
            Assert.Empty(win.GetVisualDescendants().OfType<HelpLoopView>());
            var caption = win.FindControl<TextBlock>("TxtCaption")!;
            Assert.True(caption.IsVisible);
            Assert.Equal(Loc.Get(content.CaptionKey!), caption.Text);
        }
        finally { win.Close(); }
    });

    private static HelpVideoWindow? Current() =>
        (HelpVideoWindow?)typeof(HelpVideoWindow).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        HelpLoopView.RequestAnimationFrame = (_, _) => { };   // no real frame clock in tests
        try { body(); }
        finally
        {
            HelpLoopView.RequestAnimationFrame = (top, cb) => top.RequestAnimationFrame(cb);
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });
}
