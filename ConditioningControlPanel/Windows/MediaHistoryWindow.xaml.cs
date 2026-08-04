using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using XamlAnimatedGif;

namespace ConditioningControlPanel
{
    /// <summary>
    /// "Media Log" recap window (opened from the Assets tab). Shows the app-lifetime history
    /// of flashed images and played videos in a virtualized master list, with a single live
    /// preview pane on the right. Only visible rows decode a tiny thumbnail (see
    /// <see cref="MediaThumbnailConverter"/>) and only ONE full media plays at a time, so the
    /// window stays cheap even with the full 500-entry history loaded.
    /// </summary>
    public partial class MediaHistoryWindow : Window
    {
        private readonly List<MediaHistoryRow> _allRows = new();          // newest first, unfiltered
        private readonly ObservableCollection<MediaHistoryRow> _view = new();
        private string _filter = "all";       // all | image | video
        private string _search = "";
        private bool _subscribed;

        public MediaHistoryWindow()
        {
            InitializeComponent();
            MediaList.ItemsSource = _view;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            var snapshot = App.MediaHistory?.GetSnapshot() ?? new List<MediaLogEntry>();
            foreach (var entry in snapshot)
                _allRows.Add(new MediaHistoryRow(entry));

            RebuildView();
            UpdateFilterButtons();

            if (App.MediaHistory != null)
            {
                App.MediaHistory.EntryAdded += OnEntryAdded;
                App.MediaHistory.Cleared += OnHistoryCleared;
                _subscribed = true;
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_subscribed && App.MediaHistory != null)
            {
                App.MediaHistory.EntryAdded -= OnEntryAdded;
                App.MediaHistory.Cleared -= OnHistoryCleared;
            }
            StopPreview();
        }

        // ---- Live updates -------------------------------------------------

