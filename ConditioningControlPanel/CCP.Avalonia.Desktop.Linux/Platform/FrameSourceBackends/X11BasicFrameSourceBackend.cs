using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.Interop;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.FrameSourceBackends;

/// <summary>
/// X11 desktop frame capture via the universal <c>XGetImage</c> path
/// (linux-framesource-contract.md §3.3, Slice B). Slow but correct on every X server; the
/// MIT-SHM fast path (Slice C / wave 2) supersedes it where usable.
/// </summary>
/// <remarks>
/// <para><b>Behavioral contract</b> mirrors <c>WindowsFrameSource.cs</c> (the reference
/// IFrameSource impl): captures the rect identified by <c>ScreenInfo.Bounds</c> in absolute
/// virtual-desktop pixels (<c>WindowsFrameSource.cs:25-29,34</c>), returns tightly-packed
/// 32-bit BGRA with <c>BgraData.Length == Width*Height*4</c> (<c>WindowsFrameSource.cs:43-46</c>),
/// honors cancellation before and after the blit (<c>WindowsFrameSource.cs:23,37</c>), and
/// clamps dimensions to a minimum of 1x1 (<c>WindowsFrameSource.cs:28-29</c>).</para>
///
/// <para><b>Row repack + alpha normalization</b> (contract §1.2 / §3.3, the key X11
/// correction): <c>XGetImage</c> ZPixmap buffers may carry row padding
/// (<c>bytes_per_line &gt; width*4</c>) and depth-24 visuals leave the high byte UNDEFINED.
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
/// handler calls <c>exit()</c>. <c>XGetImage</c> raises <c>BadMatch</c> when the requested
/// rect is not fully inside the drawable (monitor hot-unplug/resize between the ScreenInfo
/// snapshot and capture). Every capture runs inside a <see cref="XlibErrorTrap"/> scope:
/// Reset(display) → XGetImage → XSync → GetLastErrorCode(display). On a trapped error THIS
/// call returns a per-call black frame (never an exception, never a process kill). Repeated
/// trapped errors demote the backend (contract §3.2: "if errors repeat, demote") so later
/// calls short-circuit to black without touching X.</para>
///
/// <para><b>Privacy hard-line (contract §1.4 — same class as webcam frames):</b> captured
/// frames are MEMORY-ONLY. They are never written to disk, never sent over the network,
/// never logged (only dimensions are logged, never pixel content). Derived, non-
/// reconstructable data (OCR hits, average colors, calibration coefficients) may flow onward
/// per each consumer's own contract.</para>
///
/// <para><b>No idle / background capture:</b> this backend is strictly PULL-BASED. It does
/// ZERO work unless a consumer calls <see cref="CaptureAsync"/>. There is no timer, no
/// background thread, no polling loop — start/stop is governed entirely by the consuming
/// feature's lifetime.</para>
///
/// <para><b>Capture-exclusion of CCP's OWN overlay windows — LIMITATION (privacy-invariant
/// note):</b> <c>XGetImage</c> on the root window has NO mechanism to exclude our own
/// topmost overlay windows (the Unified Compositor Engine overlay, the spiral, the
/// theme-color-filter). If the X server/compositor does not composite-redirect the root,
/// our overlays WILL appear in captured frames (recursive self-capture). There is no
/// standard X11 per-window "exclude-from-root-capture" property. Slice B performs NO
/// exclusion. Consequence &amp; rule: this source MUST NOT feed a visual effect that is
/// itself painted into our overlay (that would close a feedback loop). Wave 2+ may mitigate
/// by reading the XComposite overlay window or masking regions matching our overlay XIDs.</para>
/// </remarks>
public sealed class X11BasicFrameSourceBackend : ILinuxFrameSourceBackend
{
    /// <summary>
    /// Consecutive trapped-X-error threshold after which the backend demotes itself
    /// (contract §3.2 "if errors repeat, demote"). Mirrors X11InputShapeBackend's threshold.
    /// </summary>
    private const int DemotionErrorThreshold = 3;

    private readonly ILogger? _logger;
    private readonly object _xLock = new();

    private IntPtr _display;
    private bool _available;
    private bool _demoted;
    private int _consecutiveXErrors;
    private bool _disposed;

    public X11BasicFrameSourceBackend(ILogger<X11BasicFrameSourceBackend>? logger = null)
    {
        _logger = logger;
        ProbeDisplay();
    }

    /// <summary>Human-readable backend name for diagnostics (never frame content).</summary>
    public string Name => "X11BasicFrameSourceBackend";

    /// <summary>True only when the dedicated X display opened successfully and not demoted.</summary>
    public bool IsAvailable => _available && !_demoted;

