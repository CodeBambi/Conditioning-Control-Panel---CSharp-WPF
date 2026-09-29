using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.SessionIO.cs HandleSessionDrop + SessionBtn_Delete on the Avalonia rack.</summary>
public sealed class SessionIoTests
{
    /// <summary>WPF #1303: a recap that ends under a Bubble Count cover opens passive (not
    /// activated, non-modal), and a second one replaces the first rather than stacking.</summary>
    [Fact]
    public Task RecapUnderACoverOpensPassiveAndKeepsOnlyOne() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        shell.Show();
        _ = new ConditioningControlPanel.Avalonia.Views.Windows.BubbleCountWindow("x.mp4",
            ConditioningControlPanel.Avalonia.Views.Windows.Difficulty.Easy, false, _ => { });
        try
        {
            var log = new SessionLog { SessionId = "s", SessionName = "S", Duration = TimeSpan.FromSeconds(5) };
            shell.OnSessionLogReady(null, new SessionLogReadyEventArgs(log));
            shell.OnSessionLogReady(null, new SessionLogReadyEventArgs(log));
            Dispatcher.UIThread.RunJobs();
            var recap = Assert.Single(shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Windows.SessionCompleteWindow>());
            Assert.False(recap.ShowActivated);
            recap.Close();
        }
        finally
        {
            ConditioningControlPanel.Avalonia.Views.Windows.BubbleCountWindow.ForceCloseAll();
            shell.Close();
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF SessionLock.Owned sweep: a running session greys every marked dose dial in the
    /// Studio rack and on the Graded Intake tab, and gives the original tooltip back after.</summary>
    [Fact]
    public Task SessionLockGreysOwnedDialsAndRestoresTheirTooltips() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        shell.Show();
        try
        {
            var flash = shell.StudioRack!.HostedFeaturePanels
                .OfType<ConditioningControlPanel.Avalonia.Views.Features.FlashFeatureControl>().Single();
            var freq = flash.FindControl<Slider>("SliderFrequency")!;
            var quiz = shell.Named<UserControl>("GradedIntakeTab")!.FindControl<Slider>("SliderPopQuizFrequency")!;
            var haptics = shell.StudioRack!.HapticsPanel.FindControl<CheckBox>("ChkHapticsEnabled")!;
            ToolTip.SetTip(freq, "original");

            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = () => true;
            shell.RefreshSessionFeatureLock();
            Assert.False(freq.IsEnabled);
            Assert.False(quiz.IsEnabled);
            Assert.False(haptics.IsEnabled);
            Assert.NotEqual("original", ToolTip.GetTip(freq));

            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = () => false;
            shell.RefreshSessionFeatureLock();
            Assert.True(freq.IsEnabled);
            Assert.True(quiz.IsEnabled);
            Assert.True(haptics.IsEnabled);
            Assert.Equal("original", ToolTip.GetTip(freq));
        }
        finally
        {
            ConditioningControlPanel.CoreSession.IsSessionRunningProvider = null;
            shell.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DroppedSessionIsImportedAndItsRowDeleteButtonRemovesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var root = Path.Combine(Path.GetTempPath(), "ccp-session-io-" + Guid.NewGuid().ToString("N"));
        var custom = Path.Combine(root, "custom");
        var builtIn = Path.Combine(root, "built-in");
        Directory.CreateDirectory(custom);
        Directory.CreateDirectory(builtIn);
        Window? host = null;
        try
        {
            var files = new SessionFileService(custom, builtIn);
            var definition = SessionDefinition.FromSession(Session.MorningDrift);
            definition.Id = "dropped";
            definition.Name = "Dropped";
            var outside = Path.Combine(root, "dropped.session.json");
            files.ExportSession(definition, outside);
            var manager = new SessionManager(files);
            manager.LoadAllSessions();

            var view = new PresetsTabView { Width = 1100, Height = 760 };
            host = new Window { Width = 1100, Height = 760, Content = view };
            host.Show();
            view.UseSessionManager(manager);
            view.HandleSessionDrop(outside);
            Dispatcher.UIThread.RunJobs();

            var imported = Assert.Single(Directory.GetFiles(custom));
            var rack = view.FindControl<StackPanel>("SessionRackPanel")!;
            var row = rack.Children.OfType<Border>().Single(b => (b.Tag as Session)?.Id == "dropped");
            var builtInRow = rack.Children.OfType<Border>().First(b => (b.Tag as Session)?.Source == SessionSource.BuiltIn);
            Assert.Equal(2, builtInRow.GetVisualDescendants().OfType<Button>().Count(b => b.IsEnabled));

            row.GetVisualDescendants().OfType<Button>().Last(b => b.IsEnabled)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var confirm = Assert.Single(host.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get("btn_delete"), Assert.IsType<TextBlock>(confirm.FindControl<Button>("BtnOk")!.Content).Text);
            Assert.True(confirm.FindControl<Button>("BtnCancel")!.IsVisible);
            confirm.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.False(File.Exists(imported));
            Assert.DoesNotContain(rack.Children.OfType<Border>(), b => (b.Tag as Session)?.Id == "dropped");
        }
        finally
        {
            host?.Close();
            try { Directory.Delete(root, true); } catch { }
        }
        return Task.CompletedTask;
    });
    /// <summary>WPF HandleSessionDrop: an invalid file is refused with "Invalid: ..." and nothing
    /// is copied into CustomSessions or added to the rack.</summary>
    [Fact]
    public Task BadSessionDropIsRejectedAndSavesNothing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var root = Path.Combine(Path.GetTempPath(), "ccp-session-bad-" + Guid.NewGuid().ToString("N"));
        var custom = Path.Combine(root, "custom");
        var builtIn = Path.Combine(root, "built-in");
        Directory.CreateDirectory(custom);
        Directory.CreateDirectory(builtIn);
        try
        {
            var manager = new SessionManager(new SessionFileService(custom, builtIn));
            manager.LoadAllSessions();
            var bad = Path.Combine(root, "bad.session.json");
            File.WriteAllText(bad, "{ not json");
            var view = new PresetsTabView();
            view.UseSessionManager(manager);
            var before = manager.AllSessions.Count;

            view.HandleSessionDrop(bad);

            var status = view.FindControl<TextBlock>("DropZoneStatus")!;
            Assert.True(status.IsVisible);
            Assert.StartsWith("Invalid: ", status.Text);
            Assert.Empty(Directory.GetFiles(custom));
            Assert.Equal(before, manager.AllSessions.Count);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
        return Task.CompletedTask;
    });
}
