using System.Windows;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "What's New" dialog shown once after an update. Uses a fixed window size with a
    /// scrollable notes region and a pinned OK button, so long patch notes can never push
    /// the button off-screen (the old MessageBox-based version could — see ccp-bugs #427).
    /// </summary>
    public partial class WhatsNewDialog : Window
    {
        public WhatsNewDialog(string title, string notes)
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtNotes.Text = notes;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
