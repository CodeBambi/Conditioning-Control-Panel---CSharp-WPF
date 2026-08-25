using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// POSSESSION.md's first hard rule: "Undo restores the control EXACTLY". Two ways that was not true.
///
/// <para><b>Ghost</b> stamped the opacity and hit-testing it found back as LOCAL values. A control
/// whose opacity comes from a Style setter, a trigger or a storyboard has no local value of its own,
/// and a local value outranks all three - so a toggle that dissolved once came back permanently
/// pinned, its hover and disabled states dead until the app restarted. FallEffect and
/// Ghost.NeutralTransform already did the ReadLocalValue / ClearValue dance; Ghost now does too.</para>
///
/// <para><b>TransformLease</b> marks itself released before it eases back, so a Take() during that
/// window builds a fresh lease around the old group. The old lease then landed and restored
/// RenderTransformOrigin and removed the table entry regardless - deleting the NEW lease's
/// registration, after which IsLeased lies and the next effect stacks another TransformGroup.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PossessionRestoreTests
{
    // ---- harness -------------------------------------------------------------------------------

    /// <summary>Only the ghost layer matters here; nothing under test reaches for the window.</summary>
    private sealed class StubHost : IPossessionHost
    {
        public StubHost(Canvas layer) { GhostLayer = layer; }
        public Window Window => null!;
        public Canvas GhostLayer { get; }
        public Canvas RubbleFloor { get; } = new Canvas();
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public Point PointOf(FrameworkElement element) => default;
        public bool IsUsable => true;
    }

    /// <summary>
    /// A real (never shown) top-level HWND around the tree. WPF's <c>IsVisible</c> - which
    /// Ghost.LayerBoundsOf gates on - is false for a tree that is not connected to a presentation
    /// source, so a plain Measure/Arrange host is not enough to photograph anything.
    /// </summary>
    private static void WithLiveTree(Action<StubHost, Border> body)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var layer = new Canvas { IsHitTestVisible = false };
            var victim = new Border { Width = 120, Height = 60, Background = Brushes.SlateGray };
            var root = new Grid { Width = 400, Height = 300 };
            root.Children.Add(victim);
            root.Children.Add(layer);

            var source = new HwndSource(new HwndSourceParameters("possession-restore-tests")
            {
                Width = 400,
                Height = 300,
                WindowStyle = 0x00800000   // WS_BORDER only: created, never shown
            })
            { RootVisual = root };

            try
            {
                root.Measure(new Size(400, 300));
                root.Arrange(new Rect(0, 0, 400, 300));
                root.UpdateLayout();
                body(new StubHost(layer), victim);
            }
            finally { try { source.Dispose(); } catch { } }
        });
    }

    /// <summary>Runs the dispatcher for a while, so a fire-and-forget release can actually land.</summary>
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(ms), DispatcherPriority.Background,
                                        (_, __) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    // ---- Ghost ---------------------------------------------------------------------------------

    [Fact]
    public void Ghost_restore_hands_a_styled_opacity_back_to_its_style()
    {
        WithLiveTree((host, victim) =>
        {
            var style = new Style(typeof(Border));
            style.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6));
            victim.Style = style;
            victim.UpdateLayout();
            Assert.Equal(DependencyProperty.UnsetValue, victim.ReadLocalValue(UIElement.OpacityProperty));

            var ghost = Ghost.Capture(victim, host);
            Assert.NotNull(ghost);

            ghost!.Hide();
            Assert.Equal(0, victim.Opacity, 3);

            ghost.Dispose();

            Assert.Equal(DependencyProperty.UnsetValue, victim.ReadLocalValue(UIElement.OpacityProperty));
            Assert.Equal(0.6, victim.Opacity, 3);
        });
    }

    [Fact]
    public void Ghost_restore_keeps_a_local_opacity_local()
    {
        WithLiveTree((host, victim) =>
        {
            victim.Opacity = 0.8;

            var ghost = Ghost.Capture(victim, host);
            Assert.NotNull(ghost);

            ghost!.Hide();
            ghost.Dispose();

            Assert.NotEqual(DependencyProperty.UnsetValue, victim.ReadLocalValue(UIElement.OpacityProperty));
            Assert.Equal(0.8, victim.Opacity, 3);
        });
    }

    /// <summary>Hide() without the hit-test flag must not touch hit-testing at all - the point of an
    /// opacity-only ghost is that the toggle you are watching turn to ash still toggles.</summary>
    [Fact]
    public void Ghost_restore_does_not_touch_hit_testing_it_never_changed()
    {
        WithLiveTree((host, victim) =>
        {
            var ghost = Ghost.Capture(victim, host);
            Assert.NotNull(ghost);

            ghost!.Hide();
            ghost.Dispose();

            Assert.Equal(DependencyProperty.UnsetValue, victim.ReadLocalValue(UIElement.IsHitTestVisibleProperty));
            Assert.True(victim.IsHitTestVisible);
        });
    }

    [Fact]
    public void Ghost_restore_clears_the_hit_testing_it_turned_off()
    {
        WithLiveTree((host, victim) =>
        {
            var ghost = Ghost.Capture(victim, host);
            Assert.NotNull(ghost);

            ghost!.Hide(alsoDisableHitTesting: true);
            Assert.False(victim.IsHitTestVisible);

            ghost.Dispose();

            Assert.Equal(DependencyProperty.UnsetValue, victim.ReadLocalValue(UIElement.IsHitTestVisibleProperty));
            Assert.True(victim.IsHitTestVisible);
        });
    }

    // ---- TransformLease -------------------------------------------------------------------------

    [Fact]
    public void A_lease_gives_the_element_back_with_no_local_transform()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var el = new Border();
            var lease = TransformLease.Take(el);
            Assert.NotNull(lease);
            lease!.SetOrigin(new Point(0.5, 0.5));

            lease.ReleaseImmediate();

            Assert.Equal(DependencyProperty.UnsetValue, el.ReadLocalValue(UIElement.RenderTransformProperty));
            Assert.Equal(DependencyProperty.UnsetValue, el.ReadLocalValue(UIElement.RenderTransformOriginProperty));
            Assert.False(TransformLease.IsLeased(el));
        });
    }

    /// <summary>Two effects on one control share one wrapper; the crash-safe path must drop its own
    /// reference and leave the other one standing, not snap the live effect home.</summary>
    [Fact]
    public void ReleaseImmediate_respects_a_second_holder()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var el = new Border();
            var first = TransformLease.Take(el);
            var second = TransformLease.Take(el);
            Assert.Same(first, second);

            first!.ReleaseImmediate();

            Assert.False(first.IsReleased);
            Assert.True(TransformLease.IsLeased(el));

            second!.ReleaseImmediate();

            Assert.True(first.IsReleased);
            Assert.False(TransformLease.IsLeased(el));
        });
    }

    /// <summary>The release race: a scene releases over 150-600 ms, another beat takes the same
    /// control in that window, and the old lease lands afterwards. It must not delete the new lease's
    /// registration (IsLeased would then lie and the next effect would stack a second group).</summary>
    [Fact]
    public void A_superseded_lease_leaves_the_new_one_registered()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var el = new Border();
            var old = TransformLease.Take(el);
            Assert.NotNull(old);
            old!.SetOrigin(new Point(1, 0));

            old.Release(TimeSpan.FromMilliseconds(60));   // fire and forget, as the scenes do

            var fresh = TransformLease.Take(el);
            Assert.NotNull(fresh);
            Assert.NotSame(old, fresh);
            fresh!.SetOrigin(new Point(0.5, 0.5));

            Pump(400);   // let the old lease's ease-back finish and its restore run

            Assert.True(old.IsReleased);
            Assert.False(fresh.IsReleased);
            Assert.True(TransformLease.IsLeased(el));
            Assert.Same(fresh, TransformLease.Take(el));          // joins, does not stack
            Assert.Equal(new Point(0.5, 0.5), el.RenderTransformOrigin);

            fresh.ReleaseImmediate();
            fresh.ReleaseImmediate();
            Assert.False(TransformLease.IsLeased(el));
        });
    }
}
