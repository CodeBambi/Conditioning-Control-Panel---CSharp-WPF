using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Presets tab session detail (WPF MainWindow.SessionIO.cs SelectSession, Presets.cs
/// BtnRevealSpoilers_Click / BtnCatalogue_Click), driven from the shell: a rack row picked by
/// keyboard fills the detail pane, the three spoiler warnings guard the reveal, and the Catalogue
/// chip opens the web catalogue.</summary>
public sealed class PresetsTabSessionDetailTests
{
    [Fact]
    public Task RowFillsDetailRevealAsksThriceAndCatalogueChipOpens() => Run(shell =>
    {
        CoreSettings.Current.PlayerLevel = 40;   // multiplier 1.1 (SessionXp.Multiplier)
        shell.ShowTab("presets");
        Dispatcher.UIThread.RunJobs();
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var row = tab.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>()
            .First(b => b.Tag is Session { Id: "good_girls_dont_cum" });
        var session = (Session)row.Tag!;
        row.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();

        string Text(string name) => tab.FindControl<TextBlock>(name)!.Text ?? "";
        Assert.Equal(PresetsTabView.SessionTimelineLine(session), Text("TxtDetailSubtitle"));
        Assert.Equal(Loc.GetF("label_0_minutes", session.DurationMinutes), Text("TxtSessionDuration"));
        Assert.Contains(Loc.GetF("session_timeline_flashes", session.Settings.FlashPerHour), Text("TxtDetailSubtitle"));
        Assert.Equal($"{Loc.GetF("rack_xp", (int)System.Math.Round(session.BonusXP * 1.1))} (1.1x)", Text("TxtSessionXP"));
        Assert.Equal(Color.FromRgb(255, 165, 0), ((ISolidColorBrush)tab.FindControl<TextBlock>("TxtSessionXP")!.Foreground!).Color);
        Assert.EndsWith(session.GenerateFeatureDescription(), Text("TxtSessionDescription"));
        Assert.Equal(session.GetSpoilerFlash(), Text("TxtSessionFlash"));
        Assert.Equal(session.GetSpoilerTimeline(), Text("TxtSessionTimeline"));

        var panel = tab.FindControl<StackPanel>("SessionSpoilerPanel")!;
        var reveal = tab.FindControl<Button>("BtnRevealSpoilers")!;
        MessageDialog Dialog() => Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
        void Answer(bool ok)
        {
            Dialog().FindControl<Button>(ok ? "BtnOk" : "BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Get("spoiler_warning1_title"), Dialog().Title);
        Answer(true);
        Assert.Equal(Loc.Get("spoiler_warning2_title"), Dialog().Title);
        Answer(false);                                                  // "You're right, nevermind"
        Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
        Assert.False(panel.IsVisible);

        reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Answer(true);
        Answer(true);
        Assert.Equal(Loc.Get("spoiler_warning3_title"), Dialog().Title);
        Answer(true);
        Assert.True(panel.IsVisible);
        Assert.Equal(Loc.Get("btn_hide_details"), Text("TxtRevealSpoilers"));

        reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // hide: no dialog
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
        Assert.False(panel.IsVisible);
        Assert.Equal(Loc.Get("btn_reveal_details"), Text("TxtRevealSpoilers"));

        // Tests run sandboxed, so the real URL is refused before any launcher; the refusal log
        // names the host, which proves the chip reached ExternalOpener with the catalogue URL.
        var sink = new Sink();
        var previous = Serilog.Log.Logger;
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var chip = tab.FindControl<Border>("SessionDropZone")!;
            Assert.True(chip.IsEnabled && chip.Focusable);
            chip.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(sink.Lines, l => l == "Sandbox: refused to open \"app.cclabs.app\"");
        }
        finally { Serilog.Log.Logger = previous; }
    });

    private sealed class Sink : Serilog.Core.ILogEventSink
    {
        public readonly List<string> Lines = new();
        public void Emit(Serilog.Events.LogEvent e) { lock (Lines) Lines.Add(e.RenderMessage()); }
    }

    private static Task Run(System.Action<MainShellWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var level = CoreSettings.Current.PlayerLevel;
        var shell = new MainShellWindow();
        shell.Show();
        try { body(shell); }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            CoreSettings.Current.PlayerLevel = level;
        }
        return Task.CompletedTask;
    });
}
