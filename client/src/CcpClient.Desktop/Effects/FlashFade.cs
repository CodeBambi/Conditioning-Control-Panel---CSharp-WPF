using CcpClient.Desktop.Overlay;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// <b>The flash fade, as a law.</b> How fast a flash's opacity ramps, how often the ramp is
/// stepped, what a stalled step is allowed to do, and where the ramp ends. The MECHANICS — the
/// timers, the surfaces, the teardown — are <see cref="FlashSurfacePresenter"/>'s, which owns the
/// clock and the pool; what is here is the arithmetic, so the envelope can be pinned without a
/// window.
///
/// <para><b>Why this exists at all: a monitor-sized white rectangle appearing in ONE frame is what
/// "the screen turns white" looks like.</b> The port had no fade anywhere in the flash path and
/// snapped its layered window straight to full alpha, while upstream ramps every flash in and out.
/// Same peak brightness — nothing here dims anything — and a far harsher onset. Measured headed
/// (2026-08-25): Flash Images alone at its clamped dials put 80.65 % of a 2880x1800 desktop into
/// near-white against a 0.003 baseline, from a surface at <c>LWA_ALPHA</c> 255 whose own buffer was
/// 97 % near-white. That the pixels are white is the user's own GIF and is faithful; that they
/// arrive in one frame was not.</para>
///
/// <para><b>UPSTREAM'S ENVELOPE, read out of its heartbeat and not out of its documentation.</b>
/// A flash window is shown at opacity ZERO — <c>Services/Flash/FlashService.cs:1505</c> for the
/// per-window arm, <c>:1461</c> for the shared-host arm, <c>:3624</c> for the pooled shell — and a
/// render heartbeat walks every live window on every frame, stepping its opacity TOWARD a target by
/// <c>FADE_PER_SEC * dt</c> (<c>:2073</c>, the two-armed ramp at <c>:2108-2118</c>). The target is
/// the opacity dial while the window is alive and ZERO once it is not (<c>:2105</c>), and a window
/// whose downward ramp reaches zero is removed and closed (<c>:2117-2123</c>).</para>
///
/// <para><b>The RATE is the constant, so the DURATION is not.</b> <see cref="RatePerSecond"/> is
/// opacity units per second, applied to a target of <c>FlashOpacity / 100.0</c> (<c>:2072</c>) — so
/// at the top of the opacity dial a flash takes 1.0 / 2.4 = about 0.42 s to arrive and the same to
/// leave, and at the dial's 10 % floor it takes 0.1 / 2.4 = about 42 ms. A fade written as a fixed
/// DURATION would be a different behaviour at every dial position but one.</para>
///
/// <para><b>In and out are symmetric.</b> One constant, both arms, no easing: upstream's ramp is
/// linear in both directions and the only asymmetry in the whole envelope is WHERE each arm sits
/// relative to the lifetime.</para>
///
/// <para><b>The fade-in is INSIDE the lifetime and the fade-out is BEYOND it.</b> A window's
/// deadline is <c>lifetimeMs = duration * 1000 + 1000</c> from the spawn (<c>:1073</c>, armed as
/// both <c>ExpiresAt</c> at <c>:1250</c> and a <c>CancelAfter</c> at <c>:1185</c>), and the
/// downward ramp only STARTS there. So a flash is on screen for its lifetime plus one ramp, and the
/// duration dial moves the hold rather than the ramps.</para>
///
/// <para><b>A retirement that is not the lifetime's does not fade.</b> Upstream's heartbeat catches
/// any per-window exception and removes that window immediately (<c>:2142-2146</c>), and
/// <c>Stop()</c> takes the heartbeat down and closes every live window outright
/// (<c>:372-376</c>, <c>CloseAllWindows</c> at <c>:3879-3897</c>). A session that stops mid-fade
/// leaves nothing on screen, and no failure path leaves a surface up.</para>
/// </summary>
public static class FlashFade
{
    /// <summary>
    /// Upstream's <c>FADE_PER_SEC</c> (<c>Services/Flash/FlashService.cs:2018</c>): opacity units
    /// per second, in both directions. Its own comment records where the number came from — "Old
    /// 33ms-tick fade step was 0.08/tick — same speed, expressed per second".
    /// </summary>
    public const double RatePerSecond = 2.4;

    /// <summary>
    /// How often the ramp is stepped. Upstream rides <c>CompositionTarget.Rendering</c>, the
    /// composition clock, deliberately rather than a 33 ms dispatcher timer whose "OS-quantized
    /// cadence beats against the display refresh" (<c>:2021-2033</c>, <c>:316-318</c>) — so this is
    /// one display refresh at 60 Hz, taken on the session clock because this port has no render
    /// heartbeat and every timed behaviour in this rack rides that clock.
    ///
    /// <para><b>The cadence is not the envelope.</b> Each step is <see cref="StepFor"/> of the time
    /// that really elapsed, exactly as upstream computes a true delta from the render clock
    /// (<c>:2050-2063</c>), so a timer that fires late or coarsely produces the same 0.42 s ramp in
    /// fewer, larger steps rather than a slower fade.</para>
    /// </summary>
    public static readonly TimeSpan Cadence = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// The most time one step may be credited with, in seconds — upstream's own stall clamp
    /// (<c>:2060</c>: <c>if (dt &gt; 0.1) dt = 0.1;</c>, under a comment saying it is there "so
    /// fades can't jump"). A machine that stalls for a second resumes its ramp rather than
    /// completing it in one frame, which is the difference between a fade and the snap this whole
    /// file exists to remove.
    /// </summary>
    public const double MaximumStepSeconds = 0.1;

    /// <summary>
    /// The opacity a fading flash is PRESENTED at, before its first step.
    ///
    /// <para><b>Upstream shows the window at zero and this cannot</b>, and the difference is
    /// deliberate rather than an approximation: <see cref="OverlaySurfaceRequest"/> refuses opacity
    /// zero outright, because a layered window the OS agrees is visible while it holds no alpha is
    /// the exact ghost this port's overlay capability exists to make impossible. So a flash starts
    /// at the smallest alpha the OS will hold —
    /// <see cref="OverlaySurfaceRequest.MinimumAlpha"/> of 255, four tenths of one percent, 1.6 ms
    /// into a full ramp — and the ramp's floor is the same value for the same reason: a surface
    /// that has ramped down to it is WITHDRAWN, which is what upstream does when its ramp reaches
    /// zero (<c>Services/Flash/FlashService.cs:2117-2123</c>).</para>
    /// </summary>
    public const double OnsetOpacity = OverlaySurfaceRequest.MinimumAlpha / 255.0;

    /// <summary>
    /// How much opacity one step moves for <paramref name="elapsed"/> real time:
    /// <see cref="RatePerSecond"/> times the elapsed seconds, clamped at
    /// <see cref="MaximumStepSeconds"/> — upstream's <c>fadeStep</c> (<c>:2073</c>) over its
    /// clamped delta (<c>:2060</c>).
    ///
    /// <para>Zero or negative time gives zero or negative, and a caller does nothing with it:
    /// upstream returns from the whole tick on a non-positive delta (<c>:2057</c>).</para>
    /// </summary>
    public static double StepFor(TimeSpan elapsed) =>
        RatePerSecond * Math.Min(elapsed.TotalSeconds, MaximumStepSeconds);
}
