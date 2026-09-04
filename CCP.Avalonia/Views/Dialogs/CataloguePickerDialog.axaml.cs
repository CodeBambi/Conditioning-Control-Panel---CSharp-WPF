using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
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
    ///   • Esc / Close button dismisses without selecting (result = false)
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/CataloguePickerDialog.xaml.cs. Deviations:
    ///  - WPF's <c>DialogResult</c> becomes <c>Close(bool)</c>, because Avalonia carries the result
    ///    through <c>ShowDialog&lt;bool?&gt;</c> (same shape as the ported TextEditorDialog).
    ///  - <c>MouseLeftButtonUp</c> -> <c>PointerReleased</c>, <c>MouseEnter/MouseLeave</c> ->
    ///    <c>PointerEntered/PointerExited</c>, <c>PreviewKeyDown</c> -> <c>KeyDown</c> with
    ///    <c>RoutingStrategies.Tunnel</c>.
    ///  - <c>WindowChromeHelper.ApplyDarkTitleBar</c> is a Win32 head service and is dropped:
    ///    the window has no system decorations to darken.
    ///  - The remote thumbnail is not fetched (see <see cref="BuildThumbnail"/>); the placeholder
    ///    tile the WPF original also shows is what draws.
    ///  - <see cref="CatalogueEntry"/> is copied from
    ///    ConditioningControlPanel/Services/CatalogueLookupService.cs: the record lives in the WPF
    ///    head, not CCP.Core, and neither may be touched by this port.
    ///
    /// <para><b>No opener yet, and the blocker is the CALLER.</b> WPF reaches this from
    /// <c>MainWindow.OpenCataloguePickerDialog</c> (MainWindow.DeeperTab.cs:1025), itself the
    /// "pick one" action on a toast raised by <c>ShowCatalogueLookupToast</c> after
    /// <c>RunCatalogueLookupAsync</c> matched the browser's current HypnoTube video. Both ends are
    /// head-side — <c>App.CatalogueLookup</c> (Services/CatalogueLookupService.cs) for the lookup
    /// AND the download, <c>App.Notifications</c> for the toast — and the dialog exists only to
    /// feed <c>DownloadAndOpenCatalogueEntryAsync</c>. Opening it with entries nobody can fetch and
    /// a selection nobody can download would be a picker that picks nothing. It arrives with the
    /// lookup service, which is the same wait MainShellWindow.DeeperTab.cs records.</para>
    /// </summary>
    public partial class CataloguePickerDialog : Window
    {
        public CatalogueEntry? SelectedEntry { get; private set; }

        private readonly ItemsControl _entriesList;

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        internal CataloguePickerDialog() : this(SampleEntries(), "ht-9f2c41") { }

        public CataloguePickerDialog(List<CatalogueEntry> entries, string? htVideoId)
        {
            AvaloniaXamlLoader.Load(this);

            _entriesList = this.FindControl<ItemsControl>("EntriesList")!;

            this.FindControl<TextBlock>("TxtSubtitle")!.Text =
                Loc.GetF("dialog_catalogue_picker_subtitle_fmt", entries.Count);

            // Browse all on web: deep-link to the catalogue filtered to this HT video. Falls back
            // to the unfiltered catalogue when we couldn't extract an id (defensive — won't happen
            // in practice since the dialog only opens for HT-eligible URLs). HyperlinkButton opens
            // it through the platform launcher, which is what Process.Start(UseShellExecute) did.
            this.FindControl<HyperlinkButton>("LinkBrowseWeb")!.NavigateUri = new Uri(
                string.IsNullOrEmpty(htVideoId)
                    ? "https://app.cclabs.app/catalogue"
                    : $"https://app.cclabs.app/catalogue?video={Uri.EscapeDataString(htVideoId)}");

            this.FindControl<Button>("BtnClose")!.Click += (_, _) =>
            {
                SelectedEntry = null;
                Close(false);
            };

            var rows = new List<Control>();
            foreach (var entry in entries)
                rows.Add(BuildEntryRow(entry));
            _entriesList.ItemsSource = rows;

            // Esc to dismiss (WPF got this from PreviewKeyDown plus IsCancel="True" on BtnClose;
            // Avalonia has neither, so one tunnelling handler covers both).
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    SelectedEntry = null;
                    Close(false);
                }
            }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private IBrush Res(string key) => (IBrush)this.FindResource(key)!;

        private Border BuildEntryRow(CatalogueEntry entry)
        {
            // Whole row is one focusable, clickable Border with a faux button
            // role for screen readers. Inline thumbnail (left), text body (center),
            // metadata footer (bottom).
            var row = new Border
            {
                Background = Res("PanelBgBrush"),
                BorderBrush = Res("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = new Cursor(StandardCursorType.Hand),
                Focusable = true,
            };
            AutomationProperties.SetName(row,
                $"{entry.Title} {(string.IsNullOrEmpty(entry.RemixerName)
                    ? Loc.GetF("dialog_catalogue_picker_by_fmt", entry.CreatorName)
                    : Loc.GetF("dialog_catalogue_picker_remix_by_fmt", entry.RemixerName))}");

            row.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left) Select(entry);
            };
            row.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    Select(entry);
                }
            };
            row.PointerEntered += (_, _) => row.Background = Res("DeeperAccentTransparent20Brush");
            row.PointerExited += (_, _) => row.Background = Res("PanelBgBrush");

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
                Foreground = Res("TextLightBrush"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
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
                        Background = Res("DeeperAccentTransparent20Brush"),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(0, 0, 6, 4),
                        Child = new TextBlock
                        {
                            Text = tag,
                            FontSize = 11,
                            Foreground = Res("TextLightBrush"),
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

        // Build the 80x50 thumbnail tile. The WPF original decoded an http(s)
        // ThumbnailPath straight into a BitmapImage, which downloads on the UI
        // thread; Avalonia's Bitmap takes a stream, so the fetch needs an HTTP
        // client this view is not allowed to reach for yet.
        // ponytail: needs an image-fetch service, wired when it moves to Core.
        // The placeholder below is what the WPF version also shows for the
        // storage-path case, so nothing regresses in the meantime.
        private Control BuildThumbnail(CatalogueEntry entry)
        {
            var container = new Border
            {
                Width = 80,
                Height = 50,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                BorderBrush = Res("DeeperAccentTransparent20Brush"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true,
            };

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
            Close(true);
        }

        private static List<CatalogueEntry> SampleEntries() => new()
        {
            new CatalogueEntry("e1", "Spiral Descent — Deep Focus Mix",
                "A slow ten-minute induction layered over the original audio, with breath cues on the downbeat.",
                "velvet", null, new List<string> { "induction", "spiral", "slow" }, "CC BY-SA 4.0", 1842,
                "https://hypnotube.example/v/9f2c41", null, "https://app.cclabs.app/files/e1.ccp"),
            new CatalogueEntry("e2", "Spiral Descent — Trigger Remix",
                "Adds three keyword triggers and a shorter tail. Built on velvet's mix.",
                "velvet", "mirrorfade", new List<string> { "triggers", "remix" }, null, 613,
                "https://hypnotube.example/v/9f2c41", null, "https://app.cclabs.app/files/e2.ccp"),
            new CatalogueEntry("e3", "Quiet Version", "", "cinder", null,
                new List<string>(), "CC BY-NC 4.0", 97,
                "https://hypnotube.example/v/9f2c41", null, "https://app.cclabs.app/files/e3.ccp"),
        };
    }

    /// <summary>
    /// One catalogue entry as returned by /api/enhancements/by-ht-url. All
    /// fields are server-truth; the client doesn't enrich or transform
    /// (except for graceful defaults when fields are missing).
    ///
    /// Copied verbatim from ConditioningControlPanel/Services/CatalogueLookupService.cs — the
    /// record lives in the WPF head and this port may reference neither it nor CCP.Core for it.
    /// Delete this copy when CatalogueLookupService moves to Core.
    /// </summary>
    public record CatalogueEntry(
        string Id,
        string Title,
        string Description,
        string CreatorName,
        string? RemixerName,
        List<string> Tags,
        string? License,
        int ViewCount,
        string HtUrl,
        string? ThumbnailPath,
        string FileUrl
    );
}
