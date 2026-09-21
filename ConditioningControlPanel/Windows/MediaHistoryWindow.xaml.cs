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
        private int _previewGeneration;   // drops an online decode whose row was deselected

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
            _previewGeneration++;
            PreviewHint.Visibility = Visibility.Collapsed;
            PreviewName.Text = row.DisplayName;

            var plan = MediaHistoryPreviewRules.Plan(
                row.Entry.FilePath, row.Entry.Type, row.FileExists,
                Services.Fyp.Online.RemoteMediaCache.IsCached(row.Entry.FilePath));

            PreviewPath.Text = plan.SourceText;
            BtnPreviewOpenFolder.IsEnabled = plan.CanOpenFolder;
            BtnPreviewOpenFile.IsEnabled = plan.CanOpenFile;
            BtnPreviewOpenFolder.Visibility = plan.IsRemote ? Visibility.Collapsed : Visibility.Visible;
            BtnPreviewOpenFile.Visibility = plan.IsRemote ? Visibility.Collapsed : Visibility.Visible;
            BtnPreviewCopyLink.Visibility = plan.CanCopyLink ? Visibility.Visible : Visibility.Collapsed;
            BtnPreviewOpenSource.Visibility = plan.CanOpenSource ? Visibility.Visible : Visibility.Collapsed;
            BtnPreviewLoad.Visibility = plan.CanLoadPreview ? Visibility.Visible : Visibility.Collapsed;
            BtnPreviewLoad.IsEnabled = plan.CanLoadPreview;

            if (plan.Kind == MediaPreviewKind.LocalMissing || plan.Kind == MediaPreviewKind.RemoteUncached)
            {
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewVideo.Visibility = Visibility.Collapsed;
                PreviewMissing.Text = Localization.Loc.Get(plan.IsRemote
                    ? "label_media_streamed_only"
                    : "label_file_not_found");
                PreviewMissing.Visibility = Visibility.Visible;
                return;
            }
            PreviewMissing.Visibility = Visibility.Collapsed;

            if (plan.Kind == MediaPreviewKind.RemoteCached)
            {
                ShowRemotePreview(row, _previewGeneration);
                return;
            }

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
                PreviewMissing.Text = Localization.Loc.Get("label_file_not_found");
                PreviewMissing.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Draws an online still from whatever <see cref="Services.Fyp.Online.RemoteMediaCache"/>
        /// still holds. <paramref name="fetch"/> false is the free path (bytes are already in
        /// memory, so nothing touches the network); true is the user pressing "Load preview" and
        /// is the only way this window ever reaches out. The generation guard drops a decode whose
        /// row has been deselected in the meantime.
        /// </summary>
        private async void ShowRemotePreview(MediaHistoryRow row, int generation, bool fetch = false)
        {
            var url = row.Entry.FilePath;
            try
            {
                if (fetch)
                {
                    BtnPreviewLoad.IsEnabled = false;
                    PreviewMissing.Text = Localization.Loc.Get("label_media_loading_preview");
                    PreviewMissing.Visibility = Visibility.Visible;
                }

                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var stream = await Services.Fyp.Online.RemoteMediaCache.OpenAsync(url, cts.Token);
                if (generation != _previewGeneration) return;

                // The app's own remote decoder: WIC, then SkiaSharp for the webp a plain Win10
                // box cannot read. Single frame - the preview pane never animates an online item.
                var decoded = stream == null
                    ? null
                    : FlashService.DecodeRemoteStill(url, stream, 720, allowAnimated: false);
                if (generation != _previewGeneration) return;

                if (decoded == null || decoded.Frames.Count == 0)
                {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewVideo.Visibility = Visibility.Collapsed;
                    PreviewMissing.Text = Localization.Loc.Get("label_media_streamed_only");
                    PreviewMissing.Visibility = Visibility.Visible;
                    BtnPreviewLoad.IsEnabled = true;
                    return;
                }

                AnimationBehavior.SetSourceUri(PreviewImage, null);
                PreviewVideo.Visibility = Visibility.Collapsed;
                PreviewImage.Source = decoded.Frames[0];
                PreviewImage.Visibility = Visibility.Visible;
                PreviewMissing.Visibility = Visibility.Collapsed;
                BtnPreviewLoad.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (generation != _previewGeneration) return;
                App.Logger?.Debug("MediaHistoryWindow: online preview failed for {Host}: {Error}",
                    Services.Logging.UrlLog.Host(url), ex.Message);
                PreviewImage.Visibility = Visibility.Collapsed;
                PreviewVideo.Visibility = Visibility.Collapsed;
                PreviewMissing.Text = Localization.Loc.Get("label_media_streamed_only");
                PreviewMissing.Visibility = Visibility.Visible;
                BtnPreviewLoad.IsEnabled = true;
            }
        }

        private void ShowPreviewNone()
        {
            _previewGeneration++;
            PreviewHint.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewVideo.Visibility = Visibility.Collapsed;
            PreviewMissing.Visibility = Visibility.Collapsed;
            PreviewName.Text = "";
            PreviewPath.Text = "";
            BtnPreviewOpenFolder.IsEnabled = false;
            BtnPreviewOpenFile.IsEnabled = false;
            BtnPreviewOpenFolder.Visibility = Visibility.Visible;
            BtnPreviewOpenFile.Visibility = Visibility.Visible;
            BtnPreviewCopyLink.Visibility = Visibility.Collapsed;
            BtnPreviewOpenSource.Visibility = Visibility.Collapsed;
            BtnPreviewLoad.Visibility = Visibility.Collapsed;
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
            // Nothing to reveal for an online item: it never had a file here.
            if (MediaHistoryPreviewRules.IsRemote(path)) return;
            // Helper handles the missing-file fallback to the containing folder (#998).
            Helpers.ExplorerLauncher.RevealInExplorer(path);
        }

        // ---- Online source ------------------------------------------------

        private void PreviewCopyLink_Click(object sender, RoutedEventArgs e)
        {
            if (MediaList.SelectedItem is not MediaHistoryRow row) return;
            try
            {
                Clipboard.SetText(row.Entry.FilePath);
                FlashCopyConfirmation();
            }
            catch (Exception ex)
            {
                // The clipboard is a shared, lockable resource; another app can be holding it.
                App.Logger?.Debug("MediaHistoryWindow: copy link failed: {Error}", ex.Message);
            }
        }

        private void PreviewOpenSource_Click(object sender, RoutedEventArgs e)
        {
            if (MediaList.SelectedItem is not MediaHistoryRow row) return;
            if (!MediaHistoryPreviewRules.IsBrowsable(row.Entry.FilePath)) return;
            Helpers.BrowserLauncher.OpenUrlOrPrompt(row.Entry.FilePath);
        }

        private void PreviewLoad_Click(object sender, RoutedEventArgs e)
        {
            if (MediaList.SelectedItem is not MediaHistoryRow row) return;
            if (!MediaHistoryPreviewRules.IsRemote(row.Entry.FilePath)) return;
            ShowRemotePreview(row, _previewGeneration, fetch: true);
        }

        /// <summary>Two seconds of "Copied" on the button, then back. Cheaper than a toast and the
        /// window has nowhere to put one.</summary>
        private void FlashCopyConfirmation()
        {
            var original = BtnPreviewCopyLink.Content;
            BtnPreviewCopyLink.Content = Localization.Loc.Get("btn_copied");
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                BtnPreviewCopyLink.Content = original;
            };
            timer.Start();
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
            /// <summary>Hidden for online entries: there is no folder of theirs to reveal.</summary>
            public Visibility FolderButtonVisibility { get; }

            public MediaHistoryRow(MediaLogEntry entry)
            {
                Entry = entry;
                bool remote = MediaHistoryPreviewRules.IsRemote(entry.FilePath);
                DisplayName = string.IsNullOrEmpty(entry.DisplayName) ? SafeName(entry.FilePath) : entry.DisplayName;
                TimeText = FormatTime(entry.Timestamp);
                FolderButtonVisibility = remote ? Visibility.Collapsed : Visibility.Visible;

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
