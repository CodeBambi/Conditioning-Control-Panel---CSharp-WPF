namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Platform seam for reading the current foreground window's TITLE — and only the title:
/// no process name, no PID (privacy contract, WPF Services/UI/WindowAwarenessService.cs:63-66,:549-556).
/// Heads that cannot implement this (Linux/macOS for now) register nothing; the awareness
/// engine then refuses to start and the feature degrades gracefully to "off".
/// </summary>
public interface IForegroundWindowTitleProvider
{
    /// <summary>
    /// Title of the current foreground window, or null/empty when unavailable.
    /// The returned string must never be persisted or logged by callers — it is
    /// memory-only input for activity classification.
    /// </summary>
    string? GetForegroundWindowTitle();
}
