using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>Social &gt; Leash (WPF LeashTabView). Empty state only on this head: the leash cards
/// are parity row feat-leash. The button opens Friends, where offers start.</summary>
public partial class LeashTabView : UserControl
{
    public LeashTabView()
    {
        InitializeComponent();
        EmptyButton.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("friends");
    }

    /// <summary>True while there is nothing leash-shaped to show (always, until feat-leash lands).</summary>
    internal bool ShowingEmpty => EmptyPanel.IsVisible;
}
