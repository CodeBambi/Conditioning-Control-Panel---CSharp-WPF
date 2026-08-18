using Avalonia.Controls;
using CcpClient.Desktop.Navigation;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// Hop 2 of the Loom route (wpf-surface-reachability.md §4, §8.4 verified live):
/// rack row <c>Spiral Overlay</c> -> module panel -> <c>THE LOOM — weave your own spiral</c>
/// -> <see cref="LoomLaunch"/>. The row selects the module and opens NO window
/// (<c>MainWindow.Presets.cs:976-978,1009</c> — "Navigation tiles still navigate to the ONE
/// existing entry, never launch"); only the button on the destination page launches, which is
/// WPF's one-entry rule (<c>MainWindow.Presets.cs:1007</c>).
/// </summary>
public partial class StudioPage : UserControl
{
    public StudioPage(LoomLaunch loom)
    {
        InitializeComponent();

        // Row selection swaps the panel in, exactly as WPF's rack drives its row state from
        // the RadioButton's own checked transitions rather than from the click handler
        // (StudioTabView.xaml.cs:664-665), so the panel can never drift out of step with the
        // selection. Right-click is NOT handled: WPF quick-toggles here, the port has no
        // effect flag to flip, and an unhandled gesture is honest where a fake toggle is not.
        RowSpiralOverlay.IsCheckedChanged += (_, _) =>
        {
            var open = RowSpiralOverlay.IsChecked == true;
            SpiralModulePanel.IsVisible = open;
            RackHint.IsVisible = !open;
        };

        LoomButton.Click += (_, _) => loom.Launch();
    }
}
