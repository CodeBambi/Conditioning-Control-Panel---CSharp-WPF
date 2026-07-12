using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.FrameSourceBackends;

/// <summary>
/// X11 desktop frame capture via the MIT-SHM (shared-memory) fast path
/// (linux-framesource-contract.md §3.4, Slice C). On a local X server this avoids the
/// per-pixel socket copy of <see cref="X11BasicFrameSourceBackend"/> (XGetImage) by having
/// the server blit directly into a SysV shared-memory segment the client already maps.
/// Selected FIRST on a native X11 session where the SHM attach probe succeeds; otherwise the
/// selector falls back to <see cref="X11BasicFrameSourceBackend"/> and then to the black frame.
/// </summary>
/// <remarks>
/// <para><b>Behavioral contract</b> mirrors <c>WindowsFrameSource.cs</c> (the reference
/// IFrameSource impl) exactly as the basic backend does: captures the rect identified by
/// <c>ScreenInfo.Bounds</c> in absolute virtual-desktop pixels (<c>WindowsFrameSource.cs:25-29,34</c>),
/// returns tightly-packed 32-bit BGRA with <c>BgraData.Length == Width*Height*4</c>
/// (<c>WindowsFrameSource.cs:43-46</c>), honors cancellation before and after the blit
/// (<c>WindowsFrameSource.cs:23,37</c>), and clamps dimensions to a minimum of 1x1
/// (<c>WindowsFrameSource.cs:28-29</c>).</para>
///
/// <para><b>Row repack + alpha normalization</b> (contract §1.2 / §3.3-§3.4): the SHM segment
/// is sized to the XImage's <c>bytes_per_line * height</c>, which may carry row padding
/// (<c>bytes_per_line &gt; width*4</c>), and depth-24 visuals leave the high byte UNDEFINED.
/// Rows are copied honoring <c>bytes_per_line</c>, and on depth 24 the alpha byte of every
/// pixel is forced to 0xFF so the buffer satisfies the RawFrame tight-pack contract
/// (consumers index by Width/Height and corrupt on padded rows / garbage alpha).</para>
///
/// <para><b>Connection &amp; threading (contract §3.1):</b> owns a DEDICATED
/// <c>XOpenDisplay(null)</c> connection, never Avalonia's. Xlib is not thread-safe and
/// <c>XInitThreads</c> cannot be guaranteed to run first next to Avalonia's own X11 use, so
/// ALL access is serialized behind <see cref="_xLock"/>. <c>CaptureAsync</c> may arrive from
/// any thread (the preview loop, an OCR one-shot).</para>
///
/// <para><b>Scoped Xlib error trap (contract §3.2 — mandatory):</b> the default Xlib error
/// handler calls <c>exit()</c>. <c>XShmAttach</c> raises <c>BadAccess</c> ASYNCHRONOUSLY
/// (remote display / SHM policy) and <c>XShmGetImage</c> raises <c>BadMatch</c> when the
/// capture rect is not fully inside the drawable (monitor hot-unplug/resize between the
/// ScreenInfo snapshot and capture). Every SHM attach/get runs inside a chained
/// <see cref="XlibErrorTrap"/> scope: Reset(display) → call → XSync → GetLastErrorCode(display).
/// On a trapped error THIS call returns a per-call black frame (never an exception, never a
/// process kill). Repeated trapped errors demote the backend (contract §3.2) so later calls
/// short-circuit to black without touching X.</para>
///
/// <para><b>Probe + locality (contract §3.4):</b> <c>XShmQueryExtension</c> is necessary but
/// not sufficient — a remote (SSH-forwarded) display may report the extension present yet fail
/// the attach. The backend therefore gates on (1) extension present, (2) a local display
/// string, AND (3) a real 1x1 attach round-trip in the constructor. The selector only commits
/// to SHM when <see cref="IsAvailable"/> is true, so a failed probe silently routes to the
/// basic XGetImage backend.</para>
///
/// <para><b>Segment lifecycle (contract §3.4, lifecycle corrected):</b> the segment is created
/// ONCE per capture size and REUSED across frames (never per-frame — that would leak). The
/// segment is recreated only when <c>ScreenInfo.Bounds</c> size changes. <c>shmget</c> uses
/// mode 0600 (owner-only; the draft's 0777 exposed screen pixels world-read/write —
/// corrected). <c>IPC_RMID</c> is marked IMMEDIATELY after the server attach is confirmed, so
/// the kernel reclaims the segment even if the process dies; the segment lives until BOTH
/// server and client detach (refcount-gated). On dispose / size change / trapped capture
/// error, the backend detaches the server, unmaps the client, destroys the image, and frees
/// the shminfo block — every resource released, no leak (verified clean by the contract's
/// <c>ipcs -m</c> acceptance test).</para>
///
/// <para><b>Privacy hard-line (contract §1.4 — same class as webcam frames):</b> captured
/// frames are MEMORY-ONLY. They are never written to disk, never sent over the network, never
/// logged (only dimensions are logged, never pixel content). The shared-memory segment holds
/// live pixels only while attached and is torn down when capture stops; it is never persisted.</para>
///
/// <para><b>No idle / background capture:</b> this backend is strictly PULL-BASED. It does
/// ZERO work unless a consumer calls <see cref="CaptureAsync"/>. There is no timer, no
/// background thread, no polling loop — start/stop is governed entirely by the consuming
/// feature's lifetime.</para>
/// </remarks>
public sealed class X11ShmFrameSourceBackend : ILinuxFrameSourceBackend
{
    /// <summary>
    /// Consecutive trapped-X-error threshold after which the backend demotes itself
    /// (contract §3.2 "if errors repeat, demote"). Mirrors X11BasicFrameSourceBackend's
    /// threshold so the two X11 backends behave identically under sustained error storms.
    /// </summary>
    private const int DemotionErrorThreshold = 3;

