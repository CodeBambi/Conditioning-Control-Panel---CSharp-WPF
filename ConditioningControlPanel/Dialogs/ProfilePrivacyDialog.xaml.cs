using System;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Views.Controls;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Holds the Profile tab's relocated sharing controls. The dialog owns no state of its own —
    /// it borrows the long-lived <see cref="ProfilePrivacyPanel"/> instance from DiscordTabView for
    /// as long as it is open, then hands it back on close so MainWindow keeps writing into the
    /// same controls.
    /// </summary>
    public partial class ProfilePrivacyDialog : Window
    {
        private readonly ProfilePrivacyPanel _panel;

        public ProfilePrivacyDialog(ProfilePrivacyPanel panel)
        {
            InitializeComponent();
            _panel = panel;

            // A WPF element has exactly one parent: if a previous dialog was torn down without its
            // Closed handler running, detach before re-parenting rather than throwing.
            if (_panel.Parent is System.Windows.Controls.Border previous)
                previous.Child = null;
            PanelHost.Child = _panel;

            Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            try
            {
                PanelHost.Child = null;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ProfilePrivacyDialog detach: {E}", ex.Message);
            }
        }

        private void BtnJoinDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var mw = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()
                         ?? Application.Current?.MainWindow as MainWindow;
                mw?.BtnDiscord_Click(sender, e);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ProfilePrivacyDialog: Join Discord failed");
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
