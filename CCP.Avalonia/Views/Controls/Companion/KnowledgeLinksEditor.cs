// PORTED from WPF 7.1.5 ConditioningControlPanel/Views/Controls/Companion/KnowledgeLinksEditor.xaml(.cs):
// the global knowledge base links ("Links it knows"), lifted out of CompanionPromptEditorDialog so the
// dialog and Companion > Links can host one control. Code only on this head (a list, an empty line
// and two buttons); the add form is the head's KnowledgeLinkEditorDialog, the "select one first"
// note the head's MessageDialog, exactly as CompanionPromptEditorDialog does them.
using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// The global knowledge base links editor (AppSettings.GlobalKnowledgeBaseLinks).
    /// <see cref="SaveImmediately"/> writes every add / remove straight to settings (the page);
    /// without it the host saves through <see cref="SaveToSettings"/> (a dialog's Save button).
    /// </summary>
    public sealed class KnowledgeLinksEditor : UserControl
    {
        private readonly ObservableCollection<KnowledgeBaseLink> _links = new();

        public KnowledgeLinksEditor()
        {
            LstKnowledgeLinks = new ListBox
            {
                Name = "LstKnowledgeLinks", MinHeight = 120, MaxHeight = 320, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Foreground = Brushes.White, FontSize = 12,
                SelectionMode = SelectionMode.Single, ItemsSource = _links,
                ItemTemplate = new FuncDataTemplate<KnowledgeBaseLink>((link, _) => Row(link)),
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(LstKnowledgeLinks, global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            TxtKnowledgeLinksEmpty = CompanionPageHost.Loc(new TextBlock
            {
                Name = "TxtKnowledgeLinksEmpty", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false,
            }, "companion_page_links_known_empty");

            BtnAddKnowledgeLink = KbButton("btn_add_link", "BtnAddKnowledgeLink");
            BtnRemoveKnowledgeLink = KbButton("btn_remove_selected", "BtnRemoveKnowledgeLink");
            BtnRemoveKnowledgeLink.Margin = new Thickness(8, 0, 0, 0);
            BtnAddKnowledgeLink.Click += async (_, _) =>
            {
                var dialog = new KnowledgeLinkEditorDialog();
                await dialog.ShowDialogSafe(TopLevel.GetTopLevel(this) as Window);
                if (dialog.Result == null) return;
                _links.Add(dialog.Result);
                Commit();
            };
            BtnRemoveKnowledgeLink.Click += async (_, _) =>
            {
                if (LstKnowledgeLinks.SelectedItem is KnowledgeBaseLink link)
                {
                    _links.Remove(link);
                    Commit();
                }
                else if (TopLevel.GetTopLevel(this) is Window owner)
                    await MessageDialog.ShowAsync(owner, Loc.Get("btn_remove_selected"), Loc.Get("msg_please_select_a_link_to_remove"));
            };

            Content = new StackPanel
            {
                Children =
                {
                    new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
                        CornerRadius = new CornerRadius(6), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8),
                        Child = new Grid { Children = { LstKnowledgeLinks, TxtKnowledgeLinksEmpty } },
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { BtnAddKnowledgeLink, BtnRemoveKnowledgeLink } },
                },
            };
            _links.CollectionChanged += (_, _) => UpdateEmpty();
            UpdateEmpty();
        }

        internal ListBox LstKnowledgeLinks { get; }
        internal TextBlock TxtKnowledgeLinksEmpty { get; }
        internal Button BtnAddKnowledgeLink { get; }
        internal Button BtnRemoveKnowledgeLink { get; }

        /// <summary>True on the Links page: each edit saves at once.</summary>
        public bool SaveImmediately { get; set; }

        /// <summary>Raised after an add or a remove.</summary>
        public event EventHandler? Changed;

        internal ObservableCollection<KnowledgeBaseLink> Links => _links;

        public void Load()
        {
            _links.Clear();
            var links = CoreSettings.Current?.GlobalKnowledgeBaseLinks;
            if (links != null)
                foreach (var link in links) _links.Add(link);
        }

        public void SaveToSettings()
        {
            if (CoreSettings.Current is not { } s) return;
            s.GlobalKnowledgeBaseLinks.Clear();
            foreach (var link in _links) s.GlobalKnowledgeBaseLinks.Add(link);
        }

        private void UpdateEmpty() => TxtKnowledgeLinksEmpty.IsVisible = _links.Count == 0;

        private void Commit()
        {
            if (SaveImmediately)
            {
                SaveToSettings();
                CoreSettings.Save();
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static Control Row(KnowledgeBaseLink link)
        {
            var desc = new TextBlock
            {
                Text = link.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0)),
                IsVisible = !string.IsNullOrEmpty(link.Description),
            };
            return new StackPanel
            {
                Margin = new Thickness(0, 3),
                Children =
                {
                    new TextBlock { Text = link.Title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
                                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)) },
                    new TextBlock { Text = link.Url, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
                                    Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)) },
                    desc,
                },
            };
        }

        private static Button KbButton(string key, string name) => new()
        {
            Name = name,
            Content = CompanionPageHost.Loc(new TextBlock(), key),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 6),
            CornerRadius = new CornerRadius(6),
            Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
        };
    }
}
