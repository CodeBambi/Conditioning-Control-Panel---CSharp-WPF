using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>shell-roadmap: the node strip painted from RoadmapService (WPF MainWindow.Roadmap.cs:118),
/// opened from the keyboard (P17), and the completion chime (WPF :459 SystemSounds.Exclamation).</summary>
public sealed class RoadmapNodeTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
    }

    private static void Press(Control c, Key key) =>
        c.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    [Fact]
    public void RoadmapSubTabPaintsNodesAndEnterOpensTheGateOrTheStartDialog() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        var host = new Window { Width = 1200, Height = 900, Content = new QuestsTabView() };
        try
        {
            host.Show();
            var view = (QuestsTabView)host.Content!;
            view.FindControl<Button>("BtnQuestSubRoadmap")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var steps = RoadmapStepDefinition.GetStepsForTrack(RoadmapTrack.EmptyDoll).ToList();
            var nodes = view.FindControl<StackPanel>("RoadmapNodesPanel")!.Children.OfType<Border>().ToList();
            Assert.Equal(steps.Select(s => s.Id), nodes.Select(n => (string)n.Tag!));
            Assert.All(nodes, n => Assert.True(n.Focusable));

            var roadmap = MainShellWindow.Roadmap;
            var locked = nodes.First(n => !roadmap.IsStepCompleted((string)n.Tag!) && !roadmap.IsStepActive((string)n.Tag!));
            Press(locked, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened.OfType<MessageDialog>());          // "Step Locked", not the start dialog
            Assert.Empty(opened.OfType<RoadmapStartDialog>());

            var active = nodes.First(n => roadmap.IsStepActive((string)n.Tag!));
            Press(active, Key.Space);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened.OfType<RoadmapStartDialog>());
        }
        finally
        {
            foreach (var w in opened.ToArray()) w.Close();
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void StepCompletedPlaysTheChime() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var old = CoreAudio.PlayOneShotProvider;
        var tags = new List<string>();
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        try
        {
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            var step = new RoadmapStepDefinition("t1", RoadmapTrack.EmptyDoll, 1, "Title", "Objective", "Photo");
            MainShellWindow.OnRoadmapStepCompleted(null,
                new RoadmapStepCompletedEventArgs(step, new RoadmapStepProgress("t1"), false, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "roadmap-step" }, tags);
        }
        finally
        {
            CoreAudio.PlayOneShotProvider = old;
            foreach (var w in opened.ToArray()) w.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
