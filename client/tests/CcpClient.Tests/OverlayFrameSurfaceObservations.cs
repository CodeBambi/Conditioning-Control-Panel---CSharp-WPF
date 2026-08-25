using System.Diagnostics;
using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;

namespace CcpClient.Tests;

/// <summary>
/// What a flash's worth of overlay surfaces costs the process, and what it gives back when the
/// surfaces leave the screen.
///
/// <para><b>Why this is measured rather than argued.</b> Every frame a surface holds is TWO 32-bpp
/// DIB sections sized to the frame (<c>Overlay/Win32OverlayPresence.cs:798-801</c>) — the retained
/// frame and the independent read-back the content confirmation compares against. They are GDI
/// objects and process-private commit, and neither is visible to the managed heap, so a retention
/// here is invisible to every ordinary .NET instrument. Arithmetic over the frame size predicts a
/// number; only the process can say what it is really holding.</para>
///
/// <para><b>The two arms are the control.</b> <see cref="FlashGeometry.Size"/> fits a source image
/// into a 40 % box and then multiplies by the user's scale dial
/// (<c>Effects/FlashGeometry.cs:54-61</c>), so the same source at the dial's maximum (250 %) covers
/// the whole monitor and at its default (100 %) covers 16 % of it. If retention of the frame
/// surfaces is what a session pays for, the two arms must diverge by roughly that ratio; if both
/// arms are flat the hypothesis is wrong. Each arm is run twice so the noise floor is visible next
/// to the signal.</para>
///
/// <para><b>What this cannot say.</b> Nothing here is a frame-cadence, CPU or GPU measurement, and
/// nothing here proves a human saw a flash. It measures one process's GDI object count and its
/// private commit around a real place/withdraw cycle on a real desktop.</para>
/// </summary>
internal static class OverlayFrameSurfaceObservations
{
    /// <summary>The flash pool's cap — one surface per image, ten of them
    /// (<c>Effects/FlashSurfacePresenter.cs:88</c>). The measurement drives the real pool size
    /// rather than a convenient one, because the retained bytes are per PRESENCE.</summary>
    internal const int PoolSize = FlashSurfacePresenter.MaxConcurrentSurfaces;

    /// <summary>Opacity is irrelevant to the frame surfaces and is held fixed across both arms.</summary>
    private const double Opacity = 0.6;

    private const uint GrGdiObjects = 0;

