using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Settings ▸ General ▸ Deeper master switch, through the shell: WPF hides the rail's Deeper door
/// on load and on toggle, and falls back to Settings when Deeper was the open tab
/// (MainWindow.Settings.cs:119, MainWindow.DeeperTab.cs:127).
/// </summary>
public sealed class GeneralDeeperSwitchTests
{
    [Fact]
    public async Task DeeperSwitchHidesTheRailDoorAndLeavesTheDeeperTab()
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
                w = new MainShellWindow();   // startup: the door follows the stored switch
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var door = w.Named<Button>("BtnDeeper")!;
                Assert.False(door.IsVisible, "Deeper door shown at startup with Deeper disabled");

                var general = w.GetLogicalDescendants().OfType<GeneralSettingsSection>().First();
                var box = general.FindControl<CheckBox>("ChkEnableDeeper")!;
                box.IsChecked = true;   // the user turns Deeper on
                Dispatcher.UIThread.RunJobs();
                Assert.True(door.IsVisible);

                w.ShowTab("deeper");
                Dispatcher.UIThread.RunJobs();
                var deeperTab = w.Named<Control>("DeeperTab")!;
                Assert.True(deeperTab.IsVisible);

                box.IsChecked = false;   // ... and off again while Deeper is open
                Dispatcher.UIThread.RunJobs();
                Assert.False(s.EnableDeeper);
                Assert.False(door.IsVisible, "Deeper door kept after the switch went off");
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
