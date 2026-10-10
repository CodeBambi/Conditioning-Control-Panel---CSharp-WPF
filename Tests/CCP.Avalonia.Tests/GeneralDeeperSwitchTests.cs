using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Settings ▸ General ▸ Deeper master switch, through the shell: WPF 7.1.5 falls back to Settings
/// when Deeper was the open tab (MainWindow.DeeperTab.cs:127). The rail's Deeper door left in the
/// nav rework; Deeper is a Play pill now and the switch hides no door (7.1.5 has no reader of
/// EnableDeeper in the nav), so the pill stays on Play either way.
/// </summary>
public sealed class GeneralDeeperSwitchTests
{
    [Fact]
    public async Task DeeperSwitchLeavesTheDeeperTabAndThePlayPillStays()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var s = CoreSettings.Current;
            var before = s.EnableDeeper;
            MainShellWindow? w = null;
            try
            {
                s.EnableDeeper = false;
                w = new MainShellWindow();
                w.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(w.Named<Button>("BtnDeeper"));   // the rail door is gone (nav rework)

                var general = w.GetLogicalDescendants().OfType<GeneralSettingsSection>().First();
                var box = general.FindControl<CheckBox>("ChkEnableDeeper")!;
                box.IsChecked = true;   // the user turns Deeper on
                Dispatcher.UIThread.RunJobs();
                Assert.True(s.EnableDeeper);

                w.ShowTab("deeper");
                Dispatcher.UIThread.RunJobs();
                var deeperTab = w.Named<Control>("DeeperTab")!;
                Assert.True(deeperTab.IsVisible);
                Assert.Contains(w.GetVisualDescendants().OfType<Button>(), b => b.Name == "NavPill_deeper");

                box.IsChecked = false;   // ... and off again while Deeper is open
                Dispatcher.UIThread.RunJobs();
                Assert.False(s.EnableDeeper);
                Assert.False(deeperTab.IsVisible, "Deeper tab stayed open after the switch went off");
                Assert.Equal("settings", w.CurrentTab);   // WPF ShowTab("settings"), the same key
            }
            finally
            {
                w?.Close();
                s.EnableDeeper = before;
            }
            return Task.CompletedTask;
        });
    }
}
