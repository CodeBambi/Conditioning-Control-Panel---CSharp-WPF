using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Exercises the two pieces of <see cref="SessionLock"/> that carry real logic:
/// <see cref="SessionLock.FindOwnedControls"/> (the tree walk that decides which dials get greyed
/// out while a session runs) and <see cref="SessionLock.ApplyLockToolTip"/> (the save/restore that
/// keeps the lock from eating a control's own tooltip).
///
/// Deliberately built against SYNTHETIC control trees rather than the real
/// Features/*FeatureControl.xaml types. Those declare <c>Style="{StaticResource ToggleStyle}"</c>,
/// so loading one needs the application's resource dictionaries and an Application instance -
/// a lot of fragile setup to test code that only cares about tree SHAPE. The shapes reproduced
/// here (Border > Grid > StackPanel nesting, and a Collapsed panel holding a marked dial) are
/// the ones the real popups actually use. Which controls carry the marker in the shipped XAML is
/// covered separately and without WPF by SessionLockMarkerTests.
///
/// WPF objects have thread affinity and must be created on an STA thread; xunit's worker threads
/// are MTA, hence the OnSta helper.
/// </summary>
public class SessionLockSweepTests
{
    /// <summary>Runs <paramref name="body"/> on a dedicated STA thread, rethrowing faithfully.</summary>
    private static void OnSta(Action body)
    {
        ExceptionDispatchInfo? captured = null;
        var t = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { captured = ExceptionDispatchInfo.Capture(e); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "STA test thread hung");
        captured?.Throw();
    }

    private static CheckBox Owned(string name)
    {
        var c = new CheckBox { Name = name };
        SessionLock.SetOwned(c, true);
        return c;
    }

    [Fact]
    public void FindsMarkedControlsThroughNestedContainers()
    {
        OnSta(() =>
        {
            var deep = Owned("deep");
            var root = new StackPanel();
            root.Children.Add(new Border
            {
                Child = new Grid { Children = { new StackPanel { Children = { deep } } } }
            });

            var found = SessionLock.FindOwnedControls(root);

            Assert.Single(found);
            Assert.Same(deep, found[0]);
        });
    }

    /// <summary>
    /// The case the logical-tree walk exists for. Several popups keep option panels Collapsed
    /// until a parent toggle is ticked (BubblePop's TriggerOptionsPanel, Video's attention block).
    /// A dial that escapes the lock merely because it was hidden when the session started would be
    /// a silent hole, so collapsed subtrees must still be swept.
    /// </summary>
    [Fact]
    public void FindsMarkedControlsInsideCollapsedPanels()
    {
        OnSta(() =>
        {
            var hidden = Owned("hidden");
            var panel = new StackPanel { Visibility = Visibility.Collapsed };
            panel.Children.Add(hidden);

            var root = new StackPanel();
            root.Children.Add(panel);

            Assert.Same(hidden, Assert.Single(SessionLock.FindOwnedControls(root)));
        });
    }

    [Fact]
    public void IgnoresUnmarkedAndExplicitlyUnownedControls()
    {
        OnSta(() =>
        {
            var unmarked = new Slider { Name = "unmarked" };
            var explicitlyFalse = new Slider { Name = "explicitlyFalse" };
            SessionLock.SetOwned(explicitlyFalse, false);

            var root = new StackPanel { Children = { unmarked, explicitlyFalse } };

            Assert.Empty(SessionLock.FindOwnedControls(root));
        });
    }

    /// <summary>
    /// The walk unions the logical and visual trees, so a control reachable via both must not be
    /// reported twice - callers count what they touched.
    /// </summary>
    [Fact]
    public void DoesNotYieldTheSameControlTwice()
    {
        OnSta(() =>
        {
            var root = new StackPanel { Children = { Owned("a"), Owned("b") } };

            var found = SessionLock.FindOwnedControls(root);

            Assert.Equal(2, found.Count);
            Assert.Equal(2, found.Distinct().Count());
        });
    }

    [Fact]
    public void NullRootYieldsNothing()
        => Assert.Empty(SessionLock.FindOwnedControls(null));

    /// <summary>A marked root is itself eligible, not just its descendants.</summary>
    [Fact]
    public void FindsAMarkedRoot()
        => OnSta(() => Assert.Single(SessionLock.FindOwnedControls(Owned("root"))));

    // -------------------------------------------------------------------------------------
    // ApplyLockToolTip
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// WPF hides tooltips on disabled controls unless ShowOnDisabled is set. The lock's whole
    /// point is to explain itself, so this must be on while locked - without it the user just
    /// finds a dead grey dial. This was the defect in the previously shipped program lock.
    /// </summary>
    [Fact]
    public void LockedControlShowsItsTooltipWhileDisabled()
    {
        OnSta(() =>
        {
            var c = new CheckBox();

            SessionLock.ApplyLockToolTip(c, locked: true, reason: "you are in a session");

            Assert.Equal("you are in a session", c.ToolTip);
            Assert.True(ToolTipService.GetShowOnDisabled(c));
        });
    }

