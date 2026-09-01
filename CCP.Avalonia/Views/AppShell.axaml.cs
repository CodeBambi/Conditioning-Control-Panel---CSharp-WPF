using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views
{
    /// <summary>
    /// Left nav rail plus content host. Currently shows one ported view; the doors are static
    /// until more views land, because a router with one destination is not a router.
    /// </summary>
    public partial class AppShell : UserControl
    {
        public AppShell() => AvaloniaXamlLoader.Load(this);
    }
}
