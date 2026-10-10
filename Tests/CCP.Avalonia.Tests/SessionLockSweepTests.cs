using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>PORTED from Tests/ConditioningControlPanel.Tests/SessionLockSweepTests.cs: the tree walk
/// that decides which dials get greyed while a session runs, and the tooltip save / restore that
/// explains the lock on a disabled control.</summary>
public sealed class SessionLockSweepTests
{
    private static Task OnUi(System.Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        body();
        return Task.CompletedTask;
    });

    private static CheckBox Owned(string name)
    {
        var c = new CheckBox { Name = name };
        SessionLock.SetOwned(c, true);
        return c;
    }

    [Fact]
    public Task FindsMarkedControlsThroughNestedContainers() => OnUi(() =>
    {
        var deep = Owned("deep");
        var root = new StackPanel();
        root.Children.Add(new Border { Child = new Grid { Children = { new StackPanel { Children = { deep } } } } });
        Assert.Same(deep, Assert.Single(SessionLock.FindOwnedControls(root)));
    });

    /// <summary>Several cards keep option panels collapsed until a parent toggle is ticked. A dial
    /// that escapes the lock merely because it was hidden when the session started would be a
    /// silent hole, so collapsed subtrees must still be swept.</summary>
    [Fact]
    public Task FindsMarkedControlsInsideCollapsedPanels() => OnUi(() =>
    {
        var hidden = Owned("hidden");
        var panel = new StackPanel { IsVisible = false };
        panel.Children.Add(hidden);
        var root = new StackPanel();
        root.Children.Add(panel);
        Assert.Same(hidden, Assert.Single(SessionLock.FindOwnedControls(root)));
    });

    [Fact]
    public Task IgnoresUnmarkedAndExplicitlyUnownedControls() => OnUi(() =>
    {
        var explicitlyFalse = new Slider { Name = "explicitlyFalse" };
        SessionLock.SetOwned(explicitlyFalse, false);
        var root = new StackPanel { Children = { new Slider { Name = "unmarked" }, explicitlyFalse } };
        Assert.Empty(SessionLock.FindOwnedControls(root));
    });

    /// <summary>The walk unions the logical and visual trees, so a control reachable through both
    /// must not be reported twice: callers count what they touched.</summary>
    [Fact]
    public Task DoesNotYieldTheSameControlTwice() => OnUi(() =>
    {
        var root = new StackPanel { Children = { Owned("a"), Owned("b") } };
        var host = new Window { Content = root };
        host.Show();
        try
        {
            var found = SessionLock.FindOwnedControls(root);
            Assert.Equal(2, found.Count);
            Assert.Equal(2, found.Distinct().Count());
        }
        finally { host.Close(); }
    });

    [Fact]
    public void NullRootYieldsNothing() => Assert.Empty(SessionLock.FindOwnedControls(null));

    [Fact]
    public Task FindsAMarkedRoot() => OnUi(() => Assert.Single(SessionLock.FindOwnedControls(Owned("root"))));

    /// <summary>A locked control is a disabled one, and a tooltip stays silent on a disabled control
    /// unless ShowOnDisabled is set: without it the explanation never appears at all.</summary>
    [Fact]
    public Task LockedControlShowsItsTooltipWhileDisabled() => OnUi(() =>
    {
        var c = Owned("c");
        SessionLock.ApplyLockToolTip(c, locked: true, "Locked by the session");
        Assert.Equal("Locked by the session", ToolTip.GetTip(c));
        Assert.True(ToolTip.GetShowOnDisabled(c));
    });

    [Fact]
    public Task RestoresAPreExistingTooltipOnUnlock() => OnUi(() =>
    {
        var c = Owned("c");
        ToolTip.SetTip(c, "How often");
        SessionLock.ApplyLockToolTip(c, true, "Locked");
        SessionLock.ApplyLockToolTip(c, false, null);
        Assert.Equal("How often", ToolTip.GetTip(c));
        Assert.False(ToolTip.GetShowOnDisabled(c));
    });

    [Fact]
    public Task LeavesNoTooltipBehindWhenThereWasNoneToStartWith() => OnUi(() =>
    {
        var c = Owned("c");
        SessionLock.ApplyLockToolTip(c, true, "Locked");
        SessionLock.ApplyLockToolTip(c, false, null);
        Assert.Null(ToolTip.GetTip(c));
        Assert.False(c.IsSet(ToolTip.TipProperty));
    });

    [Fact]
    public Task RepeatedLockPassesDoNotClobberTheSavedOriginal() => OnUi(() =>
    {
        var c = Owned("c");
        ToolTip.SetTip(c, "Original");
        SessionLock.ApplyLockToolTip(c, true, "Locked");
        SessionLock.ApplyLockToolTip(c, true, "Locked again");
        Assert.Equal("Locked again", ToolTip.GetTip(c));
        SessionLock.ApplyLockToolTip(c, false, null);
        Assert.Equal("Original", ToolTip.GetTip(c));
    });

    /// <summary>The unlocked paint runs on every sweep: a control we never borrowed from keeps its
    /// own tooltip and its own ShowOnDisabled.</summary>
    [Fact]
    public Task UnlockedPaintDoesNotEatTheTooltipOfANeverLockedControl() => OnUi(() =>
    {
        var c = Owned("c");
        ToolTip.SetTip(c, "Mine");
        ToolTip.SetShowOnDisabled(c, true);
        SessionLock.ApplyLockToolTip(c, false, null);
        SessionLock.ApplyLockToolTip(c, false, null);
        Assert.Equal("Mine", ToolTip.GetTip(c));
        Assert.True(ToolTip.GetShowOnDisabled(c));
    });

    [Fact]
    public Task TooltipSurvivesRepaintsAfterAFullLockUnlockCycle() => OnUi(() =>
    {
        var c = Owned("c");
        ToolTip.SetTip(c, "Original");
        for (var i = 0; i < 3; i++)
        {
            SessionLock.ApplyLockToolTip(c, true, "Locked");
            SessionLock.ApplyLockToolTip(c, false, null);
            SessionLock.ApplyLockToolTip(c, false, null);
        }
        Assert.Equal("Original", ToolTip.GetTip(c));
    });
}
