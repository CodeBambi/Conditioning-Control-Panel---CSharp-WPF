using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Patreon.cs:2214: the tube menu's "Pause browser" and the Companion Behavior switch
/// are one state. The switch follows the shell without sending the change back.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class PauseBrowserSyncTests
{
    [Fact]
    public void The_behavior_switch_follows_the_shell_and_only_a_users_flip_is_reported() => AvaloniaTestDispatcher.Run(() =>
    {
        var cell = new WorkshopBehaviorCell();
        var box = cell.FindControl<CheckBox>("ChkPauseBrowserCompanion")!;
        var reported = 0;
        cell.PauseBrowserChanged += (_, _) => reported++;

        cell.SetPauseBrowser(true);          // the tube menu paused it
        Assert.True(box.IsChecked);
        Assert.Equal(0, reported);
        cell.SetPauseBrowser(false);
        Assert.False(box.IsChecked);
        Assert.Equal(0, reported);

        box.IsChecked = true;                // the player flips the switch
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, reported);
    });
}
