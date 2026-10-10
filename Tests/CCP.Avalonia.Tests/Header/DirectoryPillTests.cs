using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests.Header;

/// <summary>WPF 7.1.5 MainWindow.RemoteControl.cs:693 (UpdateDirectoryListingStatus): the title bar
/// pill's four states. Ledger H16.</summary>
public sealed class DirectoryPillTests
{
    [Fact]
    public void NoSessionHidesThePill()
    {
        Assert.False(MainShellWindow.DirectoryPillFor(active: false, optedIn: false, claimed: false).Visible);
        Assert.False(MainShellWindow.DirectoryPillFor(active: false, optedIn: true, claimed: true).Visible);
    }

    [Fact]
    public void ARunningSessionThatIsNotListedSaysPrivateOnly()
    {
        var pill = MainShellWindow.DirectoryPillFor(active: true, optedIn: false, claimed: true);
        Assert.True(pill.Visible);
        Assert.Equal("Private only", pill.Text);
        Assert.Equal("#8A8AA0", pill.DotHex);
    }

    [Fact]
    public void AListedSessionSaysListedThenClaimed()
    {
        var listed = MainShellWindow.DirectoryPillFor(active: true, optedIn: true, claimed: false);
        Assert.Equal(("Listed", "#B47BFF"), (listed.Text, listed.DotHex));
        var claimed = MainShellWindow.DirectoryPillFor(active: true, optedIn: true, claimed: true);
        Assert.Equal(("Claimed", "#00FF88"), (claimed.Text, claimed.DotHex));
    }

    [Fact]
    public void TheTooltipsCarryNoLongDashes()
    {
        foreach (var optedIn in new[] { false, true })
            foreach (var claimed in new[] { false, true })
            {
                var tip = MainShellWindow.DirectoryPillFor(true, optedIn, claimed).Tip;
                Assert.NotEmpty(tip);
                Assert.DoesNotContain('—', tip);
                Assert.DoesNotContain('–', tip);
            }
    }
}
