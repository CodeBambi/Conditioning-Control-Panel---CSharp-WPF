using Avalonia.Controls;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The Companion door's destination. The window-opening action stays with the shell (it owns
/// the one-at-a-time policy and the owner window); this page owns the control that asks for it.
/// </summary>
public partial class CompanionPage : UserControl
{
    public CompanionPage(Action showCompanion)
    {
        InitializeComponent();
        CompanionButton.Click += (_, _) => showCompanion();
    }
}