    /// <summary><c>shmat</c> failure sentinel: it returns <c>(void*)-1</c> on error.</summary>
    private static readonly IntPtr ShmatInvalid = new(-1);

    private readonly ILogger? _logger;
    private readonly object _xLock = new();

    private IntPtr _display;
    private bool _available;
    private bool _demoted;
    private int _consecutiveXErrors;
    private bool _disposed;

    // Current reusable segment state (contract §3.4: create once per size, reuse, recreate on
    // size change). Mutated only under _xLock. Cleared by DestroySegment.
    private bool _hasSegment;
    private IntPtr _segmentImage;
    private IntPtr _segmentShmInfo;   // stable unmanaged XShmSegmentInfo block (image->obdata)
    private IntPtr _segmentShmaddr;   // shmat() mapping (== image->data)
    private int _segmentShmid;
    private int _segmentWidth;
    private int _segmentHeight;
    private int _segmentStride;       // image->bytes_per_line (rows may be padded)
    private int _segmentDepth;
    private int _segmentByteOrder;

    public X11ShmFrameSourceBackend(ILogger<X11ShmFrameSourceBackend>? logger = null)
    {
        _logger = logger;
        ProbeDisplay();
    }

    /// <summary>Human-readable backend name for diagnostics (never frame content).</summary>
    public string Name => "X11ShmFrameSourceBackend";

    /// <summary>
    /// True only when MIT-SHM is usable on a local dedicated display (extension present,
    /// display local, 1x1 attach probe succeeded) and not demoted.
    /// </summary>
    public bool IsAvailable => _available && !_demoted;

