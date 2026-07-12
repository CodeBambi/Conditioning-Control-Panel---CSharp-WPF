using System;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.Compositor;

/// <summary>
/// Provides the current compositor capture mask snapshot. Implemented by the compositor
/// engine (or an intermediary holder) and read by the low-level mouse hook to decide,
/// synchronously inside the hook callback, whether a click should be SWALLOWED (inside the
/// mask) or PASSED to the app behind (ambient-only / bare-desktop region).
/// </summary>
public interface ICaptureMaskSource
{
    /// <summary>
    /// The current immutable capture mask snapshot (never null). A shared <see cref="CaptureMask.Empty"/>
    /// when no non-ambient active layer is painted, so the ambient regions and bare desktop
    /// remain click-through.
    /// </summary>
    CaptureMask CurrentMask { get; }
}

/// <summary>
/// Mutable holder of the current <see cref="CaptureMask"/> snapshot, decoupling the writer
/// (compositor engine, UI thread) from the reader (low-level mouse hook, system hook thread)
/// to avoid the DI cycle that would come from the hook depending directly on the engine while
/// the engine owns the hook's install/uninstall lifecycle.
/// </summary>
/// <remarks>
/// THREADING: <see cref="Publish"/> runs on the UI thread (one engine tick at a time);
/// <see cref="CurrentMask"/> is read from the hook callback thread. A volatile reference swap
/// gives readers a consistent (immutable) snapshot without a lock — the <see cref="CaptureMask"/>
/// itself is immutable. Writes are never torn (reference assignment is atomic on the CLR for
/// reference types on all supported architectures).
/// </remarks>
public sealed class CaptureMaskState : ICaptureMaskSource
{
    private CaptureMask _mask = CaptureMask.Empty;
    private readonly ILogger? _logger;

    public CaptureMaskState(ILogger<CaptureMaskState>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>The current immutable capture mask snapshot. Lock-free read.</summary>
    public CaptureMask CurrentMask => Volatile.Read(ref _mask);

    /// <summary>
    /// Publish a new mask snapshot (UI thread). Called once per engine tick with the union of
    /// every non-ambient active layer's painted region, or <see cref="CaptureMask.Empty"/>
    /// when nothing is captured. Returns the previous mask so the caller can detect the
    /// empty/non-empty transition and manage the hook's install lifecycle.
    /// </summary>
    public CaptureMask Publish(CaptureMask mask)
    {
        var previous = Volatile.Read(ref _mask);
        Volatile.Write(ref _mask, mask ?? CaptureMask.Empty);
        return previous;
    }
}
