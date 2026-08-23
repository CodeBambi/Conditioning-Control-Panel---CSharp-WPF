using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The rebuild rule against a REAL window on a REAL desktop.</b>
///
/// <para><see cref="OverlayTopmostRebuildTests"/> drives the rule through an injected read-back, which
/// proves the arithmetic and nothing about the operating system. These two facts close that gap: a
/// real <see cref="Win32OverlayPresence"/> window is put in the topmost band, DEMOTED out of it the
/// way another process winning the same adjudication demotes it
/// (<c>SetWindowPos(HWND_NOTOPMOST)</c> — a style write does not take, which
/// <see cref="OverlayWindowProbe.DemoteFromTopmost"/> records), and the product's own read-back is
/// asked what it thinks.</para>
///
/// <para><b>Why the read-back and not the return value.</b> <c>SetWindowPos(HWND_TOPMOST)</c> returns
/// TRUE and quietly declines to apply <c>WS_EX_TOPMOST</c> when the process holds no
/// <c>SetForegroundWindow</c> permission — measured in this repository, and the reason
/// <see cref="RealDesktopWindowFloor"/> keeps a hidden window alive on every thread that runs a fact
/// in this collection. A rebuild loop built on the return value would rebuild forever and learn
/// nothing.</para>
///
/// <para><b>What these do NOT prove.</b> That a human saw a tint disappear and come back: composited
/// pixels depend on DWM, exclusive-fullscreen applications, Magnifier, RDP and mirror drivers, and
/// every query here can answer yes while a screen shows nothing. Sustained contention over minutes,
/// multi-monitor, and every part of Linux, where there is no overlay at all. Those are headed claims
/// with a named manual gate.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class OverlayTopmostRebuildObservations : RealDesktopFacts
{
    private static readonly OverlayBounds Bounds = new(160, 160, 240, 180);

    [Fact]
    public void TheReadBackFollowsTheOS_ThroughALossAndBackAgain()
    {
        using var overlay = new Win32OverlayPresence();
        var present = overlay.Present(new OverlaySurfaceRequest(Bounds, 0.6, ClickThrough: true));

        if (!OverlayWindowProbe.MachineHasInteractiveDesktop)
        {
            // No desktop: the backend refuses, and a refusal is what the read-back is asked about
            // rather than skipped over. Nothing may report the band as HELD here.
            Assert.IsNotType<CapabilityState.Available>(present);
            Assert.NotEqual(true, OverlaySurfaceSet.TopmostHeldByOs(overlay));
            return;
        }

        Assert.IsType<CapabilityState.Available>(present);
        var window = overlay.NativeHandles.Window;

        // Held, and the probe agrees with the product read-back — two independent reads of the same
        // OS state, so a detector that had degenerated into a constant is visible either way.
        Assert.True(OverlaySurfaceSet.TopmostHeldByOs(overlay));
        Assert.True((OverlayWindowProbe.ExStyleOf(window) & OverlayWindowProbe.TopmostBit) != 0);

        // Another process wins the adjudication.
        OverlayWindowProbe.DemoteFromTopmost(window);
        Assert.True((OverlayWindowProbe.ExStyleOf(window) & OverlayWindowProbe.TopmostBit) == 0,
            "the demotion did not take, so this fact would prove nothing");

        // THE DETECTOR NOTICES. This is the whole mechanism: nothing this process did changed, no
        // call returned false, and the answer moved because the OS's answer moved.
        Assert.False(OverlaySurfaceSet.TopmostHeldByOs(overlay));

        // And it comes back when the band does, so the rule's reset arm is reachable on a real
        // desktop rather than only in arithmetic.
        Assert.IsType<CapabilityState.Available>(
            overlay.Present(new OverlaySurfaceRequest(Bounds, 0.6, ClickThrough: true)));
        Assert.True(OverlaySurfaceSet.TopmostHeldByOs(overlay));
    }

    [Fact]
    public void ThreeSecondsOfRealTopmostLoss_RebuildsTheSurfaceAndWinsTheBandBack()
    {
        using var overlay = new Win32OverlayPresence();
        var clock = new ManualClock();
        var request = new OverlaySurfaceRequest(Bounds, 0.6, ClickThrough: true);
        var frame = OverlayFrame.Solid(Bounds.Width, Bounds.Height, 0x40, 0x20, 0x80);

        OverlaySurfaceSet? set = null;
        OverlaySurfaceSet.Slot? slot = null;
        var rebuilds = 0;

        // The consumer's half of the rule, in miniature: a rebuild is a re-PLACE of what this caller
        // still holds — which is exactly what PinkFilterSurfacePresenter.Rebuild and
        // SpiralSurfacePresenter.Rebuild do with their tint and their clip.
        void Rebuild()
        {
            rebuilds++;
            if (slot is not null)
            {
                set!.Place(slot, request, frame, lifetime: null);
            }
        }

        // NO topmostHeld seam: this runs on the PRODUCT read-back, over a real window.
        set = new OverlaySurfaceSet(
            clock, action => action(), () => overlay, maxSurfaces: 1,
            topmostCadence: TimeSpan.FromSeconds(5), rebuild: Rebuild);

        try
        {
            slot = set.Acquire();
            Assert.NotNull(slot);
            var placed = set.Place(slot, request, frame, lifetime: null);

            if (!OverlayWindowProbe.MachineHasInteractiveDesktop)
            {
                // No desktop, no surface, no reconcile loop, and no rebuild spent on a band nobody
                // can hold. Asserted rather than skipped.
                Assert.False(placed);
                clock.Advance(TimeSpan.FromSeconds(30));
                Assert.Equal(0, rebuilds);
                return;
            }

            Assert.True(placed);
            var window = overlay.NativeHandles.Window;

            // A CONTENDER THAT KEEPS WINNING. The demotion is re-applied before every tick, so the
            // loop's own cheap re-assertion cannot end the loss and the escalation is the only way
            // out — which is the state a screen recorder, a chat overlay or a game bar produces, and
            // the state a REFUSED band produces permanently.
            //
            // Six ticks is upstream's three seconds (OverlayService.cs:681-682) and the streak is
            // asserted to be INCOMPLETE for the first five: a rule that rebuilt on the first lost
            // tick would fire here and would tear the window down on every flicker.
            for (var i = 0; i < OverlaySurfaceSet.LossTicksBeforeRebuild; i++)
            {
                OverlayWindowProbe.DemoteFromTopmost(overlay.NativeHandles.Window);
                Assert.True(
                    (OverlayWindowProbe.ExStyleOf(overlay.NativeHandles.Window)
                        & OverlayWindowProbe.TopmostBit) == 0,
                    $"the demotion did not take on tick {i}, so this fact would prove nothing");
                Assert.False(OverlaySurfaceSet.TopmostHeldByOs(overlay));

                clock.Advance(OverlaySurfaceSet.ReconcileCadence);

                if (i < OverlaySurfaceSet.LossTicksBeforeRebuild - 1)
                {
                    Assert.Equal(0, rebuilds);
                }
            }

            Assert.Equal(1, rebuilds);

            // AND THE REBUILD WORKED. Present is the call that earns the fact — it re-reads the
            // extended style, the z-order and the hit test from the OS — so this is the OS saying
            // the surface is back in the band, not this process saying it asked.
            Assert.IsType<CapabilityState.Available>(set.LastPresent);
            Assert.True(OverlaySurfaceSet.TopmostHeldByOs(overlay));
            Assert.Equal(1, set.LiveSurfaces);
        }
        finally
        {
            set?.Dispose();
        }
    }

    /// <summary>The manual clock, in the shape every module test shares. Zero wall-clock — the three
    /// seconds above are three seconds of an injected clock, and this fact runs in microseconds.</summary>
    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(ManualClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