    private void ProbeDisplay()
    {
        try
        {
            _display = X11Interop.XOpenDisplay(null);
            if (_display == IntPtr.Zero)
            {
                _logger?.LogWarning("X11ShmFrameSourceBackend: XOpenDisplay returned null (no X display)");
                _available = false;
                return;
            }

            // Install the process-kill guard BEFORE any request that could error
            // (contract §3.2; same registration pattern as X11BasicFrameSourceBackend).
            XlibErrorTrap.RegisterDisplay(_display);

            // (1) MIT-SHM must be present.
            if (!X11Interop.XShmQueryExtension(_display))
            {
                _logger?.LogWarning("X11ShmFrameSourceBackend: MIT-SHM extension not present");
                _available = false;
                FailProbe();
                return;
            }

            // (2) Cheap locality pre-filter (contract §3.4: MIT-SHM only works client+server on
            // the same machine). The attach probe below is the authoritative check; this just
            // skips the round-trip for the obvious remote/SSH-forwarded case.
            if (!IsLocalDisplay())
            {
                _logger?.LogWarning("X11ShmFrameSourceBackend: display is remote; MIT-SHM skipped");
                _available = false;
                FailProbe();
                return;
            }

            // (3) Real attach probe (contract §3.4: "the attach round-trip is the real probe —
            // on failure fall back to XGetImage silently"). A throwaway 1x1 segment is created,
            // attached, then fully torn down so the probe leaks nothing.
            if (!TryCreateAndAttachSegment(1, 1, out var probeState))
            {
                _logger?.LogWarning("X11ShmFrameSourceBackend: MIT-SHM 1x1 attach probe failed; " +
                    "the selector will fall back to the basic XGetImage backend");
                _available = false;
                FailProbe();
                return;
            }

            FreeSegmentState(probeState);

            _available = true;
            _logger?.LogInformation(
                "X11ShmFrameSourceBackend: dedicated X display open, MIT-SHM usable " +
                "(local display, attach probe ok) — shared-memory capture path");
        }
        catch (DllNotFoundException ex)
        {
            _logger?.LogWarning("X11ShmFrameSourceBackend: libXext/libX11/libc not found: {Message}", ex.Message);
            FailProbe();
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger?.LogWarning("X11ShmFrameSourceBackend: missing XShm/libc entry point: {Message}", ex.Message);
            FailProbe();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: failed to probe MIT-SHM");
            FailProbe();
        }
    }

