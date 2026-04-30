using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using LibVLCSharp.Shared;
using NAudio.Wave;
using Microsoft.Win32;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using VlcMedia = LibVLCSharp.Shared.Media;

namespace ConditioningControlPanel.Views.Deeper
{
    public partial class DeeperEditorWindow : Window
    {
        private Enhancement _enhancement = new();
        private string? _filePath;
        private bool _isDirty;
        private bool _suppressDirty;

        // Video playback
        private VlcMediaPlayer? _mediaPlayer;

        // Audio playback
        private WaveOutEvent? _waveOut;
        private AudioFileReader? _audioReader;
        private AudioWaveformResult? _waveformData;

        // Common
        private double _totalSeconds;
        private double _currentSeconds;
        private bool _isScrubbing;
        private DispatcherTimer? _playheadTimer;
        private DispatcherTimer? _validationTimer;

        // Regions
        private static readonly string[] RegionPalette =
        {
            "#7B5CFF", "#FF69B4", "#5CFFB7", "#FFC85C", "#5CC8FF", "#FF7B5C"
        };
        private Region? _selectedRegion;
        private readonly List<System.Windows.Shapes.Rectangle> _regionVisuals = new();
        private enum DragMode { None, Scrub, CreateRegion }
        private DragMode _dragMode = DragMode.None;
        private double _dragCreateStartSec;
        private System.Windows.Shapes.Rectangle? _dragCreatePreview;

        public DeeperEditorWindow(Enhancement enhancement, string? filePath)
        {
            InitializeComponent();
            Loaded += DeeperEditorWindow_Loaded;
            KeyDown += DeeperEditorWindow_KeyDown;

            _validationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _validationTimer.Tick += (_, _) => { _validationTimer.Stop(); RefreshValidation(); };

            _playheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _playheadTimer.Tick += PlayheadTimer_Tick;

            BuildColorSwatches();
            LoadEnhancement(enhancement, filePath);
        }

        private void BuildColorSwatches()
        {
            RegionColorSwatches.Children.Clear();
            foreach (var hex in RegionPalette)
            {
                var brush = TryParseBrush(hex) ?? Brushes.MediumPurple;
                var swatch = new Border
                {
                    Width = 22, Height = 22, Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = brush,
                    BorderBrush = (System.Windows.Media.Brush)FindResource("GlassBorderBrush"),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    Tag = hex,
                    ToolTip = hex
                };
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    if (_selectedRegion == null) return;
                    TxtRegionColor.Text = hex;
                };
                RegionColorSwatches.Children.Add(swatch);
            }
        }

        private static System.Windows.Media.Brush? TryParseBrush(string hex)
        {
            try
            {
                var conv = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                var brush = new System.Windows.Media.SolidColorBrush(conv);
                brush.Freeze();
                return brush;
            }
            catch { return null; }
        }

