using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// "Media Log" recap window (opened from the Assets tab). Shows the app-lifetime history
    /// of flashed images and played videos in a virtualized master list, with a single live
    /// preview pane on the right.
    ///
    /// PORTED from ConditioningControlPanel/Windows/MediaHistoryWindow.xaml.cs. Deviations:
    ///  - <c>App.MediaHistory</c> / <c>MediaLogEntry</c> / <c>MediaHistoryService</c> live in the
    ///    WPF head, so <see cref="LoadRows"/> returns placeholder rows and the EntryAdded /
    ///    Cleared subscriptions and the Clear button are stubs. The filter, search, count and
    ///    preview logic are ported verbatim and run against those rows.
    ///  - Reveal-in-folder and open-file ARE live: WPF's Win32 <c>ExplorerLauncher</c> becomes
    ///    <c>UseShellExecute</c>, which is ShellExecute on Windows and xdg-open on Linux. Only the
    ///    SELECT-the-file half of Explorer's behaviour is lost.
    ///  - <see cref="MediaHistoryRow"/> takes the fields it formats rather than a
    ///    <c>MediaLogEntry</c>, for the same reason. Restoring the model constructor when it
    ///    reaches Core is a one-liner.
    ///  - <c>Visibility</c> -> <c>IsVisible</c>; <c>DragMove()</c> -> <c>BeginMoveDrag(e)</c>;
    ///    <c>Application.Current.TryFindResource</c> -> <c>this.TryFindResource</c>.
    ///  - The video preview (WPF's <c>MediaElement</c>) and the animated-GIF branch
    ///    (XamlAnimatedGif) have no Avalonia equivalent and no package may be added; both fall
    ///    through to the still-image path.
    ///  - <c>DisplayPath</c> normalised to backslashes; here it normalises to the platform
    ///    separator, or the same call would mangle every Linux path.
    ///  - The row's open-in-folder Click moves out of the DataTemplate onto the ListBox: template
    ///    content has no name scope to bind a markup handler through. The Tag still carries the row.
    /// </summary>
    public partial class MediaHistoryWindow : Window
    {
        private readonly List<MediaHistoryRow> _allRows = new();          // newest first, unfiltered
        private readonly ObservableCollection<MediaHistoryRow> _view = new();
        private string _filter = "all";       // all | image | video
        private string _search = "";

        private readonly ListBox _mediaList;
        private readonly TextBox _txtSearch;
        private readonly TextBlock _txtCount, _txtEmpty, _searchPlaceholder;
        private readonly TextBlock _previewHint, _previewMissing, _previewName, _previewPath;
        private readonly Image _previewImage;
        private readonly Button _btnFilterAll, _btnFilterImages, _btnFilterVideos;
        private readonly Button _btnPreviewOpenFolder, _btnPreviewOpenFile;

        public MediaHistoryWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _mediaList = this.FindControl<ListBox>("MediaList")!;
            _txtSearch = this.FindControl<TextBox>("TxtSearch")!;
            _txtCount = this.FindControl<TextBlock>("TxtCount")!;
            _txtEmpty = this.FindControl<TextBlock>("TxtEmpty")!;
            _searchPlaceholder = this.FindControl<TextBlock>("SearchPlaceholder")!;
            _previewHint = this.FindControl<TextBlock>("PreviewHint")!;
            _previewMissing = this.FindControl<TextBlock>("PreviewMissing")!;
            _previewName = this.FindControl<TextBlock>("PreviewName")!;
            _previewPath = this.FindControl<TextBlock>("PreviewPath")!;
            _previewImage = this.FindControl<Image>("PreviewImage")!;
            _btnFilterAll = this.FindControl<Button>("BtnFilterAll")!;
            _btnFilterImages = this.FindControl<Button>("BtnFilterImages")!;
            _btnFilterVideos = this.FindControl<Button>("BtnFilterVideos")!;
            _btnPreviewOpenFolder = this.FindControl<Button>("BtnPreviewOpenFolder")!;
            _btnPreviewOpenFile = this.FindControl<Button>("BtnPreviewOpenFile")!;

            _mediaList.ItemsSource = _view;
            _mediaList.SelectionChanged += MediaList_SelectionChanged;
            _txtSearch.TextChanged += Search_TextChanged;

            // Handlers live here rather than in markup, per the porting convention.
            _btnFilterAll.Click += Filter_Click;
            _btnFilterImages.Click += Filter_Click;
            _btnFilterVideos.Click += Filter_Click;
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnClear")!.Click += (_, _) => BtnClear_Click();
            _btnPreviewOpenFolder.Click += (_, _) => RevealSelected();
            _btnPreviewOpenFile.Click += (_, _) => OpenSelectedFile();

            // One handler on the list instead of one inside the DataTemplate; Click bubbles.
            _mediaList.AddHandler(Button.ClickEvent, OpenFolder_Click);
            this.FindControl<DockPanel>("HeaderBar")!.PointerPressed += Header_PointerPressed;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            foreach (var row in LoadRows())
                _allRows.Add(row);

            RebuildView();
            UpdateFilterButtons();

            // Show the populated preview state rather than the hint; WPF got here on the first
            // click, and the render proof only ever sees the loaded window.
            if (_view.Count > 0) _mediaList.SelectedIndex = 0;
        }

        /// <summary>
        /// WPF read <c>App.MediaHistory.GetSnapshot()</c> and subscribed to EntryAdded / Cleared.
        /// ponytail: needs MediaHistoryService, wired when it moves to Core. Until then these
        /// placeholder rows let the list, the filters and the preview all draw their real states.
        /// </summary>
        private static List<MediaHistoryRow> LoadRows()
        {
            var now = DateTime.Now;
            return new List<MediaHistoryRow>
            {
                new("/home/user/Assets/images/soft-pink-01.gif", "soft-pink-01.gif", now.AddMinutes(-2), isVideo: false),
                new("/home/user/Assets/videos/deep-spiral.mp4", "deep-spiral.mp4", now.AddMinutes(-9), isVideo: true),
                new("/home/user/Assets/images/mantra-card-04.png", "mantra-card-04.png", now.AddMinutes(-21), isVideo: false),
                new("/home/user/Assets/videos/loop-trance.webm", "loop-trance.webm", now.AddHours(-3), isVideo: true),
                new("/home/user/Assets/images/glow-07.jpg", "glow-07.jpg", now.AddDays(-1), isVideo: false),
                new("/home/user/Assets/images/sink-deeper.png", "sink-deeper.png", now.AddDays(-4), isVideo: false),
            };
        }

        // ---- Filtering / search ------------------------------------------

        private bool PassesFilter(MediaHistoryRow row)
        {
            if (_filter == "image" && row.IsVideo) return false;
            if (_filter == "video" && !row.IsVideo) return false;
            if (!string.IsNullOrEmpty(_search) &&
                row.DisplayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return true;
        }

        private void RebuildView()
        {
            _view.Clear();
            foreach (var row in _allRows)
                if (PassesFilter(row)) _view.Add(row);

            bool empty = _view.Count == 0;
            _txtEmpty.IsVisible = empty;
            _mediaList.IsVisible = !empty;
            UpdateCount();
        }

        private void UpdateCount()
        {
            int total = _allRows.Count;
            int shown = _view.Count;
            _txtCount.Text = shown == total
                ? Loc.GetF("label_media_entry_count", total)
                : Loc.GetF("label_media_entry_count_filtered", shown, total);
        }

        private void Filter_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is string tag)
            {
                _filter = tag;
                RebuildView();
                UpdateFilterButtons();
            }
        }

        private void UpdateFilterButtons()
        {
            SetActive(_btnFilterAll, _filter == "all");
            SetActive(_btnFilterImages, _filter == "image");
            SetActive(_btnFilterVideos, _filter == "video");
        }

        private void SetActive(Button btn, bool active)
        {
            btn.Background = active
                ? (this.TryFindResource("PinkBrush", out var pink) && pink is IBrush b
                    ? b
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)))
                : new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x40));
            btn.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xE8));
        }

        private void Search_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _search = _txtSearch.Text?.Trim() ?? "";
            _searchPlaceholder.IsVisible = string.IsNullOrEmpty(_txtSearch.Text);
            RebuildView();
        }

        // ---- Preview ------------------------------------------------------

        private void MediaList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_mediaList.SelectedItem is MediaHistoryRow row)
                ShowPreview(row);
            else
                ShowPreviewNone();
        }

        private void ShowPreview(MediaHistoryRow row)
        {
            StopPreview();
            _previewHint.IsVisible = false;
            _previewName.Text = row.DisplayName;
            _previewPath.Text = DisplayPath(row.FilePath);

            bool exists = row.FileExists;
            _btnPreviewOpenFolder.IsEnabled = exists;
            _btnPreviewOpenFile.IsEnabled = exists;

            if (!exists)
            {
                _previewImage.IsVisible = false;
                _previewMissing.IsVisible = true;
                return;
            }
            _previewMissing.IsVisible = false;

            // ponytail: the video and animated-GIF branches need a media stack (WPF used
            // MediaElement and XamlAnimatedGif); wired when one is chosen for the Avalonia head.
            if (row.IsVideo)
            {
                _previewImage.IsVisible = false;
                _previewMissing.IsVisible = true;
                return;
            }

            try
            {
                _previewImage.IsVisible = true;
                using var stream = File.OpenRead(row.FilePath);
                // WPF capped the single preview with DecodePixelWidth=720; never full-res.
                _previewImage.Source = global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 720);
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("MediaHistoryWindow: preview failed for {Path}: {Error}", row.FilePath, ex.Message);
                _previewImage.IsVisible = false;
                _previewMissing.IsVisible = true;
            }
        }

        private void ShowPreviewNone()
        {
            _previewHint.IsVisible = true;
            _previewImage.IsVisible = false;
            _previewMissing.IsVisible = false;
            _previewName.Text = "";
            _previewPath.Text = "";
            _btnPreviewOpenFolder.IsEnabled = false;
            _btnPreviewOpenFile.IsEnabled = false;
        }

        private void StopPreview()
        {
            try { _previewImage.Source = null; } catch { }
        }

        // ---- Open in the file manager -------------------------------------

        private void OpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            if ((e.Source as Control)?.Tag is MediaHistoryRow row)
                RevealInExplorer(row.FilePath);
        }

        private void RevealSelected()
        {
            if (_mediaList.SelectedItem is MediaHistoryRow row)
                RevealInExplorer(row.FilePath);
        }

        /// <summary>
        /// WPF's <c>Helpers.ExplorerLauncher.RevealInExplorer</c> SELECTS the file in Explorer,
        /// which is a Win32 shell call and stays in that head. The portable half is opening the
        /// containing folder through the desktop's own handler - the same thing
        /// <see cref="SessionCompleteWindow"/> does, and the same #998 fallback: when the file is
        /// gone the folder is still worth opening.
        /// </summary>
        private static void RevealInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch (Exception ex) { Log.Warning(ex, "MediaHistoryWindow: failed to open folder for {Path}", path); }
        }

        /// <summary>Opens the selected file in whatever the desktop associates with it -
        /// <c>UseShellExecute</c> is ShellExecute on Windows and xdg-open on Linux, so this needs no
        /// per-head helper. A missing file is silently ignored, as the WPF shell call was.</summary>
        private void OpenSelectedFile()
        {
            if (_mediaList.SelectedItem is not MediaHistoryRow row) return;
            if (string.IsNullOrEmpty(row.FilePath) || !File.Exists(row.FilePath)) return;
            try { Process.Start(new ProcessStartInfo { FileName = row.FilePath, UseShellExecute = true }); }
            catch (Exception ex) { Log.Warning(ex, "MediaHistoryWindow: failed to open {Path}", row.FilePath); }
        }

        /// <summary>ponytail: the confirm half is available now - Dialogs.MessageDialog.ConfirmAsync
        /// with confirm_clear_media_log - but there is nothing to clear: the log itself is
        /// MediaHistoryService, still in the WPF head (ConditioningControlPanel/Services/Media/),
        /// so this view's rows come from a local placeholder. Asking "clear the log?" and then
        /// clearing nothing would be a button that lies, so the whole handler stays a no-op until
        /// the service reaches Core.</summary>
        private void BtnClear_Click() { }

        /// <summary>
        /// Paths reach the log from a mix of sources, so a stored path can carry the wrong
        /// separator and read back as "D:/Assets/images\personal\x.gif" (#1108). Display only -
        /// the stored path is left alone. WPF hardcoded '\\'; this normalises to whatever the
        /// platform uses, or the same line would mangle every Linux path.
        /// </summary>
        private static string DisplayPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return Path.DirectorySeparatorChar == '\\' ? path.Replace('/', '\\') : path.Replace('\\', '/');
        }

        // ---- Chrome -------------------------------------------------------

        private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                try { BeginMoveDrag(e); } catch { }
            }
        }
    }

    /// <summary>
    /// Lightweight view-model for one history row. All display fields are precomputed once (rows
    /// are immutable), keeping the virtualized list cheap. Top-level rather than nested so
    /// <c>x:DataType</c> can name it.
    /// </summary>
    public sealed class MediaHistoryRow
    {
        public string FilePath { get; }
        public bool IsVideo { get; }
        public string DisplayName { get; }
        public string TimeText { get; }
        public string TypeBadge { get; }
        public string PlaceholderGlyph { get; }
        public IBrush BadgeBrush { get; }
        public bool FileExists => SafeExists(FilePath);

        public MediaHistoryRow(string filePath, string? displayName, DateTime timestamp, bool isVideo)
        {
            FilePath = filePath;
            IsVideo = isVideo;
            DisplayName = string.IsNullOrEmpty(displayName) ? SafeName(filePath) : displayName!;
            TimeText = FormatTime(timestamp);

            if (isVideo)
            {
                TypeBadge = Loc.Get("badge_video");
                PlaceholderGlyph = "🎬";
                BadgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x6C, 0xD0));
            }
            else
            {
                TypeBadge = Loc.Get("badge_image");
                PlaceholderGlyph = "🖼";
                BadgeBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x50, 0x9C));
            }
        }

        private static string FormatTime(DateTime t)
        {
            var now = DateTime.Now;
            if (t.Date == now.Date) return t.ToString("HH:mm:ss");
            if (t.Date == now.Date.AddDays(-1)) return Loc.Get("label_yesterday") + " " + t.ToString("HH:mm");
            return t.ToString("MMM d, HH:mm");
        }

        private static string SafeName(string path)
        {
            try { return Path.GetFileName(path) ?? path; } catch { return path; }
        }

        private static bool SafeExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path); } catch { return false; }
        }
    }
}
