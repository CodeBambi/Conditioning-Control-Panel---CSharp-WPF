using Avalonia;
using Avalonia.Controls;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Companion;

/// <summary>
/// The companion UI surface (SP-046 slice c7). Owned, modeless, ShowInTaskbar=false
/// (W-04 window-contract discipline; placement decision record.md §2.1). The window owns
/// its view-model's lifetime: Escape with no confirm open closes; the confirm overlay
/// focuses NO by default every time it opens (WPF MessageBox default-No parity,
/// Patreon.cs:920-927); close marks the VM closed so late dispatch posts are no-ops
/// (async contract §5.5).
/// </summary>
public partial class CompanionWindow : Window
{
    private readonly CompanionViewModel _vm;

    public CompanionWindow(ApplicationHost host)
    {
        InitializeComponent();
        var participant = host.Participants.OfType<CompanionParticipant>().Single();
        _vm = new CompanionViewModel(participant, host.UiDispatch);
        DataContext = _vm;

        _vm.CloseRequested += (_, _) => Close();
        // Default NO (WPF MessageBoxDefaultButton.Button2 parity): the destructive answer
        // is never the focused default. Focus fires when the overlay is VISIBLE AND
        // LAID OUT (focusing a not-yet-visible control is a no-op).
        ConfirmOverlay.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty && ConfirmOverlay.IsVisible)
            {
                // Posted: focus lands after the binding-driven layout pass (focusing a
                // not-yet-laid-out control is a no-op).
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => ConfirmNoButton.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
            }
        };

        Opened += (_, _) =>
        {
            _vm.RefreshStatus();
            ChatInput.Focus();
        };
        Closed += (_, _) => _vm.OnWindowClosed();
    }

    /// <summary>The view-model (tests drive the real wiring through it).</summary>
    public CompanionViewModel ViewModel => _vm;
}
