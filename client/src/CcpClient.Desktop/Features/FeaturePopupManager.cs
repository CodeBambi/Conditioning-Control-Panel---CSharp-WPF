using Avalonia.Controls;

namespace CcpClient.Desktop.Features;

/// <summary>
/// One-at-a-time feature-popup lifecycle (manifest row W-04; WPF parity
/// MainWindow.Presets.cs:846-873): close-existing-before-new, modeless owned show, focus
/// restoration to the owner on close. The popup surface and the restoration are seams so
/// manager transitions are unit-testable without a display.
/// </summary>
public sealed class FeaturePopupManager
{
    /// <summary>Window-free popup surface; <see cref="FeaturePopupWindow"/> implements it for real.</summary>
    public interface IPopup
    {
        /// <summary>Raised when the popup has closed (satisfied by <c>TopLevel.Closed</c> on the real window).</summary>
        event EventHandler? Closed;

        /// <summary>Show modeless, owned by <paramref name="owner"/> (<c>Window.Show(owner)</c>).</summary>
        void ShowOwned(Window owner);

        void Close();
    }

    private readonly Window _owner;
    private readonly Func<IPopup> _factory;
    private readonly Action _restoreFocus;
    private IPopup? _active;

    public FeaturePopupManager(Window owner, Func<IPopup> factory, Action restoreFocus)
    {
        _owner = owner;
        _factory = factory;
        _restoreFocus = restoreFocus;
    }

    public IPopup? Active => _active;

    /// <summary>Close any existing popup before opening the new one (Presets.cs:852), then show modeless (Presets.cs:873).</summary>
    public IPopup Show()
    {
        _active?.Close();
        var popup = _factory();
        popup.Closed += OnPopupClosed;
        _active = popup;
        popup.ShowOwned(_owner);
        return popup;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_active, sender))
        {
            _active = null; // Presets.cs:859-861
        }

        // W-04 focus restoration runs for every tracked popup close (Presets.cs:858-870) —
        // including the close-existing-before-new case, matching WPF event order.
        _restoreFocus();
    }

    /// <summary>
    /// W-04 restoration (Presets.cs:862-870): ShowInTaskbar=false owned windows can let the
    /// OS activate whatever is behind on close — explicitly un-minimize-if-minimized and
    /// re-activate the owner. Guarded for shutdown (Presets.cs:870).
    /// </summary>
    public static Action CreateFocusRestoration(Window owner) => () =>
    {
        try
        {
            if (owner.WindowState == WindowState.Minimized)
            {
                owner.WindowState = WindowState.Normal;
            }

            owner.Activate();
        }
        catch
        {
            // The owner window may be shutting down (Presets.cs:870 parity).
        }
    };
}