    private void FailProbe()
    {
        _available = false;
        lock (_xLock)
        {
            DestroySegment();
            CloseDisplayLocked();
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/>.</summary>
    private void CloseDisplayLocked()
    {
        if (_display == IntPtr.Zero)
        {
            return;
        }

        XlibErrorTrap.UnregisterDisplay(_display);
        try
        {
            X11Interop.XCloseDisplay(_display);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: XCloseDisplay failed");
        }

        _display = IntPtr.Zero;
        _available = false;
    }

    /// <summary>
    /// Captures <paramref name="screen"/>. Never throws to a crash: on any failure (no
    /// display, demoted, trapped X error, cancellation) returns a black frame of the
    /// requested size (contract §1.4 + §5 consumer-degrade).
    /// </summary>
    public Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default)
    {
        // WindowsFrameSource.cs:23 — honor cancellation before the blit.
        cancellationToken.ThrowIfCancellationRequested();

        // WindowsFrameSource.cs:25-29,34 + 28-29 — capture Bounds in absolute virtual-desktop
        // pixels, clamped to a minimum 1x1 dimension.
        var bounds = screen.Bounds;
        var reqW = Math.Max(1, (int)bounds.Width);
        var reqH = Math.Max(1, (int)bounds.Height);

        if (_disposed || !_available || _display == IntPtr.Zero || _demoted)
        {
            return Task.FromResult(BlackFrame(reqW, reqH));
        }

        RawFrame frame;
        lock (_xLock)
        {
            if (_disposed || _display == IntPtr.Zero || _demoted || !_available)
            {
                frame = BlackFrame(reqW, reqH);
            }
            else
            {
                frame = CaptureLocked(bounds.X, bounds.Y, reqW, reqH);
            }
        }

        // WindowsFrameSource.cs:37 — honor cancellation after the blit.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(frame);
    }

    /// <summary>Must be called under <see cref="_xLock"/> with the display live. Never throws.</summary>
    private RawFrame CaptureLocked(double boundsX, double boundsY, int reqW, int reqH)
    {
        // Output is the REQUESTED size; on any fault it stays opaque black — dimension-stable
        // across hot-plug races (consumers index by RawFrame.Width/Height; contract §1.2).
        var output = new byte[reqW * reqH * 4];

        try
        {
            var root = X11Interop.XDefaultRootWindow(_display);
            if (root == IntPtr.Zero)
            {
                _logger?.LogWarning("X11ShmFrameSourceBackend: XDefaultRootWindow returned zero");
                return ForceAlphaOpaque(output, reqW, reqH, force: true);
            }

            // Contract §3.5: defensively validate the rect against the root window geometry.
            // The X root spans the whole virtual desktop, so a stable ScreenInfo.Bounds is fully
            // inside root. SHM captures a fixed-size image at a fixed offset, so unlike the
            // basic backend (which clamps+pads partial rects) the SHM path requires the full
            // rect in-bounds; the rare hot-plug race degrades to a per-call black frame
            // (contract §5 — never crash, black-frame on any failure) until ScreenInfo refreshes.
            if (!TryGetRootGeometry(root, out int rootW, out int rootH))
            {
                return ForceAlphaOpaque(output, reqW, reqH, force: true);
            }

            int reqX = (int)boundsX;
            int reqY = (int)boundsY;
            if (reqX < 0 || reqY < 0 || reqX + reqW > rootW || reqY + reqH > rootH)
            {
                // Partially/fully off-root (monitor gone between snapshot and capture): black frame.
                return ForceAlphaOpaque(output, reqW, reqH, force: true);
            }

            // Contract §3.4: create the segment ONCE per size, reuse across frames, recreate on
            // size change. A size change or missing segment rebuilds it (full teardown first).
            if (!_hasSegment || _segmentWidth != reqW || _segmentHeight != reqH)
            {
                DestroySegment();
                if (!TryCreateAndAttachSegment(reqW, reqH, out var created))
                {
                    RecordXError(0, "XShmCreateImage/Attach");
                    return ForceAlphaOpaque(output, reqW, reqH, force: true);
                }

                _segmentImage = created.Image;
                _segmentShmInfo = created.ShmInfo;
                _segmentShmaddr = created.Shmaddr;
                _segmentShmid = created.Shmid;
                _segmentWidth = created.Width;
                _segmentHeight = created.Height;
                _segmentStride = created.Stride;
                _segmentDepth = created.Depth;
                _segmentByteOrder = created.ByteOrder;
                _hasSegment = true;
            }

            // Scoped Xlib error trap (contract §3.2): Reset → XShmGetImage → XSync → check.
            XlibErrorTrap.Reset(_display);
            bool ok = X11Interop.XShmGetImage(_display, root, _segmentImage, reqX, reqY, X11Interop.AllPlanes);
            X11Interop.XSync(_display, false);
            int err = XlibErrorTrap.GetLastErrorCode(_display);

            if (!ok || err != 0)
            {
                RecordXError(err, "XShmGetImage");
                // A bad image/segment must not be reused — tear it down so the next capture
                // rebuilds it (or demotes after the error threshold).
                DestroySegment();
                return ForceAlphaOpaque(output, reqW, reqH, force: true);
            }

            _consecutiveXErrors = 0;
            return RepackSegment(reqW, reqH, output);
        }
        catch (DllNotFoundException ex)
        {
            _logger?.LogWarning("X11ShmFrameSourceBackend: native lib missing at capture: {Message}", ex.Message);
            _available = false;
            return ForceAlphaOpaque(output, reqW, reqH, force: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: capture faulted, returning black frame");
            return ForceAlphaOpaque(output, reqW, reqH, force: true);
        }
    }

    /// <summary>
    /// Creates and attaches a shared-memory segment for the given size. On success returns
    /// <c>true</c> with the fully-wired <paramref name="state"/> (server + client attached,
    /// IPC_RMID already marked). On ANY failure returns <c>false</c> and releases every
    /// partial resource (no leak — contract §3.4). NOT idempotent about the display: caller
    /// holds <see cref="_xLock"/>.
    /// </summary>
    private bool TryCreateAndAttachSegment(int w, int h, out SegmentState state)
    {
        state = default;

        int screen = X11Interop.XDefaultScreen(_display);
        IntPtr visual = X11Interop.XDefaultVisual(_display, screen);
        int depth = X11Interop.XDefaultDepth(_display, screen);
        if (visual == IntPtr.Zero)
        {
            return false;
        }

        // Stable unmanaged block — the image stores this pointer in image->obdata and
        // XShmGetImage dereferences it later, so it must not move (a GC compaction of a managed
        // copy would dangle it).
        IntPtr shmInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<X11Interop.XShmSegmentInfo>());

        bool attached = false;
        bool imageCreated = false;
        bool shmSegMade = false;
        IntPtr image = IntPtr.Zero;
        int shmid = -1;
        IntPtr shmaddr = IntPtr.Zero;
        int stride = 0;
        int byteOrder = X11Interop.LSBFirst;

        try
        {
            // XShmCreateImage stores shmInfoPtr in image->obdata; shminfo contents are read
            // later by XShmAttach, so we populate it AFTER shmget/shmat below.
            image = X11Interop.XShmCreateImage(
                _display, visual, (uint)depth, X11Interop.ZPixmap,
                IntPtr.Zero, shmInfoPtr, (uint)w, (uint)h);
            if (image == IntPtr.Zero)
            {
                return false;
            }
            imageCreated = true;

            stride = ReadInt32(image, XImageOffsets.BytesPerLine);
            byteOrder = ReadInt32(image, XImageOffsets.ByteOrder);
            if (stride < w * 4)
            {
                // Malformed image — treat as a trapped error.
                return false;
            }

            long segSize = (long)stride * h;

            // Contract §3.4 correction #3: IPC_CREAT | 0600 (owner-only; never 0777).
            shmid = X11Interop.shmget(
                X11Interop.IPC_PRIVATE, (UIntPtr)segSize, X11Interop.IPC_CREAT | X11Interop.ShmMode0600);
            if (shmid < 0)
            {
                return false;
            }
            shmSegMade = true;

            shmaddr = X11Interop.shmat(shmid, IntPtr.Zero, 0);
            if (shmaddr == ShmatInvalid || shmaddr == IntPtr.Zero)
            {
                shmaddr = IntPtr.Zero;
                return false;
            }

            // Populate the stable shminfo block (image->obdata already points at it).
            var info = new X11Interop.XShmSegmentInfo
            {
                Shmid = shmid,
                Shmaddr = shmaddr,
                ReadOnly = false
            };
            Marshal.StructureToPtr(info, shmInfoPtr, false);

            // image->data MUST equal the segment address (XShmGetImage writes there).
            Marshal.WriteIntPtr(image, XImageOffsets.Data, shmaddr);

            // Scoped attach trap (contract §3.2/§3.4): XShmAttach raises BadAccess ASYNC.
            XlibErrorTrap.Reset(_display);
            bool attachOk = X11Interop.XShmAttach(_display, shmInfoPtr);
            X11Interop.XSync(_display, false);
            int err = XlibErrorTrap.GetLastErrorCode(_display);

            if (!attachOk || err != 0)
            {
                // Remote display / SHM policy refusal — fall back silently (no leak).
                return false;
            }
            attached = true;

            // Contract §3.4 correction #4: mark the segment for removal IMMEDIATELY after the
            // server attach is confirmed. The segment lives until BOTH server and client detach
            // (refcount-gated), so this never frees a still-attached segment, but DOES reclaim
            // it if the process dies. Done exactly once here; teardown never repeats it.
            // Failure is non-fatal (the segment still works; worst case it is not auto-reclaimed
            // on abnormal exit), and it must NOT discard this good segment nor leak it — wrap it
            // so a throw cannot route through the outer catch (which would return false with
            // attached==true, skipping the finally teardown).
            try
            {
                X11Interop.shmctl(shmid, X11Interop.IPC_RMID, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: shmctl(IPC_RMID) after attach failed " +
                    "(segment still usable; will not be auto-reclaimed on abnormal exit)");
            }

            state = new SegmentState(
                image, shmInfoPtr, shmaddr, shmid, w, h, stride, depth, byteOrder);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: segment create/attach faulted");
            return false;
        }
        finally
        {
            if (!attached)
            {
                // Full cleanup of a failed attempt (contract §3.4: no leak even on error).
                if (shmaddr != IntPtr.Zero)
                {
                    TryShmdt(shmaddr);
                }
                // IPC_RMID was not yet marked on the success path (it runs AFTER attach
                // succeeds), so a failed attach must mark it now to free the kernel segment.
                if (shmSegMade)
                {
                    TryShmctlRmid(shmid);
                }
                if (imageCreated)
                {
                    TryXDestroyImage(image);
                }
                Marshal.FreeHGlobal(shmInfoPtr);
            }
        }
    }

    /// <summary>
    /// Fully releases a segment: server detach → client unmap → image destroy → shminfo free.
    /// Order matters (contract §3.4: XShmDetach → shmdt → XDestroyImage). IPC_RMID was already
    /// marked at attach time, so it is NOT repeated here (a second call is a harmless EINVAL,
    /// but the shmid may already be gone). Every call is guarded so a failing step cannot skip
    /// the rest. Must be called under <see cref="_xLock"/>.
    /// </summary>
    private void FreeSegmentState(in SegmentState st)
    {
        if (st.ShmInfo != IntPtr.Zero && st.Image != IntPtr.Zero && _display != IntPtr.Zero)
        {
            // Server detach first (flushed via XSync) so the server stops referencing the
            // segment before the client unmaps its mapping.
            try
            {
                XlibErrorTrap.Reset(_display);
                X11Interop.XShmDetach(_display, st.ShmInfo);
                X11Interop.XSync(_display, false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: XShmDetach failed during cleanup");
            }
        }

        if (st.Shmaddr != IntPtr.Zero)
        {
            TryShmdt(st.Shmaddr);
        }

        if (st.Image != IntPtr.Zero)
        {
            TryXDestroyImage(st.Image);
        }

        if (st.ShmInfo != IntPtr.Zero)
        {
            try
            {
                Marshal.FreeHGlobal(st.ShmInfo);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: FreeHGlobal(shminfo) failed");
            }
        }
    }

    /// <summary>Tears down the current cached segment (size change / dispose / capture error).</summary>
    private void DestroySegment()
    {
        if (!_hasSegment)
        {
            return;
        }

        var st = new SegmentState(
            _segmentImage, _segmentShmInfo, _segmentShmaddr, _segmentShmid,
            _segmentWidth, _segmentHeight, _segmentStride, _segmentDepth, _segmentByteOrder);

        _hasSegment = false;
        _segmentImage = IntPtr.Zero;
        _segmentShmInfo = IntPtr.Zero;
        _segmentShmaddr = IntPtr.Zero;
        _segmentShmid = 0;
        _segmentWidth = 0;
        _segmentHeight = 0;
        _segmentStride = 0;
        _segmentDepth = 0;
        _segmentByteOrder = 0;

        FreeSegmentState(st);
    }

    /// <summary>
    /// Repacks the shared-memory segment into the tightly-packed BGRA output buffer honoring
    /// <c>bytes_per_line</c> and normalizing alpha (contract §3.3-§3.4, corrected vs the naïve
    /// straight-copy). The segment image is exactly <c>(reqW, reqH)</c> and the rect is fully
    /// in-root (validated by the caller), so this is a direct per-row copy with no offset/pad.
    /// </summary>
    private RawFrame RepackSegment(int reqW, int reqH, byte[] output)
    {
        // Contract §3.3: on little-endian servers (byte_order == LSBFirst == 0, i.e. every
        // x86/ARM desktop) a standard TrueColor visual lays out as B,G,R,X in memory — matching
        // BGRA directly. Big-endian is out of scope; warn once if ever seen.
        if (_segmentByteOrder != X11Interop.LSBFirst)
        {
            _logger?.LogWarning(
                "X11ShmFrameSourceBackend: XImage byte_order == {ByteOrder} (MSBFirst); " +
                "pixel layout may be wrong (big-endian servers out of scope, contract §3.3)",
                _segmentByteOrder);
        }

        int rowBytes = reqW * 4;
        // Copy each row honoring bytes_per_line (rows may be padded) — corrected vs the draft's
        // straight Marshal.Copy of width*height*4 bytes (contract §3.3/§3.4).
        for (int row = 0; row < reqH; row++)
        {
            Marshal.Copy(
                _segmentShmaddr + (row * _segmentStride),
                output,
                row * rowBytes,
                rowBytes);
        }

        // Contract §3.3: depth-24 visuals leave the high byte UNDEFINED — force opaque alpha so
        // the buffer satisfies the RawFrame tight-pack contract. Depth-32 sources carry
        // meaningful alpha and are left as captured.
        return ForceAlphaOpaque(output, reqW, reqH, force: _segmentDepth == 24);
    }

    /// <summary>
    /// Cheap locality guard via the display connection string (contract §3.4). Local display
    /// strings start with ':' (e.g. ":0", ":0.0") or use the "unix:" abstract-path form;
    /// anything with a hostname before ':' is remote (SSH-forwarded). Returns true when local
    /// or unknown (the attach probe is authoritative), false only for the obvious remote case.
    /// </summary>
    private bool IsLocalDisplay()
    {
        try
        {
            IntPtr p = X11Interop.XDisplayString(_display);
            if (p == IntPtr.Zero)
            {
                return true; // unknown — let the attach probe decide
            }

            string s = Marshal.PtrToStringAnsi(p) ?? string.Empty;
            return s.Length == 0
                || s[0] == ':'
                || s.StartsWith("unix:", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: XDisplayString failed; assuming local");
            return true;
        }
    }

    /// <summary>
    /// Queries the root window geometry under the error trap. Never throws; does NOT demote on
    /// failure (a transient geometry fault should not retire a working capture path). Must be
    /// called under <see cref="_xLock"/>.
    /// </summary>
    private bool TryGetRootGeometry(IntPtr root, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            XlibErrorTrap.Reset(_display);
            int status = X11Interop.XGetGeometry(
                _display, root, out _, out _, out _,
                out uint w, out uint h, out _, out _);
            X11Interop.XSync(_display, false);
            int err = XlibErrorTrap.GetLastErrorCode(_display);

            if (status == 0 || err != 0)
            {
                _logger?.LogWarning(
                    "X11ShmFrameSourceBackend: XGetGeometry trapped X error {ErrorCode}", err);
                return false;
            }

            width = (int)w;
            height = (int)h;
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: XGetGeometry failed");
            return false;
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/>.</summary>
    private void RecordXError(int errorCode, string operation)
    {
        _consecutiveXErrors++;
        _logger?.LogWarning(
            "X11ShmFrameSourceBackend: trapped X error {ErrorCode} during {Operation} (consecutive: {Count})",
            errorCode, operation, _consecutiveXErrors);

        if (_consecutiveXErrors >= DemotionErrorThreshold && !_demoted)
        {
            // Contract §3.2: repeated trapped errors demote the backend — return black frames
            // without touching X for subsequent calls.
            _demoted = true;
            _logger?.LogError(
                "X11ShmFrameSourceBackend: demoted after {Count} consecutive X errors — " +
                "returning black frames without touching X (contract §3.2 demote)",
                _consecutiveXErrors);
        }
    }

    /// <summary>
    /// Forces the alpha byte of every pixel to 0xFF when the source visual left it undefined
    /// (depth 24) or when returning a black fallback, then wraps the buffer in a RawFrame of
    /// the given dimensions. Returns a RawFrame satisfying the tight-pack contract
    /// (BgraData.Length == Width*Height*4).
    /// </summary>
    private static RawFrame ForceAlphaOpaque(byte[] bgra, int width, int height, bool force)
    {
        if (force)
        {
            for (int i = 3; i < bgra.Length; i += 4)
            {
                bgra[i] = 0xFF;
            }
        }

        return new RawFrame(width, height, bgra);
    }

    /// <summary>
    /// Builds a black RawFrame of the given dimensions (alpha opaque). Used for the pre-lock
    /// short-circuits (demoted / no display / disposed).
    /// </summary>
    private static RawFrame BlackFrame(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (int i = 3; i < bgra.Length; i += 4)
        {
            bgra[i] = 0xFF;
        }

        return new RawFrame(width, height, bgra);
    }

    private void TryShmdt(IntPtr shmaddr)
    {
        try
        {
            X11Interop.shmdt(shmaddr);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: shmdt failed during cleanup");
        }
    }

    private void TryShmctlRmid(int shmid)
    {
        try
        {
            X11Interop.shmctl(shmid, X11Interop.IPC_RMID, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: shmctl(IPC_RMID) failed during cleanup");
        }
    }

    private void TryXDestroyImage(IntPtr image)
    {
        try
        {
            X11Interop.XDestroyImage(image);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11ShmFrameSourceBackend: XDestroyImage failed during cleanup");
        }
    }

    private static int ReadInt32(IntPtr ptr, int offset) => Marshal.ReadInt32(ptr, offset);

    /// <summary>
    /// Snapshot of a live shared-memory segment, kept as a readonly struct so teardown can run
    /// even after the instance fields are cleared (dispose-after-clear safety).
    /// </summary>
    private readonly struct SegmentState
    {
        public readonly IntPtr Image;
        public readonly IntPtr ShmInfo;
        public readonly IntPtr Shmaddr;
        public readonly int Shmid;
        public readonly int Width;
        public readonly int Height;
        public readonly int Stride;
        public readonly int Depth;
        public readonly int ByteOrder;

        public SegmentState(
            IntPtr image, IntPtr shmInfo, IntPtr shmaddr, int shmid,
            int width, int height, int stride, int depth, int byteOrder)
        {
            Image = image;
            ShmInfo = shmInfo;
            Shmaddr = shmaddr;
            Shmid = shmid;
            Width = width;
            Height = height;
            Stride = stride;
            Depth = depth;
            ByteOrder = byteOrder;
        }
    }

    /// <summary>
    /// Field offsets of the native <c>XImage</c> struct on LP64 (x64 Linux), per Xutil.h
    /// <c>struct _XImage</c>. Read by offset (not Marshal.PtrToStructure) so the full struct —
    /// including the trailing function-pointer table — never needs to be modeled. Identical to
    /// <c>X11BasicFrameSourceBackend.XImageOffsets</c> (same XImage ABI).
    /// </summary>
    private static class XImageOffsets
    {
        public const int Width = 0;             // int
        public const int Height = 4;            // int
        public const int XOffset = 8;           // int
        public const int Format = 12;           // int
        public const int Data = 16;             // char* (8 bytes, 8-aligned)
        public const int ByteOrder = 24;        // int
        public const int BitmapUnit = 28;       // int
        public const int BitmapBitOrder = 32;   // int
        public const int BitmapPad = 36;        // int
        public const int Depth = 40;            // int
        public const int BytesPerLine = 44;     // int
        public const int BitsPerPixel = 48;     // int
    }

    /// <summary>
    /// Closes the dedicated X display connection and tears down any cached segment
    /// (contract §3.1 + §3.4: open per backend instance at init, close on dispose; free ALL
    /// shm + image resources on the way out).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_xLock)
        {
            DestroySegment();
            CloseDisplayLocked();
        }
    }
}
