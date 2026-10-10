using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using CCP.Avalonia.Tests.Board;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The taskbar showed a generic window icon for the shell (owner, 2026-10-09). Every window wears
/// the app icon through the App.axaml ":is(Window)" style, including ones that never set Icon.
/// </summary>
public sealed class AppIconTests
{
    [Fact]
    public Task The_shell_and_a_plain_window_wear_the_app_icon() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin();
        MainShellWindow? shell = null;
        var plain = new Window();
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            plain.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(shell.Icon);
            Assert.NotNull(plain.Icon);
        }
        finally
        {
            plain.Close();
            shell?.Close();
            BoardHeadTests.Unpin();
        }
        return Task.CompletedTask;
    });
}
