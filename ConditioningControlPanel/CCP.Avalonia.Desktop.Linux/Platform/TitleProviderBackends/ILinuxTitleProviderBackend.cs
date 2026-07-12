using System;

namespace ConditioningControlPanel.Avalonia.Desktop.Linux.Platform.TitleProviderBackends;

/// <summary>
/// A single foreground-window-title backend on Linux, selected at runtime by
/// <see cref="LinuxTitleProviderBackendSelector"/> and held by
/// <see cref="LinuxForegroundWindowTitleProvider"/>. Implementations are an internal
/// composition detail of the Linux head; the public seam is
/// <see cref="ConditioningControlPanel.Core.Platform.IForegroundWindowTitleProvider"/>.
/// </summary>
/// <remarks>
/// <para><b>Privacy contract (linux-foreground-title-contract.md §1.3):</b> the returned
/// title is memory-only input for activity classification. Backends MUST NEVER log title
/// content, persist it, or send it over the network — log lines carry backend names and
/// reasons only. The seam returns the TITLE string only (no app_id, no PID), matching the
/// Windows title-only contract.</para>
/// <para><b>Never-throw:</b> <see cref="GetForegroundWindowTitle"/> returns <c>null</c> on any
/// failure (no display, no EWMH, trapped X error) rather than throwing — it is polled every
/// 1.5s from a threadpool thread by the awareness engine. Implementations also extend
/// <see cref="IDisposable"/> because backends like <see cref="X11TitleBackend"/> own a
/// dedicated X display connection (§3.1).</para>
/// </remarks>
internal interface ILinuxTitleProviderBackend : IDisposable
{
    /// <summary>Backend name for diagnostics (never contains title content).</summary>
    string Name { get; }

    /// <summary>
    /// Title of the current foreground window, or null/empty when unavailable. Never throws.
    /// </summary>
    string? GetForegroundWindowTitle();
}