    private void ProbeDisplay()
    {
        try
        {
            _display = X11Interop.XOpenDisplay(null);
            if (_display == IntPtr.Zero)
            {
                _logger?.LogWarning("X11BasicFrameSourceBackend: XOpenDisplay returned null (no X display)");
                _available = false;
                return;
            }

            // Install the process-kill guard BEFORE any request that could error
            // (contract §3.2; same registration pattern as X11InputShapeBackend).
            XlibErrorTrap.RegisterDisplay(_display);
            _available = true;
            _logger?.LogInformation(
                "X11BasicFrameSourceBackend: dedicated X display connection open (XGetImage path)");
        }
        catch (DllNotFoundException ex)
        {
            _logger?.LogWarning("X11BasicFrameSourceBackend: libX11 not found: {Message}", ex.Message);
            FailProbe();
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger?.LogWarning("X11BasicFrameSourceBackend: missing Xlib entry point: {Message}", ex.Message);
            FailProbe();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11BasicFrameSourceBackend: failed to open X display");
            FailProbe();
        }
    }

    private void FailProbe()
    {
        _available = false;
        lock (_xLock)
        {
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
            _logger?.LogWarning(ex, "X11BasicFrameSourceBackend: XCloseDisplay failed");
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
            if (_disposed || _display == IntPtr.Zero || _demoted)
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
        // Output is the REQUESTED size; out-of-bounds pixels stay black — dimension-stable
        // across hot-plug races (consumers index by RawFrame.Width/Height; contract §1.2).
        var output = new byte[reqW * reqH * 4];

        try
        {
            var root = X11Interop.XDefaultRootWindow(_display);
            if (root == IntPtr.Zero)
            {
                _logger?.LogWarning("X11BasicFrameSourceBackend: XDefaultRootWindow returned zero");
                return ForceAlphaOpaque(output, reqW, reqH, force: true);
            }

            // Contract §3.5: defensively clamp the rect to the root window geometry (monitor
            // layouts change between the ScreenInfo snapshot and capture). The root spans the
            // whole virtual desktop, so Bounds.X/Y ARE root-relative. A failed geometry probe
            // (transient) does NOT demote — just degrades this call to black.
            if (!TryGetRootGeometry(root, out int rootW, out int rootH))
            {
                return ForceAlphaOpaque(output, reqW, reqH, force: true);
            }

            int reqX = (int)boundsX;
            int reqY = (int)boundsY;
            int cx = Math.Clamp(reqX, 0, rootW);
            int cy = Math.Clamp(reqY, 0, rootH);
            int cx2 = Math.Clamp(reqX + reqW, 0, rootW);
            int cy2 = Math.Clamp(reqY + reqH, 0, rootH);
            int capW = cx2 - cx;
            int capH = cy2 - cy;
            if (capW <= 0 || capH <= 0)
            {
                // Entirely off-root (monitor gone between snapshot and capture): black frame.
                return ForceAlphaOpaque(output, reqW, reqH, force: true);
            }

            // Scoped Xlib error trap (contract §3.2): Reset → XGetImage → XSync → check.
            // The clamp above prevents the common BadMatch; the trap catches the residual
            // race (resize/unplug between the geometry probe and XGetImage).
            XlibErrorTrap.Reset(_display);

            IntPtr imagePtr = X11Interop.XGetImage(
                _display, root, cx, cy, (uint)capW, (uint)capH,
                X11Interop.AllPlanes, X11Interop.ZPixmap);

            // XSync flushes errors into the trap (XGetImage is the only request here).
            X11Interop.XSync(_display, false);
            int err = XlibErrorTrap.GetLastErrorCode(_display);

            if (imagePtr == IntPtr.Zero || err != 0)
            {
                RecordXError(err, "XGetImage");
                return ForceAlphaOpaque(output, reqW, reqH, force: true);
            }

            try
            {
                return RepackImage(imagePtr, reqX, reqY, cx, cy, capW, capH, reqW, reqH, output);
            }
            finally
            {
                // XDestroyImage frees the XImage struct AND its data buffer (contract §3.3).
                X11Interop.XDestroyImage(imagePtr);
            }
        }
        catch (DllNotFoundException ex)
        {
            _logger?.LogWarning("X11BasicFrameSourceBackend: libX11 missing at capture: {Message}", ex.Message);
            _available = false;
            return ForceAlphaOpaque(output, reqW, reqH, force: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11BasicFrameSourceBackend: capture faulted, returning black frame");
            return ForceAlphaOpaque(output, reqW, reqH, force: true);
        }
    }

    /// <summary>
    /// Repacks the captured XImage into the tightly-packed BGRA output buffer honoring
    /// <c>bytes_per_line</c> and normalizing alpha (contract §3.3, corrected vs the naïve
    /// straight-copy). Must be called under <see cref="_xLock"/> after a successful XGetImage.
    /// </summary>
    private RawFrame RepackImage(
        IntPtr imagePtr, int reqX, int reqY, int cx, int cy, int capW, int capH,
        int reqW, int reqH, byte[] output)
    {
        // Read XImage fields by offset (LP64 layout, Xutil.h struct _XImage) — avoids modeling
        // the full struct including the trailing function-pointer table (contract §3.3).
        int stride = ReadInt32(imagePtr, XImageOffsets.BytesPerLine);
        int depth = ReadInt32(imagePtr, XImageOffsets.Depth);
        int byteOrder = ReadInt32(imagePtr, XImageOffsets.ByteOrder);
        IntPtr data = ReadIntPtr(imagePtr, XImageOffsets.Data);

        if (data == IntPtr.Zero || stride < capW * 4)
        {
            // Defensive: malformed image — treat as a trapped error (demote-aware).
            RecordXError(0, "XGetImage (malformed XImage)");
            return ForceAlphaOpaque(output, reqW, reqH, force: true);
        }

        // Contract §3.3: on little-endian servers (byte_order == LSBFirst == 0, i.e. every
        // x86/ARM desktop) a standard TrueColor visual lays out as B,G,R,X in memory —
        // matching BGRA directly. Big-endian is out of scope; log once if ever seen.
        if (byteOrder != X11Interop.LSBFirst)
        {
            _logger?.LogWarning(
                "X11BasicFrameSourceBackend: XImage byte_order == {ByteOrder} (MSBFirst); " +
                "pixel layout may be wrong (big-endian servers out of scope, contract §3.3)",
                byteOrder);
        }

        // Destination offset of the in-bounds capture within the requested frame (≥ 0 because
        // cx >= reqX was clamped to [0, rootW] and reqX may be negative only off-origin).
        int destX = cx - reqX;
        int destY = cy - reqY;
        int rowBytes = capW * 4;

        // Partial capture (rect clamped by a hot-plug/resize race): the out-of-bounds padding
        // must be OPAQUE black, not transparent black — pre-fill its alpha before the row
        // copies overwrite the in-bounds region. (For depth-24 sources the depth==24 pass
        // below covers the whole buffer anyway; this handles depth-32 partial captures.)
        if (depth != 24 && (destX > 0 || destY > 0 || capW < reqW || capH < reqH))
        {
            for (int i = 3; i < output.Length; i += 4)
            {
                output[i] = 0xFF;
            }
        }

        // Copy each captured row honoring bytes_per_line (rows may be padded). Out-of-bounds
        // pixels remain zero (black). This is the corrected repack vs the draft's straight
        // Marshal.Copy of width*height*4 bytes (contract §3.3).
        for (int row = 0; row < capH; row++)
        {
            Marshal.Copy(
                data + (row * stride),
                output,
                (destY + row) * (reqW * 4) + (destX * 4),
                rowBytes);
        }

        _consecutiveXErrors = 0;

        // Contract §3.3: depth-24 visuals leave the high byte UNDEFINED — force opaque alpha
        // so the buffer satisfies the RawFrame tight-pack contract. Depth-32 sources carry
        // meaningful alpha and are left as captured.
        return ForceAlphaOpaque(output, reqW, reqH, force: depth == 24);
    }

    /// <summary>
    /// Queries the root window geometry under the error trap. Never throws; does NOT demote
    /// on failure (a transient geometry fault should not retire a working capture path).
    /// Must be called under <see cref="_xLock"/>.
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
                    "X11BasicFrameSourceBackend: XGetGeometry trapped X error {ErrorCode}", err);
                return false;
            }

