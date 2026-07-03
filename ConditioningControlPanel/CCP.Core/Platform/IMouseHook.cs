namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform low-level mouse hook. On Windows this captures WH_MOUSE_LL events;
/// on other platforms the events simply never fire.
///
/// The implementation is a shared singleton and <see cref="Install"/>/<see cref="Uninstall"/>
/// are reference-counted: consumers MUST strictly pair their calls (install once when they
/// start needing events, uninstall once when they stop). Event coordinates are PHYSICAL
/// screen pixels. Handlers run inside the hook callback and must stay near-free (marshal
/// real work to the UI thread).
/// </summary>
public interface IMouseHook
{
    /// <summary>Raised when the left mouse button is pressed anywhere on the system.</summary>
    event EventHandler<HookPoint>? LeftButtonDown;

    /// <summary>Raised when the right mouse button is pressed anywhere on the system.</summary>
    event EventHandler<HookPoint>? RightButtonDown;

    /// <summary>Raised when the right mouse button is released anywhere on the system.</summary>
    event EventHandler<HookPoint>? RightButtonUp;

    /// <summary>Raised when the left mouse button is released anywhere on the system.</summary>
    event EventHandler<HookPoint>? LeftButtonUp;

    /// <summary>Installs the global hook. Safe to call multiple times.</summary>
    void Install();

    /// <summary>Uninstalls the global hook and releases native resources.</summary>
    void Uninstall();
}
