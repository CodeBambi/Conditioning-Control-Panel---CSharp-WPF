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
        // Another test's first run can leave the quiet window open; these openers assume it is shut.
        global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
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
            var popup = Assert.Single(opened.OfType<ProgramsIntroPopup>());
            Assert.True(settings.HasSeenProgramsIntro);
            // WPF ProgramsIntroPopup.xaml.cs:189: the featured program's sigil masks the accent rail.
            var sigil = popup.FindControl<global::Avalonia.Controls.Shapes.Rectangle>("ArtSigil")!;
            Assert.True(sigil.IsVisible);
            Assert.IsType<global::Avalonia.Media.Imaging.Bitmap>(
                Assert.IsType<global::Avalonia.Media.ImageBrush>(sigil.OpacityMask).Source);

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

    /// <summary>The head ships Assets/AwarenessPresets like WPF, so the settings merge finds the built-ins.</summary>
    [Fact]
    public void BuiltInAwarenessPresetsLoadFromTheOutput()
    {
        var settings = new AppSettings();
        settings.KeywordTriggerPresets.Clear();
        var service = (ConditioningControlPanel.Services.SettingsService)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ConditioningControlPanel.Services.SettingsService));
        typeof(ConditioningControlPanel.Services.SettingsService)
            .GetMethod("MergeBuiltInAwarenessPresets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(service, new object[] { settings });
        Assert.Contains(settings.KeywordTriggerPresets, p => p.Id == "builtin.puppy");
        Assert.True(settings.KeywordTriggerPresets.Count >= 4);
    }

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
