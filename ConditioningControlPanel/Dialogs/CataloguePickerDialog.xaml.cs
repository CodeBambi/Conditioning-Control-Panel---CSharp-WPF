using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// W3 Piece 1 — picker dialog shown when /api/enhancements/by-ht-url returns
    /// 2+ entries for the user's current HT video. Clicking a row dismisses the
    /// dialog and exposes the chosen entry via <see cref="SelectedEntry"/>; the
    /// caller is responsible for kicking off the download/open flow.
    ///
    /// Single-result lookups SKIP this dialog entirely — they go straight to
    /// the download path from the toast's "Use one" action.
    ///
    /// Keyboard model:
    ///   • Tab cycles through row borders + footer controls
    ///   • Enter or Space on a focused row selects it
    ///   • Esc / Close button dismisses without selecting (DialogResult = false)
    /// </summary>
    public partial class CataloguePickerDialog : Window
    {
        public CatalogueEntry? SelectedEntry { get; private set; }

        private readonly string? _htVideoId;

        public CataloguePickerDialog(List<CatalogueEntry> entries, string? htVideoId)
        {
            InitializeComponent();
            Services.WindowChromeHelper.ApplyDarkTitleBar(this);

            _htVideoId = htVideoId;

            TxtSubtitle.Text = Loc.GetF("dialog_catalogue_picker_subtitle_fmt", entries.Count);

            foreach (var entry in entries)
            {
                EntriesList.Items.Add(BuildEntryRow(entry));
            }

            // Esc to dismiss (in addition to the IsCancel="True" on BtnClose).
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    SelectedEntry = null;
                    DialogResult = false;
                    Close();
                }
            };
        }

        private Border BuildEntryRow(CatalogueEntry entry)
        {
            // Whole row is one focusable, clickable Border with a faux button
            // role for screen readers. Inline thumbnail (left), text body (center),
            // metadata footer (bottom).
            var row = new Border
            {
                Background = (Brush)FindResource("PanelBgBrush"),
                BorderBrush = (Brush)FindResource("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Focusable = true,
            };
            AutomationProperties.SetName(row,
                $"{entry.Title} {(string.IsNullOrEmpty(entry.RemixerName)
                    ? Loc.GetF("dialog_catalogue_picker_by_fmt", entry.CreatorName)
                    : Loc.GetF("dialog_catalogue_picker_remix_by_fmt", entry.RemixerName))}");

            row.MouseLeftButtonUp += (_, _) => Select(entry);
            row.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    Select(entry);
                }
            };
            row.MouseEnter += (_, _) =>
                row.Background = (Brush)FindResource("DeeperAccentTransparent20Brush");
            row.MouseLeave += (_, _) =>
                row.Background = (Brush)FindResource("PanelBgBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Thumbnail (or placeholder when the server didn't send one).
            grid.Children.Add(BuildThumbnail(entry));

            var textStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(textStack, 1);

            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Title) ? "Untitled" : entry.Title,
                Foreground = (Brush)FindResource("TextLightBrush"),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            textStack.Children.Add(title);

            var byline = new TextBlock
            {
                Text = string.IsNullOrEmpty(entry.RemixerName)
                    ? Loc.GetF("dialog_catalogue_picker_by_fmt", entry.CreatorName)
                    : Loc.GetF("dialog_catalogue_picker_remix_by_fmt", entry.RemixerName),
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC0)),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
            };
            textStack.Children.Add(byline);

            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                var desc = new TextBlock
                {
                    Text = entry.Description,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 38, // ~2 lines at 13pt
                    Margin = new Thickness(0, 6, 0, 0),
                };
                textStack.Children.Add(desc);
            }

            if (entry.Tags != null && entry.Tags.Count > 0)
            {
                var tagWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var tag in entry.Tags)
                {
                    tagWrap.Children.Add(new Border
                    {
                        Background = (Brush)FindResource("DeeperAccentTransparent20Brush"),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(0, 0, 6, 4),
                        Child = new TextBlock
                        {
                            Text = tag,
                            FontSize = 11,
                            Foreground = (Brush)FindResource("TextLightBrush"),
                        },
                    });
                }
                textStack.Children.Add(tagWrap);
            }

            // Footer row: view count + license, separated visually from body
            // copy by a thin gap.
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
            };
            footer.Children.Add(new TextBlock
            {
                Text = $"👁 {entry.ViewCount}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0xA8)),
                Margin = new Thickness(0, 0, 14, 0),
            });
            footer.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.License)
                    ? Loc.Get("dialog_catalogue_picker_no_license")
                    : entry.License,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0xA8)),
            });
            textStack.Children.Add(footer);

            grid.Children.Add(textStack);
            row.Child = grid;
            return row;
        }

        // Build the 80x50 thumbnail tile. The catalogue API may hand us either
        // a full URL (preferred) or a storage path; for the path case we leave
        // the placeholder up because CCP doesn't bundle the Supabase URL as a
        // client-side constant. When/if the by-ht-url endpoint starts handing
        // back full thumbnail URLs server-side, this branch lights up
        // automatically.
        private UIElement BuildThumbnail(CatalogueEntry entry)
        {
            var container = new Border
            {
                Width = 80,
                Height = 50,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = (Brush)FindResource("DeeperAccentTransparent20Brush"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true,
            };

            if (!string.IsNullOrWhiteSpace(entry.ThumbnailPath) &&
                Uri.TryCreate(entry.ThumbnailPath, UriKind.Absolute, out var thumbUri) &&
                (thumbUri.Scheme == Uri.UriSchemeHttp || thumbUri.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    var img = new Image
                    {
                        Stretch = Stretch.UniformToFill,
                        Source = new BitmapImage(thumbUri),
                    };
                    container.Child = img;
                    return container;
                }
                catch
                {
                    // Fall through to placeholder on any decode failure.
                }
            }

            container.Child = new TextBlock
            {
                Text = "▶",
                FontSize = 22,
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return container;
        }

        private void Select(CatalogueEntry entry)
        {
            SelectedEntry = entry;
            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            SelectedEntry = null;
            DialogResult = false;
            Close();
        }

        private void LinkBrowseWeb_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // Browse all on web: deep-link to the catalogue filtered to this
                // HT video. Falls back to the unfiltered catalogue when we
                // couldn't extract an id (defensive — won't happen in practice
                // since the dialog only opens for HT-eligible URLs).
                var url = string.IsNullOrEmpty(_htVideoId)
                    ? "https://app.cclabs.app/catalogue"
                    : $"https://app.cclabs.app/catalogue?video={Uri.EscapeDataString(_htVideoId)}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
                e.Handled = true;
            }
            catch
            {
                // Best-effort — failing to launch the browser shouldn't crash.
            }
        }
    }
}