    /// <summary>
    /// Regression guard. A control that already owned a tooltip (ChkAwarenessMaster carries a
    /// whole explanatory panel) must get it back verbatim on unlock. A bare
    /// ClearValue(ToolTipProperty) would erase the XAML-declared tooltip instead of restoring it,
    /// silently stripping documentation after a single lock/unlock cycle.
    /// </summary>
    [Fact]
    public void RestoresAPreExistingTooltipOnUnlock()
    {
        OnSta(() =>
        {
            var original = new ToolTip { Content = "the control's own help" };
            var c = new CheckBox { ToolTip = original };

            SessionLock.ApplyLockToolTip(c, locked: true, reason: "locked");
            Assert.Equal("locked", c.ToolTip);

            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);

            Assert.Same(original, c.ToolTip);
            Assert.False(ToolTipService.GetShowOnDisabled(c));
        });
    }

    /// <summary>"Had no tooltip" is a real state and must be restored by clearing, not left set.</summary>
    [Fact]
    public void LeavesNoTooltipBehindWhenThereWasNoneToStartWith()
    {
        OnSta(() =>
        {
            var c = new CheckBox();

            SessionLock.ApplyLockToolTip(c, locked: true, reason: "locked");
            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);

            Assert.Null(c.ToolTip);
        });
    }

    /// <summary>
    /// The per-second heartbeat and every session event can repaint an already-locked control.
    /// Saving must happen once: a second lock pass must not overwrite the saved original with the
    /// lock's own reason string, or unlocking would restore the wrong text.
    /// </summary>
    [Fact]
    public void RepeatedLockPassesDoNotClobberTheSavedOriginal()
    {
        OnSta(() =>
        {
            var c = new CheckBox { ToolTip = "mine" };

            SessionLock.ApplyLockToolTip(c, locked: true, reason: "first");
            SessionLock.ApplyLockToolTip(c, locked: true, reason: "second");
            SessionLock.ApplyLockToolTip(c, locked: true, reason: "third");
            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);

            Assert.Equal("mine", c.ToolTip);
        });
    }

    /// <summary>Unlocking a control that was never locked must not throw or invent state.</summary>
    [Fact]
    public void UnlockingAnUntouchedControlIsHarmless()
    {
        OnSta(() =>
        {
            var c = new CheckBox();
            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);
            Assert.Null(c.ToolTip);
        });
    }

    /// <summary>
    /// TRAP 1, found by play-test. An unlocked paint on a control we never locked must be a total
    /// no-op. An earlier version called ClearValue here, which permanently erased XAML-declared
    /// tooltips - and since the lock repaints on every session event, tab switch and heartbeat,
    /// it destroyed tooltips on controls that had nothing to do with any session.
    /// </summary>
    [Fact]
    public void UnlockedPaintDoesNotEatTheTooltipOfANeverLockedControl()
    {
        OnSta(() =>
        {
            var c = new CheckBox { ToolTip = "my own help text" };

            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);
            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);

            Assert.Equal("my own help text", c.ToolTip);
        });
    }

    /// <summary>
    /// The second half of trap 1: a successful restore clears the saved-flag, so the NEXT unlocked
    /// paint took the never-locked branch and destroyed the tooltip it had just put back. A full
    /// cycle followed by further unlocked paints must be stable.
    /// </summary>
    [Fact]
    public void TooltipSurvivesRepaintsAfterAFullLockUnlockCycle()
    {
        OnSta(() =>
        {
            var c = new CheckBox { ToolTip = "mine" };

            SessionLock.ApplyLockToolTip(c, locked: true, reason: "locked");
            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);
            // The heartbeat keeps painting the unlocked state long after the session ended.
            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);
            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);

            Assert.Equal("mine", c.ToolTip);
        });
    }

    /// <summary>
    /// TRAP 2, found by play-test. Tooltips written with the loc:Str markup extension are BINDINGS,
    /// so ReadLocalValue returns a BindingExpression - and storing one in another DependencyProperty
    /// throws "Cannot set Expression. It is marked as 'NonShareable' and has already been used".
    /// That exception aborted the entire lock paint part-way through, leaving later surfaces
    /// unlocked. Locking must survive a bound tooltip, and unlocking must put the binding back.
    /// </summary>
    [Fact]
    public void HandlesABoundTooltipWithoutThrowingAndRestoresTheBinding()
    {
        OnSta(() =>
        {
            var c = new CheckBox
            {
                DataContext = new TooltipSource { Text = "localized help" }
            };
            c.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(TooltipSource.Text)));

            // Forcing the expression to materialize is what made the original code throw.
            Assert.Equal("localized help", c.ToolTip);

            SessionLock.ApplyLockToolTip(c, locked: true, reason: "locked");
            Assert.Equal("locked", c.ToolTip);

            SessionLock.ApplyLockToolTip(c, locked: false, reason: null);

            // Back to a live binding, not a frozen snapshot: changing the source must flow through.
            Assert.NotNull(BindingOperations.GetBindingBase(c, FrameworkElement.ToolTipProperty));
            Assert.Equal("localized help", c.ToolTip);
        });
    }

    private sealed class TooltipSource
    {
        public string Text { get; set; } = "";
    }
}