        private static System.Windows.Media.Color? TryParseColor(string hex)
        {
            try { return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        private void DeeperEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _ = InitializePreviewAsync();
        }

        private void LoadEnhancement(Enhancement enhancement, string? filePath)
        {
            _enhancement = enhancement;
            _filePath = filePath;
            _isDirty = false;

            _suppressDirty = true;
            try
            {
                TxtMetaName.Text = _enhancement.Metadata.Name;
                TxtMetaCreator.Text = _enhancement.Metadata.Creator;
                TxtMetaDescription.Text = _enhancement.Metadata.Description;
                TxtMetaTags.Text = string.Join(", ", _enhancement.Metadata.Tags);
                TxtMetaLicense.Text = _enhancement.Metadata.License;
                TxtMediaSource.Text = _enhancement.MediaSource;
                TxtMediaType.Text = _enhancement.MediaType == MediaTypes.Audio
                    ? Loc.Get("deeper_editor_media_type_audio")
                    : Loc.Get("deeper_editor_media_type_video");
            }
            finally { _suppressDirty = false; }

            UpdateTitle();
            RefreshValidation();
            SelectRegion(null);
            RebuildRegionVisuals();
        }

        private async Task InitializePreviewAsync()
        {
            try
            {
                var source = _enhancement.MediaSource;
                if (!IsLocalFile(source))
                {
                    ShowPlaceholder();
                    return;
                }

                if (_enhancement.MediaType == MediaTypes.Audio)
                    await InitializeAudioAsync(source);
                else
                    InitializeVideo(source);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: preview init failed");
                ShowPlaceholder();
            }
        }

        private void InitializeVideo(string path)
        {
            if (!VideoService.WaitForLibVLC(3000) || VideoService.SharedLibVLC == null)
            {
                ShowPlaceholder();
                return;
            }

            VideoPreview.Visibility = Visibility.Visible;
            WaveformCanvas.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;

            _mediaPlayer = new VlcMediaPlayer(VideoService.SharedLibVLC);
            VideoPreview.MediaPlayer = _mediaPlayer;

            using var media = new VlcMedia(VideoService.SharedLibVLC, path, FromType.FromPath);
            _mediaPlayer.Media = media;

            _mediaPlayer.LengthChanged += (_, args) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    _totalSeconds = args.Length / 1000.0;
                    TxtTotalTime.Text = FormatTime(_totalSeconds);
                    UpdatePlayheadPosition();
                    RebuildRegionVisuals();
                });
            };
            _mediaPlayer.TimeChanged += (_, args) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (_isScrubbing) return;
                    _currentSeconds = args.Time / 1000.0;
                    TxtCurrentTime.Text = FormatTime(_currentSeconds);
                    UpdatePlayheadPosition();
                });
            };
            _mediaPlayer.EndReached += (_, _) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    BtnPlayPause.Content = "▶";
                });
            };

            _mediaPlayer.Play();
            _mediaPlayer.Pause();
            BtnPlayPause.Content = "▶";
        }

        private async Task InitializeAudioAsync(string path)
        {
            VideoPreview.Visibility = Visibility.Collapsed;
            WaveformCanvas.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;

            try
            {
                _waveformData = await AudioWaveformCache.LoadAsync(path);
                _totalSeconds = _waveformData.DurationSeconds;
                TxtTotalTime.Text = FormatTime(_totalSeconds);
                UpdateWaveformPath();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: waveform decode failed");
            }

            try
            {
                _audioReader = new AudioFileReader(path);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioReader);
                _waveOut.PlaybackStopped += (_, _) =>
                {
                    Dispatcher.InvokeAsync(() => BtnPlayPause.Content = "▶");
                };
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: audio playback init failed");
                _waveOut?.Dispose(); _waveOut = null;
                _audioReader?.Dispose(); _audioReader = null;
            }

            UpdatePlayheadPosition();
        }

        private void ShowPlaceholder()
        {
            VideoPreview.Visibility = Visibility.Collapsed;
            WaveformCanvas.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            TxtPlaceholderSource.Text = _enhancement.MediaSource;
        }

        private static bool IsLocalFile(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (source.Contains("://")) return false;
            if (source.Contains('*')) return false;
            return File.Exists(source);
        }

        private void UpdateWaveformPath()
        {
            if (_waveformData == null || _waveformData.Peaks.Length == 0)
            {
                WaveformPath.Data = null;
                return;
            }

            var peaks = _waveformData.Peaks;
            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            // Render as vertical bars (one per peak) — classic Audacity look.
            // Each bar centered on its X column, from (midY - amp) to (midY + amp).
            var midY = height / 2.0;
            var stepX = width / peaks.Length;
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                for (int i = 0; i < peaks.Length; i++)
                {
                    var x = i * stepX + stepX / 2.0;
                    var amp = peaks[i] * (height * 0.46);
                    if (amp < 0.5) amp = 0.5;
                    ctx.BeginFigure(new Point(x, midY - amp), false, false);
                    ctx.LineTo(new Point(x, midY + amp), true, false);
                }
            }
            geom.Freeze();
            WaveformPath.Data = geom;
        }

        // -- Transport ---------------------------------------------------------

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer != null)
            {
                if (_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Pause();
                    BtnPlayPause.Content = "▶";
                    _playheadTimer?.Stop();
                }
                else
                {
                    _mediaPlayer.Play();
                    BtnPlayPause.Content = "⏸";
                    _playheadTimer?.Start();
                }
            }
            else if (_waveOut != null)
            {
                if (_waveOut.PlaybackState == PlaybackState.Playing)
                {
                    _waveOut.Pause();
                    BtnPlayPause.Content = "▶";
                    _playheadTimer?.Stop();
                }
                else
                {
                    _waveOut.Play();
                    BtnPlayPause.Content = "⏸";
                    _playheadTimer?.Start();
                }
            }
        }

        private void PlayheadTimer_Tick(object? sender, EventArgs e)
        {
            if (_isScrubbing) return;
            if (_audioReader != null)
            {
                _currentSeconds = _audioReader.CurrentTime.TotalSeconds;
                TxtCurrentTime.Text = FormatTime(_currentSeconds);
                UpdatePlayheadPosition();
            }
            // Video time updates come via MediaPlayer.TimeChanged event already.
        }

        private void SeekToFraction(double frac)
        {
            frac = Math.Clamp(frac, 0, 1);
            _currentSeconds = frac * _totalSeconds;
            TxtCurrentTime.Text = FormatTime(_currentSeconds);
            if (_mediaPlayer != null && _totalSeconds > 0)
            {
                _mediaPlayer.SeekTo(TimeSpan.FromSeconds(_currentSeconds));
            }
            else if (_audioReader != null)
            {
                try { _audioReader.CurrentTime = TimeSpan.FromSeconds(_currentSeconds); }
                catch { /* unsupported on some streams */ }
            }
            UpdatePlayheadPosition();
        }

        private void UpdatePlayheadPosition()
        {
            if (TimelineCanvas.ActualWidth <= 0) return;
            double frac = _totalSeconds > 0 ? Math.Clamp(_currentSeconds / _totalSeconds, 0, 1) : 0;
            double x = frac * TimelineCanvas.ActualWidth;
            PlayheadLine.X1 = x;
            PlayheadLine.X2 = x;
            PlayheadLine.Y1 = 0;
            PlayheadLine.Y2 = TimelineCanvas.ActualHeight;
        }

        private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePlayheadPosition();
            UpdateWaveformPath();
            RebuildRegionVisuals();
        }

        private void TimelineCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Shift+click+drag creates a region instead of scrubbing.
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && _totalSeconds > 0)
            {
                _dragMode = DragMode.CreateRegion;
                _dragCreateStartSec = MouseToSeconds(e);
                StartDragCreatePreview(_dragCreateStartSec, _dragCreateStartSec);
                TimelineCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Plain click on empty area deselects + scrubs.
            SelectRegion(null);
            _dragMode = DragMode.Scrub;
            _isScrubbing = true;
            TimelineCanvas.CaptureMouse();
            ApplyScrubFromMouse(e);
        }

        private void TimelineCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragMode == DragMode.CreateRegion)
            {
                UpdateDragCreatePreview(MouseToSeconds(e));
                return;
            }
            if (_dragMode == DragMode.Scrub && _isScrubbing) ApplyScrubFromMouse(e);
        }

        private void TimelineCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragMode == DragMode.CreateRegion)
            {
                var endSec = MouseToSeconds(e);
                FinishDragCreate(endSec);
                TimelineCanvas.ReleaseMouseCapture();
                _dragMode = DragMode.None;
                return;
            }
            if (_dragMode == DragMode.Scrub)
            {
                _isScrubbing = false;
                _dragMode = DragMode.None;
                TimelineCanvas.ReleaseMouseCapture();
            }
        }

        private void ApplyScrubFromMouse(MouseEventArgs e)
        {
            var pt = e.GetPosition(TimelineCanvas);
            var w = TimelineCanvas.ActualWidth;
            if (w <= 0) return;
            SeekToFraction(pt.X / w);
        }

        private double MouseToSeconds(MouseEventArgs e)
        {
            var pt = e.GetPosition(TimelineCanvas);
            var w = TimelineCanvas.ActualWidth;
            if (w <= 0 || _totalSeconds <= 0) return 0;
            var frac = Math.Clamp(pt.X / w, 0, 1);
            return frac * _totalSeconds;
        }

        // -- Region creation / selection --------------------------------------

        private void StartDragCreatePreview(double startSec, double endSec)
        {
            _dragCreatePreview = new System.Windows.Shapes.Rectangle
            {
                Fill = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent40Brush"),
                Stroke = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            TimelineCanvas.Children.Insert(0, _dragCreatePreview);
            UpdateDragCreatePreview(endSec);
        }

        private void UpdateDragCreatePreview(double endSec)
        {
            if (_dragCreatePreview == null) return;
            var w = TimelineCanvas.ActualWidth;
            var h = TimelineCanvas.ActualHeight;
            if (w <= 0 || _totalSeconds <= 0) return;

            var lo = Math.Min(_dragCreateStartSec, endSec);
            var hi = Math.Max(_dragCreateStartSec, endSec);
            var leftX = (lo / _totalSeconds) * w;
            var rightX = (hi / _totalSeconds) * w;

            _dragCreatePreview.Width = Math.Max(0, rightX - leftX);
            _dragCreatePreview.Height = h;
            Canvas.SetLeft(_dragCreatePreview, leftX);
            Canvas.SetTop(_dragCreatePreview, 0);
        }

        private void FinishDragCreate(double endSec)
        {
            if (_dragCreatePreview != null)
            {
                TimelineCanvas.Children.Remove(_dragCreatePreview);
                _dragCreatePreview = null;
            }
            CreateRegion(_dragCreateStartSec, endSec);
        }

        private void CreateRegion(double a, double b)
        {
            var lo = Math.Min(a, b);
            var hi = Math.Max(a, b);
            if (hi - lo < 0.1) return;
            if (_totalSeconds > 0) hi = Math.Min(hi, _totalSeconds);
            lo = Math.Max(0, lo);

            var region = new Region
            {
                Id = NextRegionId(),
                Start = lo,
                End = hi,
                Label = "",
                Color = NextRegionColor()
            };
            _enhancement.Regions.Add(region);
            MarkDirty();
            RebuildRegionVisuals();
            SelectRegion(region);
            ScheduleValidation();
        }

        private string NextRegionId()
        {
            int n = _enhancement.Regions.Count + 1;
            while (true)
            {
                var candidate = "r" + n;
                if (!_enhancement.Regions.Any(r => r.Id == candidate)) return candidate;
                n++;
            }
        }

        private string NextRegionColor()
        {
            return RegionPalette[_enhancement.Regions.Count % RegionPalette.Length];
        }

        private void SelectRegion(Region? region)
        {
            _selectedRegion = region;
            UpdateSelectedSidePanel();
            RebuildRegionVisuals();
        }

        private void UpdateSelectedSidePanel()
        {
            if (SelectedPlaceholder == null || RegionEditor == null) return;
            if (_selectedRegion == null)
            {
                SelectedPlaceholder.Visibility = Visibility.Visible;
                RegionEditor.Visibility = Visibility.Collapsed;
                return;
            }

            SelectedPlaceholder.Visibility = Visibility.Collapsed;
            RegionEditor.Visibility = Visibility.Visible;

            _suppressDirty = true;
            try
            {
                TxtRegionId.Text = _selectedRegion.Id;
                TxtRegionLabel.Text = _selectedRegion.Label;
                TxtRegionStart.Text = _selectedRegion.Start.ToString("0.##", CultureInfo.InvariantCulture);
                TxtRegionEnd.Text = _selectedRegion.End.ToString("0.##", CultureInfo.InvariantCulture);
                TxtRegionColor.Text = _selectedRegion.Color;
                UpdateRegionColorSwatchPreview();
            }
            finally { _suppressDirty = false; }
        }

        private void UpdateRegionColorSwatchPreview()
        {
            var brush = TryParseBrush(_selectedRegion?.Color ?? "#7B5CFF");
            if (brush != null) RegionColorSwatch.Background = brush;
        }

        private void RegionField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressDirty || _selectedRegion == null) return;
            _selectedRegion.Label = TxtRegionLabel.Text ?? "";
            if (double.TryParse(TxtRegionStart.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                _selectedRegion.Start = Math.Max(0, s);
            if (double.TryParse(TxtRegionEnd.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ev))
                _selectedRegion.End = ev;
            if (!string.IsNullOrWhiteSpace(TxtRegionColor.Text))
            {
                _selectedRegion.Color = TxtRegionColor.Text.Trim();
                UpdateRegionColorSwatchPreview();
            }
            MarkDirty();
            RebuildRegionVisuals();
            ScheduleValidation();
        }

        private void BtnDeleteRegion_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRegion == null) return;
            _enhancement.Regions.Remove(_selectedRegion);
            SelectRegion(null);
            MarkDirty();
            RebuildRegionVisuals();
            ScheduleValidation();
        }

        // -- Region rendering --------------------------------------------------

        private void RebuildRegionVisuals()
        {
            if (TimelineCanvas == null) return;
            foreach (var r in _regionVisuals) TimelineCanvas.Children.Remove(r);
            _regionVisuals.Clear();

            if (_totalSeconds <= 0 || TimelineCanvas.ActualWidth <= 0)
            {
                EnsurePlayheadOnTop();
                return;
            }

            foreach (var region in _enhancement.Regions)
            {
                var rect = BuildRegionVisual(region);
                if (rect != null)
                {
                    TimelineCanvas.Children.Insert(0, rect);
                    _regionVisuals.Add(rect);
                }
            }
            EnsurePlayheadOnTop();
        }

        private System.Windows.Shapes.Rectangle? BuildRegionVisual(Region region)
        {
            var w = TimelineCanvas.ActualWidth;
            var h = TimelineCanvas.ActualHeight;
            if (w <= 0 || _totalSeconds <= 0) return null;

            var startX = Math.Max(0, (region.Start / _totalSeconds) * w);
            var endX = Math.Min(w, (region.End / _totalSeconds) * w);
            var width = Math.Max(0, endX - startX);

            var color = TryParseColor(region.Color) ?? Colors.MediumPurple;
            var fill = System.Windows.Media.Color.FromArgb(80, color.R, color.G, color.B);
            var isSelected = _selectedRegion == region;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = h,
                Fill = new System.Windows.Media.SolidColorBrush(fill),
                Stroke = new System.Windows.Media.SolidColorBrush(color),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = Cursors.Hand,
                Tag = region,
                ToolTip = string.IsNullOrEmpty(region.Label) ? region.Id : $"{region.Id} — {region.Label}"
            };
            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, 0);
            rect.MouseLeftButtonDown += RegionRect_MouseLeftButtonDown;
            return rect;
        }

        private void EnsurePlayheadOnTop()
        {
            if (PlayheadLine == null) return;
            TimelineCanvas.Children.Remove(PlayheadLine);
            TimelineCanvas.Children.Add(PlayheadLine);
        }

        private void RegionRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Shapes.Rectangle r && r.Tag is Region region)
            {
                SelectRegion(region);
                e.Handled = true;
            }
        }

        // -- Metadata sync -----------------------------------------------------

        private void MetadataField_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressDirty) return;
            _enhancement.Metadata.Name = TxtMetaName.Text ?? "";
            _enhancement.Metadata.Creator = TxtMetaCreator.Text ?? "";
            _enhancement.Metadata.Description = TxtMetaDescription.Text ?? "";
            _enhancement.Metadata.Tags = (TxtMetaTags.Text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            _enhancement.Metadata.License = TxtMetaLicense.Text ?? "";
            MarkDirty();
            ScheduleValidation();
        }

        private void MarkDirty()
        {
            if (_suppressDirty) return;
            _isDirty = true;
            TxtDirty.Visibility = Visibility.Visible;
        }

        private void ScheduleValidation()
        {
            _validationTimer?.Stop();
            _validationTimer?.Start();
        }

        private void RefreshValidation()
        {
            var errors = EnhancementValidator.Validate(_enhancement);
            int errorCount = errors.Count(x => x.Severity == ValidationSeverity.Error);
            int warningCount = errors.Count(x => x.Severity == ValidationSeverity.Warning);
            if (errorCount == 0 && warningCount == 0)
            {
                TxtValidationSummary.Text = Loc.Get("deeper_editor_validation_clean");
                TxtValidationSummary.Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush");
            }
            else
            {
                var bits = new System.Collections.Generic.List<string>();
                if (errorCount > 0) bits.Add(string.Format(Loc.Get("deeper_editor_validation_errors_fmt"), errorCount));
                if (warningCount > 0) bits.Add(string.Format(Loc.Get("deeper_editor_validation_warnings_fmt"), warningCount));
                TxtValidationSummary.Text = string.Join("  ·  ", bits);
                TxtValidationSummary.Foreground = errorCount > 0
                    ? (System.Windows.Media.Brush)FindResource("DangerBrush")
                    : (System.Windows.Media.Brush)FindResource("PinkSoftBrush");

                var first = errors.FirstOrDefault();
                if (first != null)
                    TxtValidationSummary.ToolTip = first.ToString();
            }
        }

        // -- File ops ----------------------------------------------------------

        private void MenuSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                MenuSaveAs_Click(sender, e);
                return;
            }
            SaveTo(_filePath!);
        }

        private void MenuSaveAs_Click(object sender, RoutedEventArgs e)
        {
            var library = App.EnhancementLibrary;
            var dialog = new SaveFileDialog
            {
                Title = Loc.Get("deeper_editor_save_dialog_title"),
                Filter = $"Deeper Enhancement (*{EnhancementLibrary.FileSuffix})|*{EnhancementLibrary.FileSuffix}",
                FileName = library?.SuggestedFileName(_enhancement) ?? ("Untitled" + EnhancementLibrary.FileSuffix),
                AddExtension = false
            };
            var lastDir = library?.LastDirectory;
            if (!string.IsNullOrEmpty(lastDir)) dialog.InitialDirectory = lastDir;
            if (dialog.ShowDialog(this) == true)
            {
                var path = dialog.FileName;
                if (!path.EndsWith(EnhancementLibrary.FileSuffix, StringComparison.OrdinalIgnoreCase))
                    path += EnhancementLibrary.FileSuffix;
                SaveTo(path);
            }
        }

        private void SaveTo(string path)
        {
            try
            {
                App.EnhancementLibrary?.Save(_enhancement, path);
                _filePath = path;
                _isDirty = false;
                TxtDirty.Visibility = Visibility.Collapsed;
                UpdateTitle();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: save failed");
                MessageBox.Show(this, string.Format(Loc.Get("deeper_editor_save_failed_fmt"), ex.Message),
                    Loc.Get("deeper_editor_save_dialog_title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MenuClose_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateTitle()
        {
            var name = string.IsNullOrEmpty(_enhancement.Metadata.Name)
                ? Loc.Get("deeper_editor_untitled") : _enhancement.Metadata.Name;
            TxtTitle.Text = name;
            TxtFilePath.Text = _filePath ?? Loc.Get("deeper_editor_unsaved");
            Title = $"Deeper — {name}";
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
        }

        // -- Window lifecycle --------------------------------------------------

        private void DeeperEditorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Don't hijack typing inside text fields.
            var inTextBox = Keyboard.FocusedElement is System.Windows.Controls.TextBox;

            if (e.Key == Key.Space && !inTextBox)
            {
                BtnPlayPause_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Home && !inTextBox)
            {
                SeekToFraction(0);
                e.Handled = true;
            }
            else if (e.Key == Key.R && !inTextBox && _totalSeconds > 0)
            {
                var start = _currentSeconds;
                var end = Math.Min(_totalSeconds, start + 5);
                CreateRegion(start, end);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && !inTextBox && _selectedRegion != null)
            {
                BtnDeleteRegion_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && !inTextBox && _selectedRegion != null)
            {
                SelectRegion(null);
                e.Handled = true;
            }
            else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    MenuSaveAs_Click(this, new RoutedEventArgs());
                else
                    MenuSave_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isDirty)
            {
                var result = MessageBox.Show(this,
                    Loc.Get("deeper_editor_close_unsaved_prompt"),
                    Loc.Get("deeper_editor_close_unsaved_title"),
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
                if (result == MessageBoxResult.Yes)
                {
                    if (string.IsNullOrEmpty(_filePath)) MenuSaveAs_Click(this, new RoutedEventArgs());
                    else SaveTo(_filePath!);
                    if (_isDirty) { e.Cancel = true; return; }
                }
            }

            DisposePlayback();
        }

        private void DisposePlayback()
        {
            try
            {
                _playheadTimer?.Stop();
                _validationTimer?.Stop();

                if (_mediaPlayer != null)
                {
                    if (VideoPreview != null) VideoPreview.MediaPlayer = null;
                    _mediaPlayer.Stop();
                    _mediaPlayer.Dispose();
                    _mediaPlayer = null;
                }

                _waveOut?.Stop();
                _waveOut?.Dispose();
                _waveOut = null;
                _audioReader?.Dispose();
                _audioReader = null;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DeeperEditor: dispose playback warning: {Error}", ex.Message);
            }
        }
    }
}
