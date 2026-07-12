using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.FrameSourceBackends;

/// <summary>
/// Last-resort IFrameSource backend: returns an opaque-black frame, never throws. Used when
/// no capture backend is available (Wayland/XWayland session with no native Wayland backend
/// yet, missing native libs, failed X probe, unknown session) and as the per-call degrade
/// target (linux-framesource-contract.md §5).
/// </summary>
/// <remarks>
/// <para><b>Never-crash guarantee (contract §2.2 / §5):</b> zero P/Invokes, always
/// constructs, always returns a valid RawFrame. The reason is logged EXACTLY ONCE (never
/// frame content — privacy hard-line, contract §1.4).</para>
/// <para><b>Consumer-degrade behavior (contract §5.2):</b> webcam calibration/gaze windows
/// get a black screen-frame; AvatarTube mirror gets a black backdrop; screen OCR/effects get
/// no hits. Other app functionality is unaffected.</para>
/// <para><b>RawFrame packing (contract §1.2, normative):</b> returns tightly-packed BGRA,
/// <c>BgraData.Length == Width*Height*4</c>, alpha forced to 0xFF — matching the
/// <c>WindowsFrameSource</c> reference (<c>WindowsFrameSource.cs:43-46</c>).</para>
/// </remarks>
public sealed class FallbackFrameSource : ILinuxFrameSourceBackend
{
    private readonly string _reason;
    private readonly ILogger? _logger;
    private int _logged;

    public FallbackFrameSource(string reason, ILogger<FallbackFrameSource>? logger = null)
    {
        _reason = reason ?? string.Empty;
        _logger = logger;
    }

    /// <summary>Human-readable backend name for diagnostics.</summary>
    public string Name => "FallbackFrameSource";

    /// <summary>Always available — zero P/Invokes, always constructs (contract §2.2).</summary>
    public bool IsAvailable => true;

    /// <summary>
    /// Returns an opaque-black <see cref="RawFrame"/> of the requested size, clamped to a
    /// minimum of 1x1 (<c>WindowsFrameSource.cs:28-29</c>). Logs the reason exactly once
    /// (contract §5: log the reason, never frame content).
    /// </summary>
    public Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _logged, 1) == 0)
        {
            _logger?.LogWarning(
                "Screen capture unavailable: {Reason}. Screen-dependent features will show " +
                "black/empty on this session type (linux-framesource-contract.md §5).",
                _reason);
        }

        // WindowsFrameSource.cs:28-29 — clamp to a minimum 1x1 dimension.
        var w = Math.Max(1, (int)screen.Bounds.Width);
        var h = Math.Max(1, (int)screen.Bounds.Height);

        // Tightly-packed BGRA, zero-initialized, alpha forced opaque (contract §1.2).
        var bgra = new byte[w * h * 4];
        for (int i = 3; i < bgra.Length; i += 4)
        {
            bgra[i] = 0xFF;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RawFrame(w, h, bgra));
    }

    /// <summary>No native resources — nothing to dispose (contract §5.1 shape).</summary>
    public void Dispose()
    {
    }
}