    private static readonly Lazy<IReadOnlyList<Cycle>> LazyCycles =
        new(RunAllArms, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Both arms, twice each, run once per suite execution and cached: each pass puts ten
    /// real windows on the user's real screen.</summary>
    internal static IReadOnlyList<Cycle> Cycles => LazyCycles.Value;

    /// <summary>The maximum-settings arm: the image scale dial at its ceiling
    /// (<c>Session/VisualsPresetDocument.cs:65</c>), which is the configuration
    /// <c>client/port.txt</c> names as the performance contract.</summary>
    internal static IReadOnlyList<Cycle> AtMonitorScale =>
        [.. Cycles.Where(c => c.ScalePercent == VisualsPresetDocument.MaxImageScalePercent)];

    /// <summary>The control arm: the same source image at the dial's default
    /// (<c>Session/VisualsPresetDocument.cs:58</c>).</summary>
    internal static IReadOnlyList<Cycle> AtDefaultScale =>
        [.. Cycles.Where(c => c.ScalePercent == VisualsPresetDocument.DefaultImageScalePercent)];

    /// <summary>Every arm's line, for a failure message or a report.</summary>
    internal static string DescribeAll() => string.Join("\n", Cycles.Select(c => c.Describe()));

    private static IReadOnlyList<Cycle> RunAllArms()
    {
        var cycles = new List<Cycle>();
        foreach (var scale in new[]
                 {
                     VisualsPresetDocument.MaxImageScalePercent,
                     VisualsPresetDocument.DefaultImageScalePercent,
                 })
        {
            for (var pass = 1; pass <= 2; pass++)
            {
                cycles.Add(RunCycle(scale, pass));
            }
        }

        return cycles;
    }

    /// <summary>
    /// One arm, one pass. The phases are separate on purpose: the baseline is taken with every
    /// window ALREADY up and nothing painted, so the delta that follows is the frame surfaces and
    /// only the frame surfaces — a baseline taken before the windows existed would fold the
    /// windows' own cost into the number and could not tell the two apart.
    /// </summary>
    private static Cycle RunCycle(int scalePercent, int pass)
    {
        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
        var machine = OverlayWindowProbe.MachineHasInteractiveDesktop;
        if (!machine || screenWidth <= 0 || screenHeight <= 0)
        {
            return new Cycle(scalePercent, pass, 0, 0, machine, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, "this session has no interactive desktop with a display on it");
        }

        // The product's own geometry, over a source image half the monitor's size on each axis: at
        // the 250 % ceiling that fits to the whole monitor, and at 100 % to 40 % of each axis. The
        // sizes are the product's arithmetic, never this file's.
        var (frameWidth, frameHeight) = FlashGeometry.Size(
            screenWidth / 2, screenHeight / 2, screenWidth, screenHeight, scalePercent);
        var bounds = new OverlayBounds(0, 0, frameWidth, frameHeight);
        var request = new OverlaySurfaceRequest(bounds, Opacity, ClickThrough: true);
        var frame = OverlayFrame.Solid(frameWidth, frameHeight, 40, 20, 200);

        var presences = new Win32OverlayPresence[PoolSize];
        for (var i = 0; i < presences.Length; i++)
        {
            presences[i] = new Win32OverlayPresence();
        }

        string? firstRefusal = null;
        void Note(CapabilityState state, string phase, int index)
        {
            if (state is not CapabilityState.Available && firstRefusal is null)
            {
                firstRefusal = $"{phase} #{index}: {OverlayObservations.Describe(state)}";
            }
        }

        try
        {
            // Phase 1 — every window on screen, nothing painted. This is the baseline.
            var presented = 0;
            for (var i = 0; i < presences.Length; i++)
            {
                var state = presences[i].Present(request);
                Note(state, "present", i);
                if (state is CapabilityState.Available)
                {
                    presented++;
                }
            }

            var (gdiPresent, privatePresent, workingPresent) = Sample();

            // Phase 2 — every surface holds the frame. Each presence is re-presented immediately
            // before its paint: Present re-asserts the topmost band, so the surface whose content is
            // read back is the one on top rather than one buried under the nine placed after it.
            // That is the product's order too — present, then paint, per surface
            // (Effects/OverlaySurfaceSet.cs:279-294).
            var painted = 0;
            for (var i = 0; i < presences.Length; i++)
            {
                presences[i].Present(request);
                var state = presences[i].Paint(frame);
                Note(state, "paint", i);
                if (state is CapabilityState.Available)
                {
                    painted++;
                }
            }

            var (gdiPaint, privatePaint, workingPaint) = Sample();

            // Phase 3 — every surface off screen again. Nothing is disposed: this is the pooled
            // state a session sits in between flashes, which is the whole question.
            var withdrawn = 0;
            for (var i = 0; i < presences.Length; i++)
            {
                var state = presences[i].Withdraw();
                Note(state, "withdraw", i);
                if (state is CapabilityState.Available)
                {
                    withdrawn++;
                }
            }

            var (gdiWithdraw, privateWithdraw, workingWithdraw) = Sample();

            for (var i = 0; i < presences.Length; i++)
            {
                presences[i].Dispose();
            }

            var (gdiDispose, privateDispose, workingDispose) = Sample();

            return new Cycle(
                scalePercent, pass, frameWidth, frameHeight, machine, presented, painted, withdrawn,
                gdiPresent, gdiPaint, gdiWithdraw, gdiDispose,
                privatePresent, privatePaint, privateWithdraw, privateDispose,
                workingPresent, workingPaint, workingWithdraw, workingDispose,
                firstRefusal);
        }
        finally
        {
            for (var i = 0; i < presences.Length; i++)
            {
                presences[i].Dispose();
            }

            GC.KeepAlive(frame);
        }
    }

    /// <summary>
    /// One sample of what the OPERATING SYSTEM says this process holds. The managed heap is
    /// collected first so a pending frame buffer cannot be mistaken for a native retention — the
    /// buffers here are tens of megabytes each and would otherwise sit in the same numbers.
    /// </summary>
    private static (long Gdi, long Private, long Working) Sample()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return (GdiObjects(), process.PrivateMemorySize64, process.WorkingSet64);
    }

