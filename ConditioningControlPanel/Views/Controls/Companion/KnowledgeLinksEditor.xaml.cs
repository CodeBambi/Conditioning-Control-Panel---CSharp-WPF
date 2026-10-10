using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// The global knowledge base links editor (AppSettings.GlobalKnowledgeBaseLinks), lifted out of
    /// CompanionPromptEditorDialog so the dialog and Companion > Links host the same control.
    /// <see cref="SaveImmediately"/> writes every add / remove straight to settings (the page);
    /// without it the host saves through <see cref="SaveToSettings"/> (the dialog's Save button).
    /// </summary>
    public partial class KnowledgeLinksEditor : UserControl
    {
        private readonly ObservableCollection<KnowledgeBaseLink> _links = new();

        public KnowledgeLinksEditor()
        {
            InitializeComponent();
            LstKnowledgeLinks.ItemsSource = _links;
            _links.CollectionChanged += (_, _) => UpdateEmpty();
            UpdateEmpty();
        }

        /// <summary>True on the Links page: each edit saves at once.</summary>
        public bool SaveImmediately { get; set; }

        /// <summary>Raised after an add or a remove.</summary>
        public event EventHandler? Changed;

        internal ObservableCollection<KnowledgeBaseLink> Links => _links;

        public void Load()
        {
            _links.Clear();
            var links = App.Settings?.Current?.GlobalKnowledgeBaseLinks;
            if (links != null)
                foreach (var link in links) _links.Add(link);
        }

        public void SaveToSettings()
        {
            if (App.Settings?.Current == null) return;
            App.Settings.Current.GlobalKnowledgeBaseLinks.Clear();
            foreach (var link in _links) App.Settings.Current.GlobalKnowledgeBaseLinks.Add(link);
        }

        private void UpdateEmpty() =>
            TxtKnowledgeLinksEmpty.Visibility = _links.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        private void Commit()
        {
            if (SaveImmediately)
            {
                SaveToSettings();
                App.Settings?.Save();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void AddKnowledgeLink_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new KnowledgeLinkEditorDialog { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _links.Add(dialog.Result);
                Commit();
            }
        }

        private void RemoveKnowledgeLink_Click(object sender, RoutedEventArgs e)
        {
            if (LstKnowledgeLinks.SelectedItem is KnowledgeBaseLink link)
            {
                _links.Remove(link);
                Commit();
            }
            else
            {
                var owner = Window.GetWindow(this);
                var text = Loc.Get("msg_please_select_a_link_to_remove");
                if (owner != null) MessageBox.Show(owner, text, Loc.Get("btn_remove_selected"), MessageBoxButton.OK, MessageBoxImage.Information);
                else MessageBox.Show(text, Loc.Get("btn_remove_selected"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
