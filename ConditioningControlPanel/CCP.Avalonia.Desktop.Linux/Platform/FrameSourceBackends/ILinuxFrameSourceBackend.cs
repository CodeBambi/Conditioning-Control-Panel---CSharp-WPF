using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.FrameSourceBackends;

/// <summary>
/// A Linux screen-capture backend behind <see cref="IFrameSource"/>. Each backend captures
/// one <see cref="ScreenInfo"/> into a tightly-packed BGRA <see cref="RawFrame"/>
/// (linux-framesource-contract.md §1.2). Backends are <see cref="IDisposable"/> because the
/// X11/Wayland backends own native display connections that must be closed.
/// </summary>
/// <remarks>
/// Implementations honor the privacy hard-line (contract §1.4): frames are memory-only,
/// never written to disk/network/logs; and the never-crash contract (§2.2 / §5): on any
/// failure <see cref="CaptureAsync"/> returns a black frame rather than throwing.
/// </remarks>
public interface ILinuxFrameSourceBackend : IDisposable
{
    /// <summary>Human-readable backend name for diagnostics (never frame content).</summary>
    string Name { get; }

    /// <summary>True when the backend successfully probed its native dependencies.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Captures <paramref name="screen"/> into a memory-only BGRA
    /// <see cref="RawFrame"/> (<c>BgraData.Length == Width*Height*4</c>). Never throws to a
    /// crash: on any failure (no display, demoted, trapped X error, cancellation) returns a
    /// black frame (contract §1.4 / §5).
    /// </summary>
    Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default);
}
