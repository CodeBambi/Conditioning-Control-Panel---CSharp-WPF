using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Dialog for adding a new knowledge base link.
    /// </summary>
    public partial class KnowledgeLinkEditorDialog : Window
    {
        /// <summary>
        /// The result of the dialog - the created link, or null if cancelled.
        /// </summary>
        public KnowledgeBaseLink? Result { get; private set; }

        public KnowledgeLinkEditorDialog()
        {
            InitializeComponent();
            TxtUrl.Focus();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            // Validate URL
            var url = TxtUrl.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Please enter a URL.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtUrl.Focus();
                return;
            }

            // Validate Title
            var title = TxtTitle.Text?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("Please enter a title.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTitle.Focus();
                return;
            }

            // Create the link
            Result = new KnowledgeBaseLink
            {
                Url = url,
                Title = title,
                Description = TxtDescription.Text?.Trim() ?? string.Empty
            };

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
