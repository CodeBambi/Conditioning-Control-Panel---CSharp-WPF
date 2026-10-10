using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The tour engine on this head (ledger G1 / H4 / P8 / T15): WPF Services/TutorialService.cs behind
/// the CoreTutorial seam, and the coach mark drawn INSIDE the host window.
/// Ports WPF ShortWalkTourTests' engine half (the seven cards, the completion ledger).
/// Runs alone: it opens a shell and swaps process-wide seams (the tour service is one per process).
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class TutorialTourTests : IDisposable
{
    private sealed class FakeStore : TutorialService.ITourCompletionStore
    {
        public readonly List<string> Latched = new();
        public bool Has(string name) => Latched.Contains(name, StringComparer.OrdinalIgnoreCase);
        public void Latch(string name) { if (!Has(name)) Latched.Add(name); }
    }

    private readonly FakeStore _store = new();
    private readonly Func<MainShellWindow?> _shellBefore = TutorialHeadHooks.ShellProvider;

    public TutorialTourTests()
    {
        // Never the real emi-desk.json: a tour walked to its end in a test would latch there.
        TutorialService.CompletionStore = _store;
    }

    public void Dispose()
    {
        TutorialService.CompletionStore = null!;
        TutorialHeadHooks.ShellProvider = _shellBefore;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    // ---- the service (WPF ShortWalkTourTests) --------------------------------------------------

    [Fact]
    public void ShortWalkHasTheSevenContractStepsInOrder()
    {
        var svc = new TutorialService();
        svc.Start(TutorialType.ShortWalk);
        Assert.Equal(
            new[] { "sw-assets", "sw-flash", "sw-panic", "sw-dock", "sw-xp", "sw-settings", "sw-done" },
            svc.CurrentSteps.Select(s => s.Id).ToArray());
        Assert.Equal(7, svc.TotalSteps);
    }

    [Fact]
    public void WalkingOffTheEndLatchesTheTourAndSkippingNeverDoes()
    {
        var svc = new TutorialService();
        var endings = new List<bool>();
        svc.TutorialFinished += (_, e) => endings.Add(e.Completed);

        svc.Start(TutorialType.ShortWalk);
        svc.Skip();
        Assert.False(svc.IsActive);
        Assert.False(svc.HasCompleted(TutorialType.ShortWalk));

        svc.Start(TutorialType.ShortWalk);
        for (int i = 0; i < 7; i++) svc.Next();
        Assert.False(svc.IsActive);
        Assert.True(svc.HasCompleted(TutorialType.ShortWalk));
        Assert.Equal(new[] { false, true }, endings);

        svc.Skip();                                   // a second ending is not a second event
        Assert.Equal(2, endings.Count);
    }

    [Fact]
    public void EveryTourHasCardsWithTextAndUniqueIds()
    {
        foreach (var group in TutorialHead.AllSteps().GroupBy(x => x.Tour))
        {
            var steps = group.Select(x => x.Step).ToList();
            Assert.True(steps.Count > 0, group.Key + " has no cards");
            Assert.Equal(steps.Count, steps.Select(s => s.Id).Distinct().Count());
            foreach (var s in steps)
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Title), $"{group.Key}/{s.Id} title");
                Assert.False(string.IsNullOrWhiteSpace(s.Description), $"{group.Key}/{s.Id} body");
                Assert.DoesNotContain('—', s.Title + s.Description);
                Assert.DoesNotContain('–', s.Title + s.Description);
            }
        }
    }

    /// <summary>A step whose target does not exist on this head is LISTED, never silently lost:
    /// every target name is a Name in some axaml here, or sits in TargetsNotOnThisHead (and a name
    /// in that list really is absent, so the list cannot rot).</summary>
    [Fact]
    public void EveryTourTargetIsANamedControlOrIsListed()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "CCP.Avalonia"), "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "Name=\"([A-Za-z0-9_]+)\""))
                names.Add(m.Groups[1].Value);
        }

        var targets = TutorialHead.AllSteps().Select(x => x.Step.TargetElementName).Where(n => n != null).Distinct().ToList();
        Assert.True(targets.Count > 50, "the tours lost their targets");

        var unknown = targets.Where(n => !names.Contains(n!) && !TutorialHead.TargetsNotOnThisHead.Contains(n)).ToList();
        Assert.True(unknown.Count == 0, "Tour targets with no control and no listing: " + string.Join(", ", unknown));

        var stale = TutorialHead.TargetsNotOnThisHead.Where(names.Contains).ToList();
        Assert.True(stale.Count == 0, "Listed as missing but present: " + string.Join(", ", stale));
    }

    [Fact]
    public void TheSeamProjectsTheHeadStepAndAnUnknownNameStartsNothing()
    {
        var step = new TutorialStep
        {
            Id = "x", Title = "T", Description = "D", Icon = "!", TargetElementName = "BtnPanicKey",
            TextPosition = TutorialStepPosition.Left, AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "FileSaved", AllowManualSkip = true, BlockBackgroundClicks = false,
        };
        var core = TutorialHead.Project(step);
        Assert.Same(core, TutorialHead.Project(step));
        Assert.Equal("BtnPanicKey", core.TargetElementName);
        Assert.Equal(CoreTutorial.StepPosition.Left, core.TextPosition);
        Assert.Equal(CoreTutorial.AdvanceTrigger.OnEvent, core.Advance);
        Assert.Equal("FileSaved", core.AdvanceEventName);
        Assert.True(core.AllowManualSkip);
        Assert.False(core.BlockBackgroundClicks);

        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        TutorialHeadHooks.ShellProvider = () => null;
        CoreTutorial.Start("NoSuchTour");
        Assert.False(CoreTutorial.IsActive);
        CoreTutorial.Start("ShortWalk");              // a shell tour with no shell draws nothing
        Assert.False(CoreTutorial.IsActive);
    }

    [Fact]
    public void TextMatchesIsWpfs()
    {
        Assert.True(TutorialOverlay.TextMatches(" 30 ", "30"));
        Assert.True(TutorialOverlay.TextMatches("30.2", "30"));
        Assert.True(TutorialOverlay.TextMatches("My Good Girl", "good"));
        Assert.False(TutorialOverlay.TextMatches("31", "30"));
        Assert.False(TutorialOverlay.TextMatches("anything", ""));
    }

    // ---- the shell ------------------------------------------------------------------------------

    private static Control? Stage(MainShellWindow shell) =>
        OverlayLayer.GetOverlayLayer(shell)?.Children.OfType<Control>().FirstOrDefault(c => c.Name == "RootGrid");

    [Fact]
    public Task AGuideWalksInsideTheShellAndPanicEndsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        bool perfBefore = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true;        // Forever FX loops make RunJobs spin
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;
            int windowsBefore = shell.OwnedWindows.Count;

            // The ? panel's first row closes the panel and starts its tour.
            shell.SetTutorialOverlay(true);
            shell.StartGuide("GettingStarted");
            Dispatcher.UIThread.RunJobs();

            Assert.False(shell.GetVisualDescendants().OfType<Control>().First(c => c.Name == "MainTutorialOverlay").IsVisible);
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal("GettingStarted", CoreTutorial.CurrentTourName);
            Assert.Equal(0, CoreTutorial.CurrentStepIndex);

            // The card is a layer of the shell, not a window: nothing new on the desktop to take the
            // pointer or the focus from another app.
            var stage = Stage(shell);
            Assert.NotNull(stage);
            Assert.Equal(windowsBefore, shell.OwnedWindows.Count);
            var card = stage!.GetVisualDescendants().OfType<Border>().First(b => b.Name == "TextPanel");
            Assert.True(card.IsVisible);
            var title = stage.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "TxtTitle");
            Assert.Equal(CoreTutorial.CurrentStep!.Title, title.Text);

            // Next walks, Previous walks back.
            var next = stage.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BtnNext");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, CoreTutorial.CurrentStepIndex);
            Assert.Equal(CoreTutorial.CurrentStep!.Title, title.Text);
            var prev = stage.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BtnPrevious");
            Assert.True(prev.IsVisible);
            prev.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, CoreTutorial.CurrentStepIndex);

            // One tour at a time (WPF: StartTutorial returns while an overlay is up).
            shell.StartGuide("Settings");
            Assert.Equal(1, OverlayLayer.GetOverlayLayer(shell)!.Children.OfType<Control>().Count(c => c.Name == "RootGrid"));

            // Panic ends it at once, by the abandon route.
            TutorialHead.OnPanic();
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
            Assert.Null(Stage(shell));
            Assert.False(TutorialHead.Service.HasCompleted(TutorialType.GettingStarted));

            // A fresh tour starts after that, and Escape in the shell abandons it too.
            shell.StartGuide("ShortWalk");
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreTutorial.IsActive);
            Assert.NotNull(Stage(shell));
            shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
            Assert.Null(Stage(shell));
        }
        finally
        {
            TutorialHead.ResetForTests();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.PerformanceMode = perfBefore;
            CoreSettings.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    /// <summary>The ten Help rows that start a shell tour (the eleventh opens the Mod Creator on its
    /// own tour), the panel's big button, the Deeper Tutorial button and the Companion chip.</summary>
    [Fact]
    public Task EveryHelpRowAndBothPageButtonsStartTheirTour() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        bool perfBefore = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            TutorialHead.Seed();
            TutorialHead.ResetForTests();
            TutorialHeadHooks.ShellProvider = () => shell;

            var panel = shell.GetLogicalDescendants().OfType<Control>().First(c => c.Name == "MainTutorialOverlay");
            string guide = Loc.Get("btn_start_guide");
            var rows = panel.GetLogicalDescendants().OfType<Button>().Where(b => (b.Content as string) == guide).ToList();
            Assert.Equal(11, rows.Count);

            var expected = new[]
            {
                TutorialType.UpgradeTour, TutorialType.GettingStarted, TutorialType.Settings, TutorialType.Presets,
                TutorialType.Progression, TutorialType.Achievements, TutorialType.Companion, TutorialType.Patreon,
                TutorialType.Awareness, TutorialType.Avatar,
            };
            for (int i = 0; i < expected.Length; i++)
            {
                shell.SetTutorialOverlay(true);
                rows[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(TutorialHead.Service.IsActive, expected[i] + " did not start");
                Assert.Equal(expected[i], TutorialHead.Service.CurrentTutorialType);
                Assert.False(panel.IsVisible);
                Assert.NotNull(Stage(shell));
                CoreTutorial.Skip();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(Stage(shell));
            }

            var big = panel.GetLogicalDescendants().OfType<Button>().First(b => (b.Content as string) == Loc.Get("btn_start_interactive_tutorial"));
            big.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TutorialType.FullTour, TutorialHead.Service.CurrentTutorialType);
            Assert.True(TutorialHead.Service.IsActive);
            CoreTutorial.Skip();
            Dispatcher.UIThread.RunJobs();

            // Deeper's Tutorial button (P8) is back and starts the Deeper tour.
            var deeper = shell.GetLogicalDescendants().OfType<DeeperTabView>().Single();
            var deeperBtn = deeper.FindControl<Button>("BtnDeeperTutorial")!;
            Assert.True(deeperBtn.IsVisible);
            deeperBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TutorialType.Deeper, TutorialHead.Service.CurrentTutorialType);
            Assert.True(TutorialHead.Service.IsActive);
            CoreTutorial.Skip();
            Dispatcher.UIThread.RunJobs();

            // The Companion header's Tutorial chip (T15) is back and starts the Companion tour.
            var room = shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single();
            var chip = room.FindControl<CompanionHeroCard>("HeroZone")!.FindControl<Button>("BtnCompanionTutorial")!;
            Assert.True(chip.IsVisible);
            Assert.NotNull(chip.Command);
            chip.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(TutorialType.Companion, TutorialHead.Service.CurrentTutorialType);
            Assert.True(TutorialHead.Service.IsActive);
            CoreTutorial.Skip();
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreTutorial.IsActive);
        }
        finally
        {
            TutorialHead.ResetForTests();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.PerformanceMode = perfBefore;
            CoreSettings.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
