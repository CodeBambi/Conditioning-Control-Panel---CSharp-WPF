using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.HelpLoops;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Presets.cs SetupHelpButtons, ported as MainShellWindow.HelpButtons.cs:
/// every WPF tab "?" with an Avalonia twin is attached from shell startup and opens its section
/// (and its drawn loop where one exists); together with the feature cards and rack panels every
/// help loop is reachable from a "?" or from HelpVideoWindow.</summary>
public sealed class HelpButtonsTests
{
    private static readonly Regex Call = new(@"SetHelpContent\(\w+\.(HelpBtn\w+),\s*""(\w+)""\)");

    // Not a "?": WPF opens these from HelpVideoWindow (Session Editor ?, webcam calibration).
    private static readonly string[] VideoWindowLoops = { "SessionEditor", "WebcamCalibration" };

    private static readonly string[] Tabs =
    {
        "settings", "studio", "presets", "haptics", "companion", "awareness", "play", "gradedintake",
        "blinktrainer", "remotecontrol", "discord", "quests", "achievements", "leaderboard", "assets", "appsettings",
    };

    [Fact]
    public void ShellTableIsWpfSetupHelpButtons()
    {
        var root = RepoRoot();
        var wpf = Regex.Replace(File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "MainWindow", "MainWindow.Presets.cs")), @"//.*", "");
        var expected = Call.Matches(wpf).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        Assert.True(expected.Count > 30, $"scan found only {expected.Count} SetHelpContent calls");
        Assert.Equal(expected.OrderBy(p => p.Key), MainShellWindow.HelpButtonSections.OrderBy(p => p.Key));
    }

    [Fact]
    public Task EveryHelpButtonOpensItsSectionAndEveryLoopIsReachable() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var w = new MainShellWindow();
        w.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // A hidden tab is never measured, so its templated content does not exist until the
            // user opens it: walk every door the way a user does.
            var buttons = new HashSet<Button>();
            foreach (var tab in Tabs)
            {
                w.ShowTab(tab);
                Dispatcher.UIThread.RunJobs();
                // Open every disclosure on the page, as a user unfolding drawers would (nested ones too).
                for (var round = 0; round < 4; round++)
                {
                    foreach (var ex in w.GetVisualDescendants().OfType<Expander>().Where(e => !e.IsExpanded).ToList())
                        ex.IsExpanded = true;
                    Dispatcher.UIThread.RunJobs();
                }
                buttons.UnionWith(w.GetVisualDescendants().OfType<Button>());
            }
            // The Studio rack shows one module at a time; its Scheduler/Ramp panels carry their own ?.
            foreach (var rack in new[] { "scheduler", "ramp" })
            {
                w.ShowTab("studio");
                w.Named<StudioTabView>("StudioTab")!.FocusRackEntry(rack);
                Dispatcher.UIThread.RunJobs();
                buttons.UnionWith(w.GetVisualDescendants().OfType<Button>());
            }
            var problems = new List<string>();
            foreach (var (name, id) in MainShellWindow.HelpButtonSections)
            {
                var b = buttons.FirstOrDefault(x => x.Name == name);
                if (b is null) { problems.Add($"{name}: not in the shell"); continue; }
                if (HelpPopover.SectionOf(b) != id) { problems.Add($"{name}: attached to '{HelpPopover.SectionOf(b)}', want '{id}'"); continue; }
                b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                if (!HelpPopover.IsOpen(b)) problems.Add($"{name}: ? did not open");
                else if (HelpLoopRegistry.Has(id) && HelpPopover.PopupContent(b)!.GetVisualDescendants().OfType<HelpLoopView>().Count() != 1)
                    problems.Add($"{name}: popover has no {id} loop");
                HelpPopover.CloseActive();
            }
            // WPF MainWindow.UiUpdates.cs:1708: the Intake Pass tile's ? (no section, no loop).
            var pass = buttons.FirstOrDefault(x => x.Name == "BtnIntakePassHelp");
            if (pass is null || HelpPopover.SectionOf(pass) != "IntakePass") problems.Add("BtnIntakePassHelp: not attached");
            Assert.True(problems.Count == 0, string.Join("\n", problems));

            var reachable = buttons.Select(HelpPopover.SectionOf).Concat(VideoWindowLoops).ToHashSet();
            var orphans = HelpLoopRegistry.Ids.Where(id => !reachable.Contains(id)).OrderBy(x => x).ToList();
            Assert.True(HelpLoopRegistry.Ids.Count == 23 && orphans.Count == 0,
                $"{HelpLoopRegistry.Ids.Count} loops; no ? opens: {string.Join(", ", orphans)}");
        }
        finally
        {
            HelpPopover.CloseActive();
            w.Close();
        }
        return Task.CompletedTask;
    });

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