            width = (int)w;
            height = (int)h;
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "X11BasicFrameSourceBackend: XGetGeometry failed");
            return false;
        }
    }

    /// <summary>Must be called under <see cref="_xLock"/>.</summary>
    private void RecordXError(int errorCode, string operation)
    {
        _consecutiveXErrors++;
        _logger?.LogWarning(
            "X11BasicFrameSourceBackend: trapped X error {ErrorCode} during {Operation} (consecutive: {Count})",
            errorCode, operation, _consecutiveXErrors);

        if (_consecutiveXErrors >= DemotionErrorThreshold && !_demoted)
        {
            // Contract §3.2: repeated trapped errors demote the backend — return black frames
            // without touching X for subsequent calls.
            _demoted = true;
            _logger?.LogError(
                "X11BasicFrameSourceBackend: demoted after {Count} consecutive X errors — " +
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

    private static int ReadInt32(IntPtr ptr, int offset) => Marshal.ReadInt32(ptr, offset);
    private static IntPtr ReadIntPtr(IntPtr ptr, int offset) => Marshal.ReadIntPtr(ptr, offset);

    /// <summary>
    /// Field offsets of the native <c>XImage</c> struct on LP64 (x64 Linux), per Xutil.h
    /// <c>struct _XImage</c>. Read by offset (not Marshal.PtrToStructure) so the full struct —
    /// including the trailing function-pointer table — never needs to be modeled.
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
        // red_mask (ulong) at 56, green_mask at 64, blue_mask at 72, obdata (ptr) at 80,
        // then the funcs table at 88 — none read here.
    }

    /// <summary>
    /// Closes the dedicated X display connection (contract §3.1: open per backend instance at
    /// init, close on dispose).
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
            CloseDisplayLocked();
        }
    }
}
