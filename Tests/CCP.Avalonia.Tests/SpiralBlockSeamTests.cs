using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HC6: the Spiral Room and the profile's two spiral doors read one block (WPF App.Descent.Current),
/// so a block that arrives takes the room from Waiting to Spiral.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class SpiralBlockSeamTests
{
    [Fact]
    public void TheRoomReadsTheSameBlockTheDoorsDo()
    {
        var old = (MainShellWindow.ProfileSpiralBlock, MainShellWindow.ProfileSpiralWithheld);
        try
        {
            MainShellWindow.ProfileSpiralWithheld = null;
            MainShellWindow.ProfileSpiralBlock = null;
            Assert.False(MainShellWindow.HasSpiralBlock);
            Assert.Equal(SpiralRoomState.Waiting, Room());

            MainShellWindow.ProfileSpiralBlock = () => new DescentBlock { DevotionDays = 12, Stage = new DescentStage { N = 3, NextAt = 30 } };
            Assert.True(MainShellWindow.HasSpiralBlock);
            Assert.False(MainShellWindow.SpiralIsWithheld);
            Assert.Equal(SpiralRoomState.Spiral, Room());

            MainShellWindow.ProfileSpiralBlock = () => throw new InvalidOperationException("boom");
            Assert.False(MainShellWindow.HasSpiralBlock);   // a provider that throws is no block

            MainShellWindow.ProfileSpiralWithheld = () => throw new InvalidOperationException("boom");
            Assert.True(MainShellWindow.SpiralIsWithheld);   // a rule that throws withholds
        }
        finally { (MainShellWindow.ProfileSpiralBlock, MainShellWindow.ProfileSpiralWithheld) = old; }
    }

    // The exact call SpiralTabView.Refresh makes, on a migrated account with the fuse dark.
    private static SpiralRoomState Room() => SpiralRoom.StateFor(
        DescentFusePhase.Dark, fuseArmed: false,
        spiralWithheld: MainShellWindow.SpiralIsWithheld,
        migrationCompleted: true, hasValidPendingChoice: false,
        hasBlock: MainShellWindow.HasSpiralBlock);
}
