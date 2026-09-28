using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

namespace CCP.Avalonia.Tests;

/// <summary>The user paths that open ProgramsIntroPopup, RoadmapStepPopup and AwarenessPresetDetailDialog.</summary>
public sealed class ProgramsRoadmapAwarenessOpenersTests
{
    private static Task Run(System.Action<List<Window>> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        try { body(opened); }
        finally
        {
            foreach (var w in opened.ToArray()) w.Close();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ProgramsIntroOpensOnceAndSpendsItsFlag() => Run(opened =>
    {
        var settings = CoreSettings.Current;
        var was = settings.HasSeenProgramsIntro;
        try
        {
            settings.HasSeenProgramsIntro = false;
            ProgramsIntroPopup.ShowIfFirstTime(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened.OfType<ProgramsIntroPopup>());
            Assert.True(settings.HasSeenProgramsIntro);

            ProgramsIntroPopup.ShowIfFirstTime(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened.OfType<ProgramsIntroPopup>());
        }
        finally { settings.HasSeenProgramsIntro = was; }
    });

    [Fact]
    public Task RoadmapStepCompletedShowsTheStepPopup() => Run(opened =>
    {
        var step = new RoadmapStepDefinition("t1", RoadmapTrack.EmptyDoll, 1, "Title", "Objective", "Photo");
        MainShellWindow.OnRoadmapStepCompleted(null,
            new RoadmapStepCompletedEventArgs(step, new RoadmapStepProgress("t1"), false, false));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(opened.OfType<RoadmapStepPopup>());
    });

    [Fact]
    public Task AwarenessGridListsPresetsAndAdvancedLinkOpensTheInstalledOne() => Run(opened =>
    {
        var list = CoreSettings.Current.KeywordTriggerPresets;
        var preset = AwarenessTabView.NewCustomPreset();
        preset.MasterEnabled = true;
        list.Add(preset);
        try
        {
            var view = new AwarenessTabView();
            var host = new Window { Width = 1200, Height = 900, Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();

            var items = view.FindControl<WrapPanel>("AwarenessPresetItems")!;
            Assert.Equal(list.Count + 1, items.Children.Count);  // every preset + "New Preset"
            Assert.Equal(preset.Id, items.Children[list.Count - 1].Tag);

            view.FindControl<HyperlinkButton>("LnkAwarenessAdvanced")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened.OfType<AwarenessPresetDetailDialog>());
        }
        finally { list.Remove(preset); }
    });
}
