using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class CompanionTabView : UserControl
{
    public CompanionTabView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// AI-5: saves the masked OpenAI API key to <c>ISecretStore</c> via the view model, then
    /// clears the entry field. The key is read straight off the control (never bound) and is
    /// never retained in the UI. Parity ref: WPF
    /// <c>MainWindow/MainWindow.Patreon.cs:1387-1394</c> (<c>TxtOpenAiApiKey_PasswordChanged</c>).
    /// </summary>
    private void SaveOpenAiKey_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CompanionTabViewModel vm) return;
        if (TxtOpenAiKey == null) return;

        vm.SaveOpenAiKey(TxtOpenAiKey.Text ?? string.Empty);
        TxtOpenAiKey.Text = string.Empty;
    }

    /// <summary>
    /// AI-5: deletes the stored OpenAI API key from <c>ISecretStore</c> via the view model and
    /// clears the entry field.
    /// </summary>
    private void ClearOpenAiKey_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CompanionTabViewModel vm) return;
        if (TxtOpenAiKey == null) return;

        vm.ClearOpenAiKey();
        TxtOpenAiKey.Text = string.Empty;
    }
}
