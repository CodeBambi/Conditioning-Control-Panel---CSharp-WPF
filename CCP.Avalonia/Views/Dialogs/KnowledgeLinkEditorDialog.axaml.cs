using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
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
            AvaloniaXamlLoader.Load(this);
            DataContext = new KnowledgeLinkEditorViewModel();

            var add = this.FindControl<Button>("BtnAdd")!;
            var cancel = this.FindControl<Button>("BtnCancel")!;

            add.Click += (_, _) => Accept();
            cancel.Click += (_, _) => Close();

            // WPF focused in the constructor; on Avalonia the window is not shown yet at that point.
            Opened += (_, _) => this.FindControl<TextBox>("TxtUrl")!.Focus();
        }

        private void Accept()
        {
            var txtUrl = this.FindControl<TextBox>("TxtUrl")!;
            var txtTitle = this.FindControl<TextBox>("TxtTitle")!;
            var txtDescription = this.FindControl<TextBox>("TxtDescription")!;
            var error = this.FindControl<TextBlock>("TxtError")!;

            // Validate URL
            var url = txtUrl.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                error.Text = Loc.Get("msg_enter_url");
                error.IsVisible = true;
                txtUrl.Focus();
                return;
            }

            // Validate Title
            var title = txtTitle.Text?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                error.Text = Loc.Get("msg_enter_title");
                error.IsVisible = true;
                txtTitle.Focus();
                return;
            }

            error.IsVisible = false;

            // Create the link
            Result = new KnowledgeBaseLink
            {
                Url = url,
                Title = title,
                Description = txtDescription.Text?.Trim() ?? string.Empty
            };

            Close();
        }
    }

    public sealed class KnowledgeLinkEditorViewModel
    {
        public string LocTitle => Loc.Get("dialog_add_knowledge_link");
        public string LocLabelUrl => Loc.Get("label_url_2");
        public string LocLabelTitle => Loc.Get("label_title");
        public string LocLabelDescription => Loc.Get("label_description_optional");
        public string LocCancel => Loc.Get("btn_cancel");
        public string LocAdd => Loc.Get("btn_add");
    }
}