    /// <summary>
    /// GDI objects this process holds, from the OS — the same counter the shipping product samples
    /// in its own resource watchdog (<c>Services/UI/UiHangWatchdog.cs:195-196</c>). A DIB section
    /// and a memory device context are each one of these, and the quota is process-wide and finite,
    /// which is why the count is the instrument rather than bytes alone.
    /// </summary>
    private static long GdiObjects() =>
        OperatingSystem.IsWindows() ? GetGuiResources(GetCurrentProcess(), GrGdiObjects) : -1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(nint process, uint flags);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    /// <summary>
    /// One place/withdraw cycle over a whole flash pool's worth of presences.
    /// </summary>
    /// <param name="ScalePercent">The image scale dial this arm ran at.</param>
    /// <param name="Pass">1 or 2 — the same arm run twice, so its own noise is visible.</param>
    /// <param name="FrameWidth">The frame size the product's own geometry produced.</param>
    /// <param name="FrameHeight">Ditto.</param>
    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation compares against.</param>
    /// <param name="Presented">How many of <see cref="PoolSize"/> presences the OS confirmed on screen.</param>
    /// <param name="Painted">How many of them the OS confirmed holding the frame.</param>
    /// <param name="Withdrawn">How many of them the OS confirmed off screen again.</param>
    /// <param name="GdiAfterPresent">GDI objects held with every window up and NOTHING painted yet.</param>
    /// <param name="GdiAfterPaint">GDI objects held once every surface holds its frame.</param>
    /// <param name="GdiAfterWithdraw">GDI objects held once every surface is off screen.</param>
    /// <param name="GdiAfterDispose">GDI objects held once every presence is disposed.</param>
    /// <param name="PrivateAfterPresent">Private commit, in bytes, at the same four points.</param>
    /// <param name="PrivateAfterPaint">Ditto.</param>
    /// <param name="PrivateAfterWithdraw">Ditto.</param>
    /// <param name="PrivateAfterDispose">Ditto.</param>
    /// <param name="WorkingAfterPresent">Working set, in bytes, at the same four points.</param>
    /// <param name="WorkingAfterPaint">Ditto.</param>
    /// <param name="WorkingAfterWithdraw">Ditto.</param>
    /// <param name="WorkingAfterDispose">Ditto.</param>
    /// <param name="FirstRefusal">The first non-Available outcome of the pass, for failure messages.</param>
    internal sealed record Cycle(
        int ScalePercent,
        int Pass,
        int FrameWidth,
        int FrameHeight,
        bool MachineHasInteractiveDesktop,
        int Presented,
        int Painted,
        int Withdrawn,
        long GdiAfterPresent,
        long GdiAfterPaint,
        long GdiAfterWithdraw,
        long GdiAfterDispose,
        long PrivateAfterPresent,
        long PrivateAfterPaint,
        long PrivateAfterWithdraw,
        long PrivateAfterDispose,
        long WorkingAfterPresent,
        long WorkingAfterPaint,
        long WorkingAfterWithdraw,
        long WorkingAfterDispose,
        string? FirstRefusal)
    {
        /// <summary>GDI objects the paints added and the withdrawals did not give back.</summary>
        internal long GdiRetainedAfterWithdraw => GdiAfterWithdraw - GdiAfterPresent;

        /// <summary>GDI objects the paints added.</summary>
        internal long GdiTakenByPaint => GdiAfterPaint - GdiAfterPresent;

        /// <summary>Private commit the paints added and the withdrawals did not give back.</summary>
        internal long PrivateRetainedAfterWithdraw => PrivateAfterWithdraw - PrivateAfterPresent;

        /// <summary>Private commit the paints added.</summary>
        internal long PrivateTakenByPaint => PrivateAfterPaint - PrivateAfterPresent;

        internal string Describe() =>
            $"scale {ScalePercent}% pass {Pass}: frame {FrameWidth}x{FrameHeight}, "
            + $"presented {Presented}/{PoolSize}, painted {Painted}, withdrawn {Withdrawn}; "
            + $"GDI present={GdiAfterPresent} paint={GdiAfterPaint} withdraw={GdiAfterWithdraw} "
            + $"dispose={GdiAfterDispose} (paint took {GdiTakenByPaint}, withdraw gave back "
            + $"{GdiTakenByPaint - GdiRetainedAfterWithdraw}); private MB "
            + $"present={Mb(PrivateAfterPresent)} paint={Mb(PrivateAfterPaint)} "
            + $"withdraw={Mb(PrivateAfterWithdraw)} dispose={Mb(PrivateAfterDispose)}; working MB "
            + $"present={Mb(WorkingAfterPresent)} paint={Mb(WorkingAfterPaint)} "
            + $"withdraw={Mb(WorkingAfterWithdraw)} dispose={Mb(WorkingAfterDispose)}"
            + (FirstRefusal is null ? string.Empty : $"; first refusal: {FirstRefusal}");

        private static string Mb(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("F1");
    }
}