        private void OnEntryAdded(object? sender, MediaLogEntry entry)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnEntryAdded(sender, entry)));
                return;
            }
            var row = new MediaHistoryRow(entry);
            _allRows.Insert(0, row);
            if (_allRows.Count > MediaHistoryService.MaxEntries)
                _allRows.RemoveAt(_allRows.Count - 1);

            if (PassesFilter(row))
                _view.Insert(0, row);

            UpdateCount();
        }

        private void OnHistoryCleared(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnHistoryCleared(sender, e)));
                return;
            }
            _allRows.Clear();
            RebuildView();
            StopPreview();
            ShowPreviewNone();
        }

        // ---- Filtering / search ------------------------------------------

        private bool PassesFilter(MediaHistoryRow row)
        {
            if (_filter == "image" && row.Entry.Type != MediaType.Image) return false;
            if (_filter == "video" && row.Entry.Type != MediaType.Video) return false;
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
            TxtEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            MediaList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            UpdateCount();
        }

        private void UpdateCount()
        {
            int total = _allRows.Count;
            int shown = _view.Count;
            TxtCount.Text = shown == total
                ? Localization.Loc.GetF("label_media_entry_count", total)
                : Localization.Loc.GetF("label_media_entry_count_filtered", shown, total);
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag)
            {
                _filter = tag;
                RebuildView();
                UpdateFilterButtons();
            }
        }

        private void UpdateFilterButtons()
        {
            SetActive(BtnFilterAll, _filter == "all");
            SetActive(BtnFilterImages, _filter == "image");
            SetActive(BtnFilterVideos, _filter == "video");
        }

        private static void SetActive(System.Windows.Controls.Button btn, bool active)
        {
            btn.Background = active
                ? (Brush)(Application.Current.TryFindResource("PinkBrush") ?? new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)))
                : new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x40));
            btn.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xE8));
        }

        private void Search_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _search = TxtSearch.Text?.Trim() ?? "";
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
            RebuildView();
        }

        // ---- Preview ------------------------------------------------------

        private void MediaList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (MediaList.SelectedItem is MediaHistoryRow row)
                ShowPreview(row);
            else
                ShowPreviewNone();
        }

        private void ShowPreview(MediaHistoryRow row)
        {
            StopPreview();
            PreviewHint.Visibility = Visibility.Collapsed;
            PreviewName.Text = row.DisplayName;
            PreviewPath.Text = row.Entry.FilePath;

            bool exists = row.FileExists;
            BtnPreviewOpenFolder.IsEnabled = exists;
            BtnPreviewOpenFile.IsEnabled = exists;

            if (!exists)
            {
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewVideo.Visibility = Visibility.Collapsed;
                PreviewMissing.Visibility = Visibility.Visible;
                return;
            }
            PreviewMissing.Visibility = Visibility.Collapsed;

            try
            {
                var uri = new Uri(row.Entry.FilePath, UriKind.Absolute);
                if (row.Entry.Type == MediaType.Video)
                {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewVideo.Visibility = Visibility.Visible;
                    PreviewVideo.Source = uri;
                    PreviewVideo.IsMuted = true;
                    PreviewVideo.Play();
                }
                else if (IsGif(row.Entry.FilePath))
                {
                    PreviewVideo.Visibility = Visibility.Collapsed;
                    PreviewImage.Visibility = Visibility.Visible;
                    AnimationBehavior.SetSourceUri(PreviewImage, uri);
                    AnimationBehavior.SetRepeatBehavior(PreviewImage, RepeatBehavior.Forever);
                }
                else
                {
                    PreviewVideo.Visibility = Visibility.Collapsed;
                    PreviewImage.Visibility = Visibility.Visible;
                    AnimationBehavior.SetSourceUri(PreviewImage, null);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bmp.DecodePixelWidth = 720; // cap the single preview; never full-res
                    bmp.UriSource = uri;
                    bmp.EndInit();
                    bmp.Freeze();
                    PreviewImage.Source = bmp;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MediaHistoryWindow: preview failed for {Path}: {Error}", row.Entry.FilePath, ex.Message);
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewVideo.Visibility = Visibility.Collapsed;
                PreviewMissing.Visibility = Visibility.Visible;
            }
        }

        private void ShowPreviewNone()
        {
            PreviewHint.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewVideo.Visibility = Visibility.Collapsed;
            PreviewMissing.Visibility = Visibility.Collapsed;
            PreviewName.Text = "";
            PreviewPath.Text = "";
            BtnPreviewOpenFolder.IsEnabled = false;
            BtnPreviewOpenFile.IsEnabled = false;
        }

        private void StopPreview()
        {
            try
            {
                PreviewVideo.Stop();
                PreviewVideo.Source = null;
            }
            catch { }
            try
            {
                AnimationBehavior.SetSourceUri(PreviewImage, null);
                PreviewImage.Source = null;
            }
            catch { }
        }

        private void PreviewVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop the preview.
            try { PreviewVideo.Position = TimeSpan.Zero; PreviewVideo.Play(); } catch { }
        }

        private void PreviewVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            PreviewVideo.Visibility = Visibility.Collapsed;
            PreviewMissing.Visibility = Visibility.Visible;
        }

        private static bool IsGif(string path)
        {
            try { return string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // ---- Open in Explorer --------------------------------------------

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is MediaHistoryRow row)
                RevealInExplorer(row.Entry.FilePath);
        }

        private void PreviewOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (MediaList.SelectedItem is MediaHistoryRow row)
                RevealInExplorer(row.Entry.FilePath);
        }

        private void PreviewOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (MediaList.SelectedItem is not MediaHistoryRow row) return;
            try
            {
                if (!File.Exists(row.Entry.FilePath)) return;
                Process.Start(new ProcessStartInfo(row.Entry.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaHistoryWindow: open file failed");
            }
        }

        private void RevealInExplorer(string path)
        {
            try
            {
                if (File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else
                {
                    // File gone - just open the containing folder if it still exists.
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        Process.Start("explorer.exe", $"\"{dir}\"");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaHistoryWindow: reveal in explorer failed");
            }
        }

        // ---- Chrome -------------------------------------------------------

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Localization.Loc.Get("confirm_clear_media_log"),
                Localization.Loc.Get("dialog_media_log"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                App.MediaHistory?.Clear();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        /// <summary>Lightweight view-model for one history row. All display fields are
        /// precomputed once (rows are immutable), keeping the virtualized list cheap.</summary>
        private class MediaHistoryRow
        {
            public MediaLogEntry Entry { get; }
            public string DisplayName { get; }
            public string TimeText { get; }
            public string TypeBadge { get; }
            public string PlaceholderGlyph { get; }
            public Brush BadgeBrush { get; }
            public bool FileExists => SafeExists(Entry.FilePath);

            public MediaHistoryRow(MediaLogEntry entry)
            {
                Entry = entry;
                DisplayName = string.IsNullOrEmpty(entry.DisplayName) ? SafeName(entry.FilePath) : entry.DisplayName;
                TimeText = FormatTime(entry.Timestamp);

                if (entry.Type == MediaType.Video)
                {
                    TypeBadge = Localization.Loc.Get("badge_video");
                    PlaceholderGlyph = "🎬";
                    BadgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x6C, 0xD0));
                }
                else
                {
                    TypeBadge = Localization.Loc.Get("badge_image");
                    PlaceholderGlyph = "🖼";
                    BadgeBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x50, 0x9C));
                }
            }

            private static string FormatTime(DateTime t)
            {
                var now = DateTime.Now;
                if (t.Date == now.Date) return t.ToString("HH:mm:ss");
                if (t.Date == now.Date.AddDays(-1)) return Localization.Loc.Get("label_yesterday") + " " + t.ToString("HH:mm");
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
}
