using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Z8 · HER LIBRARY — the per-mod Hypnotube link pool editor. See the XAML header.
    ///
    /// <para>WPF keeps the editor on MainWindow (MainWindow.xaml.cs:3164-3383 RefreshHypnotubeLinksUI,
    /// BtnAddVideoLink_Click, AddVideoLinkRow, PersistVideoLinks, UpdateNoVideoLinksPlaceholder) and
    /// the cell forwards its click there. Everything it touches is in Core here (CoreMods.Service's
    /// GetVideoLinks/SetUserVideoLinks, <see cref="VideoLinkPool"/>), so the cell owns it.</para>
    ///
    /// <para>No AvatarTubeWindow.ReloadVideoLinks: WPF caches the pool in a static the tube links
    /// from; every reader on this head (BambiSprite, MediaCommand, CompanionAskService) reads
    /// CoreMods.Service.GetVideoLinks() live, so a save is visible at once.</para>
    /// </summary>
    public partial class WorkshopLibraryCell : UserControl
    {
        private static readonly IBrush UrlBrush = new SolidColorBrush(Color.FromRgb(120, 200, 255));
        private static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x5A));
        private static readonly IBrush BoxBorder = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80));
        private static readonly IBrush BoxBack = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E));
        private static readonly IBrush RemoveBrush = new SolidColorBrush(Color.FromRgb(255, 100, 100));
        private const string OpenGlyph = "↗";
        private const string WarnGlyph = "⚠";

        private readonly StackPanel _pool;
        private readonly TextBlock _noLinks;
        private readonly TextBlock _modeLabel;
        internal readonly List<(TextBox NameBox, TextBox UrlBox)> Rows = new();

        public WorkshopLibraryCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopLibraryCellViewModel();
            _pool = this.FindControl<StackPanel>("VideoLinkPoolPanel")!;
            _noLinks = this.FindControl<TextBlock>("TxtNoVideoLinks")!;
            _modeLabel = this.FindControl<TextBlock>("TxtHypnotubeModeLabel")!;

            // CompanionWheelRelay is NOT ported: Avalonia chains an unusable wheel notch to the
            // parent itself (ScrollViewer.IsScrollChainingEnabled, default true).
            this.FindControl<Button>("BtnAddVideoLink")!.Click += (_, _) => AddVideoLink();
        }

        // WPF rebuilds at startup and on every mod switch (MainWindow.xaml.cs:3150/3642). Rebuilding
        // on attach covers startup and a return to the tab; ModChanged covers a switch while shown.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            CoreMods.ModChanged += OnModChanged;
            RefreshPool();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreMods.ModChanged -= OnModChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnModChanged(object? sender, ModPackage e)
            => global::Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPool);

        /// <summary>WPF RefreshHypnotubeLinksUI: the mode label, then the rows from the active
        /// mod's pool (user override, else shipped links), dropping browse/listing pages.</summary>
        internal void RefreshPool()
        {
            _modeLabel.Text = CoreSettings.Current?.ContentModeDisplay ?? "CCP Default";
            Rows.Clear();
            _pool.Children.Clear();
            _pool.Children.Add(_noLinks);
            var links = CoreMods.Service?.GetVideoLinks();
            if (links != null)
                foreach (var kvp in links)
                    if (!VideoLinkPool.IsListingUrl(kvp.Value)) AddRow(kvp.Key, kvp.Value);
            UpdatePlaceholder();
        }

        /// <summary>WPF BtnAddVideoLink_Click: a blank row, caret in the URL box.</summary>
        internal void AddVideoLink()
        {
            var row = AddRow("", "");
            UpdatePlaceholder();
            row.UrlBox.Focus();
        }

        private (TextBox NameBox, TextBox UrlBox) AddRow(string name, string url)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4), ColumnDefinitions = new ColumnDefinitions("*,8,2*,Auto,Auto") };

            var nameBox = MakeBox(name, isUrl: false);
            ToolTip.SetTip(nameBox, Loc.Get("tooltip_video_link_name_optional"));
            row.Children.Add(nameBox);

            var urlBox = MakeBox(url, isUrl: true);
            Grid.SetColumn(urlBox, 2);
            row.Children.Add(urlBox);

            // Preview: open the link externally so the user can check it (HTTPS only).
            var openBtn = MakeGlyphButton(OpenGlyph, UrlBrush, Loc.Get("tooltip_preview_video_link"));
            openBtn.Click += (_, _) =>
            {
                var u = urlBox.Text?.Trim() ?? "";
                if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                    _ = Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), u);
            };
            Grid.SetColumn(openBtn, 3);
            row.Children.Add(openBtn);

            var removeBtn = MakeGlyphButton("🗑", RemoveBrush, Loc.Get("tooltip_remove_video_link"));
            var entry = (NameBox: nameBox, UrlBox: urlBox);
            removeBtn.Click += (_, _) =>
            {
                _pool.Children.Remove(row);
                Rows.Remove(entry);
                Persist();
                UpdatePlaceholder();
            };
            Grid.SetColumn(removeBtn, 4);
            row.Children.Add(removeBtn);

            // Grey the row and flip the preview glyph to a warning when the URL is present but not
            // a usable absolute http(s) link (such rows are dropped on save).
            void UpdateValidity()
            {
                var u = urlBox.Text?.Trim() ?? "";
                bool invalid = u.Length > 0 && !VideoLinkPool.IsUsableUrl(u);
                row.Opacity = invalid ? 0.55 : 1.0;
                urlBox.BorderBrush = invalid ? WarnBrush : BoxBorder;
                ((TextBlock)openBtn.Content!).Text = invalid ? WarnGlyph : OpenGlyph;
                openBtn.Foreground = invalid ? WarnBrush : UrlBrush;
                // Hardcoded English in WPF too (MainWindow.xaml.cs AddVideoLinkRow); kept verbatim.
                ToolTip.SetTip(openBtn, invalid ? "Not a valid http(s) link - this row won't be saved."
                                                : Loc.Get("tooltip_preview_video_link"));
            }
            urlBox.TextChanged += (_, _) => UpdateValidity();
            nameBox.LostFocus += (_, _) => Persist();
            urlBox.LostFocus += (_, _) => Persist();

            Rows.Add(entry);
            _pool.Children.Add(row);
            UpdateValidity();
            return entry;
        }

        private static TextBox MakeBox(string text, bool isUrl) => new()
        {
            Text = text ?? "",
            MaxLength = isUrl ? 500 : 200,
            Background = BoxBack,
            Foreground = isUrl ? UrlBrush : Brushes.White,
            BorderBrush = BoxBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };

        private static Button MakeGlyphButton(string glyph, IBrush fg, string tip)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = glyph, FontSize = 13 },
                Width = 28, Height = 28,
                Background = Brushes.Transparent,
                Foreground = fg,
                BorderThickness = new Thickness(0),
                Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(b, tip);
            return b;
        }

        /// <summary>WPF PersistVideoLinks: the rows become the active mod's override and are saved.</summary>
        internal void Persist()
        {
            var mods = CoreMods.Service;
            if (mods == null) return;
            mods.SetUserVideoLinks(VideoLinkPool.Build(Rows.Select(r => ((string?)r.NameBox.Text, (string?)r.UrlBox.Text))));
            CoreSettings.Save();
        }

        private void UpdatePlaceholder() => _noLinks.IsVisible = Rows.Count == 0;
    }

    public sealed class WorkshopLibraryCellViewModel
    {
        public string LocLabelCurrentMode => Loc.Get("label_current_mode");
        public string LocLabelVideoLinksPoolDesc => Loc.Get("label_video_links_pool_desc");
        public string LocLabelNoVideoLinksYet => Loc.Get("label_no_video_links_yet");
        public string LocBtnAddLinkToPool => Loc.Get("btn_add_link_to_pool");
    }
}
