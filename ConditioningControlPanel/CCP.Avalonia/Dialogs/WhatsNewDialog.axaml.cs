using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Update;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// "What's New" dialog shown once after an update. Uses a fixed window size with a scrollable notes
/// region and a pinned OK button, so long patch notes can never push the button off-screen (the old
/// MessageBox-based version could — see ccp-bugs #427). Mirrors the WPF WhatsNewDialog.
/// </summary>
public partial class WhatsNewDialog : Window
{
    // Public parameterless ctor keeps the XAML reachable via the runtime loader (avoids AVLN3001;
    // matches WelcomeDialog). The parameterized ctor below is the one callers use.
    public WhatsNewDialog()
    {
        InitializeComponent();
    }

    public WhatsNewDialog(string title, string notes)
        : this()
    {
        TxtTitle.Text = title;
        TxtNotes.Text = notes;
    }

    private void BtnOk_Click(object? sender, RoutedEventArgs e) => Close(true);

    /// <summary>
    /// Show the What's New dialog once when the running version differs from the last one the user
    /// saw (post-update). Returns true when the dialog was shown. No-op once <c>LastSeenVersion</c>
    /// catches up to <see cref="UpdateService.AppVersion"/>.
    /// </summary>
    public static async Task<bool> ShowIfNeeded(Window owner)
    {
        var settingsService = App.Services?.GetService<ISettingsService>();
        if (settingsService?.Current is not { } settings) return false;

        var currentVersion = UpdateService.AppVersion;
        if (settings.LastSeenVersion == currentVersion) return false;

        var dialog = new WhatsNewDialog(Loc.GetF("whats_new_title_fmt", currentVersion), UpdateService.CurrentPatchNotes);
        await dialog.ShowDialog<bool?>(owner);

        settings.LastSeenVersion = currentVersion;
        settingsService.Save();
        return true;
    }
}
