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
        private enum DragMode { None, Scrub, CreateRegion, ShiftHapticEvent }
        private DragMode _dragMode = DragMode.None;
        private double _dragCreateStartSec;
        private System.Windows.Shapes.Rectangle? _dragCreatePreview;

        // Haptic events
        private const string DefaultTrackId = "primary";
        private HapticEvent? _selectedHaptic;
        private HapticTrack? _selectedHapticTrack;
        private readonly List<System.Windows.Shapes.Rectangle> _hapticVisuals = new();
        private double _hapticDragStartSec;
        private double _hapticDragOffsetSec;
        private HapticEvent? _draggedHaptic;
        private HapticTrack? _draggedHapticTrack;

        // Curve editor
        private const int CurveKeyframeCount = 5;
        private System.Windows.Shapes.Path? _curvePath;
        private readonly List<System.Windows.Shapes.Ellipse> _curveHandles = new();
        private int _draggingCurveIndex = -1;
        private bool _suppressPatternSync;

        // Rules
        private EnhancementRule? _selectedRule;
        private bool _suppressRuleSync;
        private GazePickerWindow? _gazePickerWindow;

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
            BuildPatternCombo();
            LoadEnhancement(enhancement, filePath);
        }

        private void BuildPatternCombo()
        {
            CmbHapticPattern.Items.Clear();
            foreach (var name in StockHapticPatterns.Names)
                CmbHapticPattern.Items.Add(name);
            CmbHapticPattern.Items.Add(Loc.Get("deeper_editor_haptic_pattern_custom"));
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
            SelectNothing();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RefreshRulesList();
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
            UpdateLaneDivider();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
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
            SelectNothing();
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
            if (_dragMode == DragMode.ShiftHapticEvent && _draggedHaptic != null)
            {
                var newStart = MouseToSeconds(e) - _hapticDragOffsetSec;
                newStart = Math.Max(0, Math.Min(newStart, Math.Max(0, _totalSeconds - _draggedHaptic.Duration)));
                _draggedHaptic.Start = newStart;
                MarkDirty();
                RebuildHapticVisuals();
                if (_selectedHaptic == _draggedHaptic) PopulateHapticEditor();
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
            if (_dragMode == DragMode.ShiftHapticEvent)
            {
                _draggedHaptic = null;
                _draggedHapticTrack = null;
                _dragMode = DragMode.None;
                TimelineCanvas.ReleaseMouseCapture();
                ScheduleValidation();
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
            _dragCreatePreview.Height = h / 2.0;
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

        private void SelectNothing()
        {
            _selectedRegion = null;
            _selectedHaptic = null;
            _selectedHapticTrack = null;
            _selectedRule = null;
            EndGazePick(commit: false);
            UpdateSelectedSidePanel();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RefreshRulesList();
        }

        private void SelectRegion(Region? region)
        {
            _selectedRegion = region;
            _selectedHaptic = null;
            _selectedHapticTrack = null;
            _selectedRule = null;
            EndGazePick(commit: false);
            UpdateSelectedSidePanel();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RefreshRulesList();
        }

        private void SelectHaptic(HapticTrack track, HapticEvent ev)
        {
            _selectedRegion = null;
            _selectedHaptic = ev;
            _selectedHapticTrack = track;
            _selectedRule = null;
            EndGazePick(commit: false);
            UpdateSelectedSidePanel();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RefreshRulesList();
        }

        private void SelectRule(EnhancementRule? rule)
        {
            _selectedRegion = null;
            _selectedHaptic = null;
            _selectedHapticTrack = null;
            _selectedRule = rule;
            EndGazePick(commit: false);
            UpdateSelectedSidePanel();
            RebuildRegionVisuals();
            RebuildHapticVisuals();
            RefreshRulesList();
        }

        private void UpdateSelectedSidePanel()
        {
            if (SelectedPlaceholder == null || RegionEditor == null || HapticEventEditor == null || RuleEditor == null) return;

            if (_selectedRegion != null)
            {
                SelectedPlaceholder.Visibility = Visibility.Collapsed;
                RegionEditor.Visibility = Visibility.Visible;
                HapticEventEditor.Visibility = Visibility.Collapsed;
                RuleEditor.Visibility = Visibility.Collapsed;

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
                return;
            }

            if (_selectedHaptic != null && _selectedHapticTrack != null)
            {
                SelectedPlaceholder.Visibility = Visibility.Collapsed;
                RegionEditor.Visibility = Visibility.Collapsed;
                HapticEventEditor.Visibility = Visibility.Visible;
                RuleEditor.Visibility = Visibility.Collapsed;
                PopulateHapticEditor();
                return;
            }

            if (_selectedRule != null)
            {
                SelectedPlaceholder.Visibility = Visibility.Collapsed;
                RegionEditor.Visibility = Visibility.Collapsed;
                HapticEventEditor.Visibility = Visibility.Collapsed;
                RuleEditor.Visibility = Visibility.Visible;
                PopulateRuleEditor();
                return;
            }

            SelectedPlaceholder.Visibility = Visibility.Visible;
            RegionEditor.Visibility = Visibility.Collapsed;
            HapticEventEditor.Visibility = Visibility.Collapsed;
            RuleEditor.Visibility = Visibility.Collapsed;
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
            SelectNothing();
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

            var laneH = h / 2.0;
            var startX = Math.Max(0, (region.Start / _totalSeconds) * w);
            var endX = Math.Min(w, (region.End / _totalSeconds) * w);
            var width = Math.Max(0, endX - startX);

            var color = TryParseColor(region.Color) ?? Colors.MediumPurple;
            var fill = System.Windows.Media.Color.FromArgb(80, color.R, color.G, color.B);
            var isSelected = _selectedRegion == region;

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = laneH,
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
            // Keep static overlays in front of all the dynamically inserted lane visuals.
            foreach (var item in new System.Windows.UIElement?[] { LaneDivider, LaneLabelRegions, LaneLabelHaptics, PlayheadLine })
            {
                if (item == null) continue;
                if (TimelineCanvas.Children.Contains(item))
                    TimelineCanvas.Children.Remove(item);
                TimelineCanvas.Children.Add(item);
            }
        }

        private void UpdateLaneDivider()
        {
            if (TimelineCanvas == null || LaneDivider == null) return;
            var w = TimelineCanvas.ActualWidth;
            var h = TimelineCanvas.ActualHeight;
            LaneDivider.X1 = 0;
            LaneDivider.X2 = w;
            LaneDivider.Y1 = h / 2.0;
            LaneDivider.Y2 = h / 2.0;
            if (LaneLabelHaptics != null) Canvas.SetTop(LaneLabelHaptics, h / 2.0 + 2);
        }

        private void RegionRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Shapes.Rectangle r && r.Tag is Region region)
            {
                SelectRegion(region);
                e.Handled = true;
            }
        }

        // -- Haptic events: rendering + interaction ---------------------------

        private HapticTrack EnsureDefaultTrack()
        {
            if (_enhancement.HapticTracks.Count == 0)
                _enhancement.HapticTracks.Add(new HapticTrack { Id = DefaultTrackId });
            return _enhancement.HapticTracks[0];
        }

        private void CreateHapticEventAtPlayhead()
        {
            if (_totalSeconds <= 0) return;
            var track = EnsureDefaultTrack();
            var start = _currentSeconds;
            var duration = 1.0;
            // Avoid overlapping: if would overlap, nudge to first free slot after current events.
            foreach (var existing in track.Events.OrderBy(x => x.Start))
            {
                if (start < existing.Start + existing.Duration && start + duration > existing.Start)
                    start = existing.Start + existing.Duration + 0.05;
            }
            if (start + duration > _totalSeconds) start = Math.Max(0, _totalSeconds - duration);

            var ev = new HapticEvent
            {
                Start = start,
                Duration = duration,
                Intensity = 1.0,
                PatternName = "Pulse"
            };
            track.Events.Add(ev);
            MarkDirty();
            RebuildHapticVisuals();
            SelectHaptic(track, ev);
            ScheduleValidation();
        }

        private void RebuildHapticVisuals()
        {
            if (TimelineCanvas == null) return;
            foreach (var v in _hapticVisuals) TimelineCanvas.Children.Remove(v);
            _hapticVisuals.Clear();

            if (_totalSeconds <= 0 || TimelineCanvas.ActualWidth <= 0)
            {
                EnsurePlayheadOnTop();
                return;
            }

            foreach (var track in _enhancement.HapticTracks)
            {
                foreach (var ev in track.Events)
                {
                    var rect = BuildHapticVisual(track, ev);
                    if (rect != null)
                    {
                        TimelineCanvas.Children.Insert(0, rect);
                        _hapticVisuals.Add(rect);
                    }
                }
            }
            EnsurePlayheadOnTop();
        }

        private System.Windows.Shapes.Rectangle? BuildHapticVisual(HapticTrack track, HapticEvent ev)
        {
            var w = TimelineCanvas.ActualWidth;
            var h = TimelineCanvas.ActualHeight;
            if (w <= 0 || _totalSeconds <= 0) return null;

            var laneTop = h / 2.0 + 2;
            var laneH = Math.Max(0, h / 2.0 - 4);
            var startX = Math.Max(0, (ev.Start / _totalSeconds) * w);
            var endX = Math.Min(w, ((ev.Start + ev.Duration) / _totalSeconds) * w);
            var width = Math.Max(0, endX - startX);

            var isSelected = _selectedHaptic == ev;
            var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF7B5CFF");
            var fill = System.Windows.Media.Color.FromArgb(isSelected ? (byte)180 : (byte)130, accent.R, accent.G, accent.B);

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = laneH,
                Fill = new System.Windows.Media.SolidColorBrush(fill),
                Stroke = new System.Windows.Media.SolidColorBrush(accent),
                StrokeThickness = isSelected ? 2.0 : 1.0,
                Cursor = Cursors.SizeAll,
                Tag = (track, ev),
                ToolTip = string.IsNullOrEmpty(ev.PatternName)
                    ? $"{track.Id} · custom · {ev.Duration:0.##}s"
                    : $"{track.Id} · {ev.PatternName} · {ev.Duration:0.##}s"
            };
            Canvas.SetLeft(rect, startX);
            Canvas.SetTop(rect, laneTop);
            rect.MouseLeftButtonDown += HapticRect_MouseLeftButtonDown;
            return rect;
        }

        private void HapticRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Shapes.Rectangle r || r.Tag is not ValueTuple<HapticTrack, HapticEvent> tuple)
                return;

            var (track, ev) = tuple;
            SelectHaptic(track, ev);

            // Begin drag-shift on pointer hold (no Shift modifier — that's region-create).
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                _dragMode = DragMode.ShiftHapticEvent;
                _draggedHaptic = ev;
                _draggedHapticTrack = track;
                _hapticDragStartSec = ev.Start;
                _hapticDragOffsetSec = MouseToSeconds(e) - ev.Start;
                TimelineCanvas.CaptureMouse();
            }
            e.Handled = true;
        }

        // -- Haptic side panel + curve editor ---------------------------------

        private void PopulateHapticEditor()
        {
            if (_selectedHaptic == null || _selectedHapticTrack == null) return;

            _suppressDirty = true;
            _suppressPatternSync = true;
            try
            {
                TxtHapticTrackId.Text = _selectedHapticTrack.Id;
                TxtHapticStart.Text = _selectedHaptic.Start.ToString("0.##", CultureInfo.InvariantCulture);
                TxtHapticDuration.Text = _selectedHaptic.Duration.ToString("0.##", CultureInfo.InvariantCulture);
                SliderHapticIntensity.Value = Math.Clamp(_selectedHaptic.Intensity, 0.0, 1.0);
                TxtHapticIntensityValue.Text = $"{(int)(SliderHapticIntensity.Value * 100)}%";

                var isCustom = _selectedHaptic.CustomPattern != null && _selectedHaptic.CustomPattern.Count > 0;
                if (isCustom)
                {
                    CmbHapticPattern.SelectedIndex = StockHapticPatterns.Names.Count; // "Custom…"
                    CurveEditorPanel.Visibility = Visibility.Visible;
                    EnsureCurveSeed();
                    RebuildCurveEditor();
                }
                else
                {
                    var idx = -1;
                    if (!string.IsNullOrEmpty(_selectedHaptic.PatternName))
                    {
                        for (int i = 0; i < StockHapticPatterns.Names.Count; i++)
                            if (StockHapticPatterns.Names[i] == _selectedHaptic.PatternName) { idx = i; break; }
                    }
                    CmbHapticPattern.SelectedIndex = idx >= 0 ? idx : 0;
                    CurveEditorPanel.Visibility = Visibility.Collapsed;
                }
            }
            finally
            {
                _suppressDirty = false;
                _suppressPatternSync = false;
            }
        }

        private void HapticField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressDirty || _selectedHaptic == null) return;
            if (double.TryParse(TxtHapticStart.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                _selectedHaptic.Start = Math.Max(0, s);
            if (double.TryParse(TxtHapticDuration.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                _selectedHaptic.Duration = Math.Max(0.05, d);
            MarkDirty();
            RebuildHapticVisuals();
            ScheduleValidation();
        }

        private void SliderHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtHapticIntensityValue != null)
                TxtHapticIntensityValue.Text = $"{(int)(SliderHapticIntensity.Value * 100)}%";
            if (_suppressDirty || _selectedHaptic == null) return;
            _selectedHaptic.Intensity = Math.Clamp(SliderHapticIntensity.Value, 0.0, 1.0);
            MarkDirty();
            ScheduleValidation();
        }

        private void CmbHapticPattern_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressPatternSync || _selectedHaptic == null) return;
            var idx = CmbHapticPattern.SelectedIndex;
            if (idx < 0) return;

            if (idx >= StockHapticPatterns.Names.Count)
            {
                // Custom...
                _selectedHaptic.CustomPattern ??= StockHapticPatterns.SeedCustomFrom(_selectedHaptic.PatternName);
                _selectedHaptic.PatternName = null;
                CurveEditorPanel.Visibility = Visibility.Visible;
                EnsureCurveSeed();
                RebuildCurveEditor();
            }
            else
            {
                _selectedHaptic.CustomPattern = null;
                _selectedHaptic.PatternName = StockHapticPatterns.Names[idx];
                CurveEditorPanel.Visibility = Visibility.Collapsed;
            }
            MarkDirty();
            RebuildHapticVisuals();
            ScheduleValidation();
        }

        private void EnsureCurveSeed()
        {
            if (_selectedHaptic == null) return;
            if (_selectedHaptic.CustomPattern == null || _selectedHaptic.CustomPattern.Count < 2)
                _selectedHaptic.CustomPattern = StockHapticPatterns.SeedCustomFrom(_selectedHaptic.PatternName);
        }

        private void CurveCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RebuildCurveEditor();

        private void RebuildCurveEditor()
        {
            if (CurveCanvas == null) return;
            CurveCanvas.Children.Clear();
            _curvePath = null;
            _curveHandles.Clear();

            if (_selectedHaptic?.CustomPattern == null || _selectedHaptic.CustomPattern.Count == 0) return;
            var w = CurveCanvas.ActualWidth;
            var h = CurveCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // Background grid
            for (int i = 1; i <= 3; i++)
            {
                var y = h * i / 4.0;
                var line = new System.Windows.Shapes.Line
                {
                    X1 = 0, X2 = w, Y1 = y, Y2 = y,
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)),
                    StrokeThickness = 0.5,
                    IsHitTestVisible = false
                };
                CurveCanvas.Children.Add(line);
            }

            // Polyline through the keyframes
            var pts = _selectedHaptic.CustomPattern;
            var geom = new System.Windows.Media.StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(KeyframeToCanvas(pts[0], w, h), false, false);
                for (int i = 1; i < pts.Count; i++)
                    ctx.LineTo(KeyframeToCanvas(pts[i], w, h), true, false);
            }
            geom.Freeze();
            _curvePath = new System.Windows.Shapes.Path
            {
                Data = geom,
                Stroke = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                StrokeThickness = 1.6,
                IsHitTestVisible = false
            };
            CurveCanvas.Children.Add(_curvePath);

            // Handles (5 fixed-X dots, drag intensity vertically)
            for (int i = 0; i < pts.Count; i++)
            {
                var pt = KeyframeToCanvas(pts[i], w, h);
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 10, Height = 10,
                    Fill = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1.2,
                    Cursor = Cursors.SizeNS,
                    Tag = i
                };
                Canvas.SetLeft(dot, pt.X - 5);
                Canvas.SetTop(dot, pt.Y - 5);
                dot.MouseLeftButtonDown += CurveHandle_MouseDown;
                dot.MouseMove += CurveHandle_MouseMove;
                dot.MouseLeftButtonUp += CurveHandle_MouseUp;
                CurveCanvas.Children.Add(dot);
                _curveHandles.Add(dot);
            }
        }

        private static Point KeyframeToCanvas(double[] kf, double w, double h)
        {
            var t = Math.Clamp(kf.Length > 0 ? kf[0] : 0, 0, 1);
            var v = Math.Clamp(kf.Length > 1 ? kf[1] : 0, 0, 1);
            return new Point(t * w, h - (v * h));
        }

        private void CurveHandle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Shapes.Ellipse el && el.Tag is int idx)
            {
                _draggingCurveIndex = idx;
                el.CaptureMouse();
                e.Handled = true;
            }
        }

        private void CurveHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingCurveIndex < 0 || _selectedHaptic?.CustomPattern == null) return;
            if (sender is not System.Windows.Shapes.Ellipse el) return;
            var pt = e.GetPosition(CurveCanvas);
            var h = CurveCanvas.ActualHeight;
            if (h <= 0) return;
            var v = Math.Clamp(1.0 - (pt.Y / h), 0.0, 1.0);
            var kf = _selectedHaptic.CustomPattern[_draggingCurveIndex];
            kf[1] = v;
            MarkDirty();
            RebuildCurveEditor();
            ScheduleValidation();
        }

        private void CurveHandle_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Shapes.Ellipse el) el.ReleaseMouseCapture();
            _draggingCurveIndex = -1;
        }

        private void BtnResetCurve_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHaptic == null) return;
            _selectedHaptic.CustomPattern = StockHapticPatterns.SeedCustomFrom(null);
            MarkDirty();
            RebuildCurveEditor();
            ScheduleValidation();
        }

        private async void BtnTestHaptic_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHaptic == null) return;

            IList<double[]>? kf = null;
            if (_selectedHaptic.CustomPattern != null && _selectedHaptic.CustomPattern.Count > 0)
                kf = _selectedHaptic.CustomPattern;
            else if (!string.IsNullOrEmpty(_selectedHaptic.PatternName)
                     && StockHapticPatterns.TryGet(_selectedHaptic.PatternName, out var named) && named != null)
                kf = named;

            if (kf == null) return;
            var durationMs = (int)Math.Max(50, _selectedHaptic.Duration * 1000);
            var samples = StockHapticPatterns.Sample(kf, _selectedHaptic.Intensity, durationMs);
            try
            {
                if (App.Haptics == null || !App.Haptics.IsConnected)
                {
                    TxtValidationSummary.Text = Loc.Get("deeper_editor_haptic_test_no_device");
                    TxtValidationSummary.Foreground = (System.Windows.Media.Brush)FindResource("PinkSoftBrush");
                    return;
                }
                await App.Haptics.SetSyncPatternAsync(samples, durationMs);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "DeeperEditor: haptic test failed");
            }
        }

        private void BtnDeleteHaptic_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHaptic == null || _selectedHapticTrack == null) return;
            _selectedHapticTrack.Events.Remove(_selectedHaptic);
            // Remove now-empty default track to keep file clean.
            if (_selectedHapticTrack.Events.Count == 0 && _selectedHapticTrack.Id == DefaultTrackId)
                _enhancement.HapticTracks.Remove(_selectedHapticTrack);
            SelectNothing();
            MarkDirty();
            RebuildHapticVisuals();
            ScheduleValidation();
        }

        // -- Rules list -------------------------------------------------------

        private void RefreshRulesList()
        {
            if (RulesList == null) return;
            RulesList.Children.Clear();

            var rules = _enhancement.Rules;
            if (RulesEmptyHint != null)
                RulesEmptyHint.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < rules.Count; i++)
                RulesList.Children.Add(BuildRuleRow(rules[i], i));
        }

        private System.Windows.UIElement BuildRuleRow(EnhancementRule rule, int index)
        {
            var isSelected = _selectedRule == rule;
            var border = new Border
            {
                Background = isSelected
                    ? (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent20Brush")
                    : (System.Windows.Media.Brush)FindResource("PanelAccentBrush"),
                BorderBrush = isSelected
                    ? (System.Windows.Media.Brush)FindResource("DeeperAccentBrush")
                    : (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 6, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Tag = rule
            };
            border.MouseLeftButtonUp += (_, _) => SelectRule(rule);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var idx = new TextBlock
            {
                Text = $"#{index + 1}",
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 10,
                Foreground = (System.Windows.Media.Brush)FindResource("TextDimBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(idx, 0);
            grid.Children.Add(idx);

            var summary = new TextBlock
            {
                Text = SummarizeRule(rule),
                FontSize = 11,
                Foreground = rule.Enabled
                    ? (System.Windows.Media.Brush)FindResource("TextLightBrush")
                    : (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(summary, 1);
            grid.Children.Add(summary);

            var toggle = new CheckBox
            {
                IsChecked = rule.Enabled,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = Loc.Get("deeper_editor_rule_enabled")
            };
            toggle.Click += (_, e2) =>
            {
                rule.Enabled = toggle.IsChecked == true;
                MarkDirty();
                RefreshRulesList();
                if (_selectedRule == rule) PopulateRuleEditor();
                ScheduleValidation();
                e2.Handled = true;
            };
            Grid.SetColumn(toggle, 2);
            grid.Children.Add(toggle);

            border.Child = grid;
            return border;
        }

        private static string SummarizeRule(EnhancementRule rule)
        {
            var trig = rule.Trigger?.Type ?? "?";
            var act = rule.Action?.Type ?? "?";
            return $"{trig}  →  {act}";
        }

        private void BtnAddRule_Click(object sender, RoutedEventArgs e)
        {
            var isAudio = _enhancement.MediaType == MediaTypes.Audio;
            EnhancementTrigger defaultTrigger = isAudio
                ? new TimeReachedTrigger { Time = Math.Max(0, _currentSeconds) }
                : new GazeTargetTrigger();
            var rule = new EnhancementRule
            {
                Trigger = defaultTrigger,
                Action = new TriggerHapticAction { PatternName = "Pulse" },
                CooldownMs = 1000,
                Enabled = true
            };
            _enhancement.Rules.Add(rule);
            MarkDirty();
            ScheduleValidation();
            SelectRule(rule);
        }

        private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRule == null) return;
            _enhancement.Rules.Remove(_selectedRule);
            SelectNothing();
            MarkDirty();
            ScheduleValidation();
        }

        // -- Rule editor population -------------------------------------------

        private static readonly string[] TriggerTypesForVideo =
        {
            TriggerTypes.GazeTarget, TriggerTypes.GazeAvoid, TriggerTypes.AttentionLost,
            TriggerTypes.BlinkDetected, TriggerTypes.MouthOpen,
            TriggerTypes.TimeReached, TriggerTypes.RegionEntered, TriggerTypes.RegionExited
        };
        private static readonly string[] TriggerTypesForAudio =
        {
            TriggerTypes.TimeReached, TriggerTypes.RegionEntered, TriggerTypes.RegionExited
        };
        private static readonly string[] AllActionTypes =
        {
            ActionTypes.Seek, ActionTypes.LoopRegion, ActionTypes.Pause,
            ActionTypes.PlayAudio, ActionTypes.TriggerHaptic, ActionTypes.ScreenShake, ActionTypes.SetIntensity
        };

        private void PopulateRuleEditor()
        {
            if (_selectedRule == null) return;
            _suppressRuleSync = true;
            try
            {
                var idx = _enhancement.Rules.IndexOf(_selectedRule);
                TxtRuleHeader.Text = idx >= 0 ? $"rule #{idx + 1}" : "rule";
                ChkRuleEnabled.IsChecked = _selectedRule.Enabled;

                // Trigger type combo (filtered by media_type).
                var isAudio = _enhancement.MediaType == MediaTypes.Audio;
                var triggerOptions = isAudio ? TriggerTypesForAudio : TriggerTypesForVideo;
                CmbTriggerType.Items.Clear();
                foreach (var opt in triggerOptions)
                    CmbTriggerType.Items.Add(opt);
                var currentTrig = _selectedRule.Trigger?.Type ?? "";
                var trigIdx = Array.IndexOf(triggerOptions, currentTrig);
                if (trigIdx < 0) trigIdx = 0;
                CmbTriggerType.SelectedIndex = trigIdx;

                // Action combo.
                CmbActionType.Items.Clear();
                foreach (var opt in AllActionTypes) CmbActionType.Items.Add(opt);
                var actIdx = Array.IndexOf(AllActionTypes, _selectedRule.Action?.Type ?? "");
                if (actIdx < 0) actIdx = 0;
                CmbActionType.SelectedIndex = actIdx;

                // Region constraint combo.
                RebuildRegionConstraintCombo();

                TxtRuleCooldown.Text = _selectedRule.CooldownMs.ToString(CultureInfo.InvariantCulture);

                BuildTriggerFields();
                BuildActionFields();
            }
            finally { _suppressRuleSync = false; }
        }

        private void RebuildRegionConstraintCombo()
        {
            CmbRuleRegion.Items.Clear();
            CmbRuleRegion.Items.Add(Loc.Get("deeper_editor_rule_region_none"));
            int selected = 0;
            for (int i = 0; i < _enhancement.Regions.Count; i++)
            {
                var r = _enhancement.Regions[i];
                CmbRuleRegion.Items.Add(string.IsNullOrEmpty(r.Label) ? r.Id : $"{r.Id} — {r.Label}");
                if (_selectedRule?.RegionConstraint == r.Id) selected = i + 1;
            }
            CmbRuleRegion.SelectedIndex = selected;
        }

        private void ChkRuleEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            _selectedRule.Enabled = ChkRuleEnabled.IsChecked == true;
            MarkDirty();
            RefreshRulesList();
            ScheduleValidation();
        }

        private void CmbTriggerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            var picked = CmbTriggerType.SelectedItem as string;
            if (string.IsNullOrEmpty(picked)) return;
            if (_selectedRule.Trigger?.Type == picked) return;

            _selectedRule.Trigger = picked switch
            {
                TriggerTypes.GazeTarget    => new GazeTargetTrigger(),
                TriggerTypes.GazeAvoid     => new GazeAvoidTrigger(),
                TriggerTypes.AttentionLost => new AttentionLostTrigger(),
                TriggerTypes.BlinkDetected => new BlinkDetectedTrigger(),
                TriggerTypes.MouthOpen     => new MouthOpenTrigger(),
                TriggerTypes.TimeReached   => new TimeReachedTrigger { Time = Math.Max(0, _currentSeconds) },
                TriggerTypes.RegionEntered => new RegionEnteredTrigger(),
                TriggerTypes.RegionExited  => new RegionExitedTrigger(),
                _                          => _selectedRule.Trigger
            };
            EndGazePick(commit: false);
            BuildTriggerFields();
            MarkDirty();
            RefreshRulesList();
            ScheduleValidation();
        }

        private void CmbActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            var picked = CmbActionType.SelectedItem as string;
            if (string.IsNullOrEmpty(picked)) return;
            if (_selectedRule.Action?.Type == picked) return;

            _selectedRule.Action = picked switch
            {
                ActionTypes.Seek          => new SeekAction { Target = SeekTargets.Time, Time = 0 },
                ActionTypes.LoopRegion    => new LoopRegionAction(),
                ActionTypes.Pause         => new PauseAction(),
                ActionTypes.PlayAudio     => new PlayAudioAction(),
                ActionTypes.TriggerHaptic => new TriggerHapticAction { PatternName = "Pulse" },
                ActionTypes.ScreenShake   => new ScreenShakeAction(),
                ActionTypes.SetIntensity  => new SetIntensityAction(),
                _                         => _selectedRule.Action
            };
            BuildActionFields();
            MarkDirty();
            RefreshRulesList();
            ScheduleValidation();
        }

        private void CmbRuleRegion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            var idx = CmbRuleRegion.SelectedIndex;
            if (idx <= 0)
            {
                _selectedRule.RegionConstraint = null;
            }
            else if (idx - 1 < _enhancement.Regions.Count)
            {
                _selectedRule.RegionConstraint = _enhancement.Regions[idx - 1].Id;
            }
            MarkDirty();
            ScheduleValidation();
        }

        private void TxtRuleCooldown_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRuleSync || _selectedRule == null) return;
            if (int.TryParse(TxtRuleCooldown.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                _selectedRule.CooldownMs = Math.Max(0, ms);
            MarkDirty();
            ScheduleValidation();
        }

        // -- Dynamic field builders (programmatic; one block per type) --------

        private void BuildTriggerFields()
        {
            TriggerFields.Children.Clear();
            if (_selectedRule?.Trigger == null) return;

            switch (_selectedRule.Trigger)
            {
                case GazeTargetTrigger g:
                    AddRectFields(TriggerFields, g.Rect, () => g.Rect);
                    AddIntField(TriggerFields, Loc.Get("deeper_editor_trigger_min_dwell"),
                        g.MinDwellMs, v => g.MinDwellMs = Math.Max(0, v));
                    break;
                case GazeAvoidTrigger g:
                    AddRectFields(TriggerFields, g.Rect, () => g.Rect);
                    AddIntField(TriggerFields, Loc.Get("deeper_editor_trigger_min_dwell"),
                        g.MinDwellMs, v => g.MinDwellMs = Math.Max(0, v));
                    break;
                case AttentionLostTrigger a:
                    AddIntField(TriggerFields, Loc.Get("deeper_editor_trigger_min_duration"),
                        a.MinDurationMs, v => a.MinDurationMs = Math.Max(0, v));
                    break;
                case BlinkDetectedTrigger:
                case MouthOpenTrigger:
                    AddInfoText(TriggerFields, Loc.Get("deeper_editor_trigger_no_params"));
                    break;
                case TimeReachedTrigger tr:
                    AddDoubleField(TriggerFields, Loc.Get("deeper_editor_trigger_time"),
                        tr.Time, v => tr.Time = Math.Max(0, v));
                    break;
                case RegionEnteredTrigger re:
                    AddRegionPicker(TriggerFields, re.RegionId, id => re.RegionId = id);
                    break;
                case RegionExitedTrigger rx:
                    AddRegionPicker(TriggerFields, rx.RegionId, id => rx.RegionId = id);
                    break;
            }
        }

        private void BuildActionFields()
        {
            ActionFields.Children.Clear();
            if (_selectedRule?.Action == null) return;

            switch (_selectedRule.Action)
            {
                case SeekAction seek:
                    AddSeekFields(seek);
                    break;
                case LoopRegionAction loop:
                    AddRegionPicker(ActionFields, loop.RegionId ?? "",
                        id => loop.RegionId = string.IsNullOrEmpty(id) ? null : id,
                        allowNone: true);
                    break;
                case PauseAction:
                    AddInfoText(ActionFields, Loc.Get("deeper_editor_action_pause_info"));
                    break;
                case PlayAudioAction pa:
                    AddTextField(ActionFields, Loc.Get("deeper_editor_action_audio_path"),
                        pa.Path, v => pa.Path = v);
                    AddIntField(ActionFields, Loc.Get("deeper_editor_action_volume"),
                        pa.Volume, v => pa.Volume = Math.Clamp(v, 0, 100));
                    AddBoolField(ActionFields, Loc.Get("deeper_editor_action_duck"),
                        pa.DuckOtherAudio, v => pa.DuckOtherAudio = v);
                    break;
                case TriggerHapticAction h:
                    AddHapticActionFields(h);
                    break;
                case ScreenShakeAction ss:
                    AddDoubleField(ActionFields, Loc.Get("deeper_editor_action_intensity"),
                        ss.Intensity, v => ss.Intensity = Math.Clamp(v, 0, 1));
                    AddIntField(ActionFields, Loc.Get("deeper_editor_action_duration_ms"),
                        ss.DurationMs, v => ss.DurationMs = Math.Max(50, v));
                    break;
                case SetIntensityAction si:
                    AddDoubleField(ActionFields, Loc.Get("deeper_editor_action_value_0_1"),
                        si.Value, v => si.Value = Math.Clamp(v, 0, 1));
                    break;
            }
        }

        private void AddRectFields(Panel host, double[] rect, Func<double[]> getter)
        {
            var grid = new Grid();
            for (int i = 0; i < 4; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string[] labels = { "x", "y", "w", "h" };
            for (int i = 0; i < 4; i++)
            {
                int captured = i;
                var stack = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 4, 0, 4, 0) };
                stack.Children.Add(new TextBlock
                {
                    Text = labels[i],
                    Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush"),
                    FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                var tb = new TextBox
                {
                    Style = (Style)FindResource("EditorTextBox"),
                    Text = rect.Length > i ? rect[i].ToString("0.##", CultureInfo.InvariantCulture) : "0"
                };
                tb.TextChanged += (_, _) =>
                {
                    if (_suppressRuleSync) return;
                    var arr = getter();
                    if (arr.Length > captured && double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        arr[captured] = Math.Clamp(v, 0, 1);
                        MarkDirty();
                        ScheduleValidation();
                    }
                };
                stack.Children.Add(tb);
                Grid.SetColumn(stack, i);
                grid.Children.Add(stack);
            }
            grid.Margin = new Thickness(0, 0, 0, 4);
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_trigger_rect"),
                Style = (Style)FindResource("EditorLabel")
            });
            host.Children.Add(grid);

            var pickBtn = new Button
            {
                Content = Loc.Get("deeper_editor_trigger_pick_on_video"),
                Padding = new Thickness(10, 4, 10, 4),
                Cursor = Cursors.Hand,
                Background = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent20Brush"),
                Foreground = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8)
            };
            pickBtn.Click += (_, _) => BeginGazePick(getter);
            host.Children.Add(pickBtn);
        }

        private void AddSeekFields(SeekAction seek)
        {
            ActionFields.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_action_seek_target"),
                Style = (Style)FindResource("EditorLabel")
            });
            var combo = new ComboBox
            {
                Style = (Style)FindResource("EditorComboBox"),
                ItemContainerStyle = (Style)FindResource("EditorComboBoxItem")
            };
            string[] targets = { SeekTargets.Time, SeekTargets.RegionStart, SeekTargets.RegionEnd };
            foreach (var t in targets) combo.Items.Add(t);
            var curIdx = Array.IndexOf(targets, seek.Target);
            combo.SelectedIndex = curIdx >= 0 ? curIdx : 0;

            var dynamicHost = new StackPanel();
            void RebuildDynamic()
            {
                dynamicHost.Children.Clear();
                if (seek.Target == SeekTargets.Time)
                {
                    AddDoubleField(dynamicHost, Loc.Get("deeper_editor_action_seek_time"),
                        seek.Time ?? 0, v => seek.Time = Math.Max(0, v));
                }
                else
                {
                    AddRegionPicker(dynamicHost, seek.RegionId ?? "",
                        id => seek.RegionId = string.IsNullOrEmpty(id) ? null : id);
                }
            }

            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                seek.Target = (combo.SelectedItem as string) ?? SeekTargets.Time;
                if (seek.Target == SeekTargets.Time && seek.Time == null) seek.Time = 0;
                RebuildDynamic();
                MarkDirty();
                ScheduleValidation();
            };

            ActionFields.Children.Add(combo);
            ActionFields.Children.Add(dynamicHost);
            RebuildDynamic();
        }

        private void AddHapticActionFields(TriggerHapticAction h)
        {
            ActionFields.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_haptic_pattern"),
                Style = (Style)FindResource("EditorLabel")
            });
            var combo = new ComboBox
            {
                Style = (Style)FindResource("EditorComboBox"),
                ItemContainerStyle = (Style)FindResource("EditorComboBoxItem")
            };
            foreach (var name in StockHapticPatterns.Names) combo.Items.Add(name);
            combo.Items.Add(Loc.Get("deeper_editor_haptic_pattern_custom"));

            var curveHost = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

            void SyncCurveVisibility()
            {
                bool isCustom = h.CustomPattern != null && h.CustomPattern.Count > 0;
                curveHost.Children.Clear();
                if (isCustom) BuildCurveEditor(curveHost, h);
            }

            int initialIdx = -1;
            bool initialCustom = h.CustomPattern != null && h.CustomPattern.Count > 0;
            if (initialCustom) initialIdx = StockHapticPatterns.Names.Count;
            else if (!string.IsNullOrEmpty(h.PatternName))
            {
                for (int i = 0; i < StockHapticPatterns.Names.Count; i++)
                    if (StockHapticPatterns.Names[i] == h.PatternName) { initialIdx = i; break; }
            }
            combo.SelectedIndex = initialIdx >= 0 ? initialIdx : 0;

            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                var idx = combo.SelectedIndex;
                if (idx < 0) return;
                if (idx < StockHapticPatterns.Names.Count)
                {
                    h.PatternName = StockHapticPatterns.Names[idx];
                    h.CustomPattern = null;
                }
                else
                {
                    h.CustomPattern ??= StockHapticPatterns.SeedCustomFrom(h.PatternName);
                    h.PatternName = null;
                }
                SyncCurveVisibility();
                MarkDirty();
                ScheduleValidation();
            };

            ActionFields.Children.Add(combo);
            ActionFields.Children.Add(curveHost);
            SyncCurveVisibility();

            AddDoubleField(ActionFields, Loc.Get("deeper_editor_action_intensity"),
                h.Intensity, v => h.Intensity = Math.Clamp(v, 0, 1));
            AddIntField(ActionFields, Loc.Get("deeper_editor_action_duration_ms"),
                h.DurationMs, v => h.DurationMs = Math.Max(50, v));
        }

        // Self-contained curve editor for any IHapticPatternTarget. Builds its
        // own canvas + handles + reset button into <paramref name="host"/>; safe
        // to use independently of the haptic-event editor's XAML CurveCanvas.
        private void BuildCurveEditor(Panel host, IHapticPatternTarget target)
        {
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_haptic_curve"),
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x10, 0, 0, 0x20)),
                BorderBrush = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent40Brush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3)
            };
            var canvas = new Canvas
            {
                Height = 100,
                Background = System.Windows.Media.Brushes.Transparent,
                ClipToBounds = true
            };
            border.Child = canvas;
            host.Children.Add(border);

            int draggingIdx = -1;

            void Render()
            {
                canvas.Children.Clear();
                if (target.CustomPattern == null || target.CustomPattern.Count == 0) return;
                var w = canvas.ActualWidth;
                var hgt = canvas.ActualHeight;
                if (w <= 0 || hgt <= 0) return;

                for (int i = 1; i <= 3; i++)
                {
                    var y = hgt * i / 4.0;
                    canvas.Children.Add(new System.Windows.Shapes.Line
                    {
                        X1 = 0, X2 = w, Y1 = y, Y2 = y,
                        Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 255, 255)),
                        StrokeThickness = 0.5,
                        IsHitTestVisible = false
                    });
                }

                var pts = target.CustomPattern;
                var geom = new System.Windows.Media.StreamGeometry();
                using (var ctx = geom.Open())
                {
                    ctx.BeginFigure(KeyframeToCanvas(pts[0], w, hgt), false, false);
                    for (int i = 1; i < pts.Count; i++)
                        ctx.LineTo(KeyframeToCanvas(pts[i], w, hgt), true, false);
                }
                geom.Freeze();
                canvas.Children.Add(new System.Windows.Shapes.Path
                {
                    Data = geom,
                    Stroke = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                    StrokeThickness = 1.6,
                    IsHitTestVisible = false
                });

                for (int i = 0; i < pts.Count; i++)
                {
                    var pt = KeyframeToCanvas(pts[i], w, hgt);
                    var dot = new System.Windows.Shapes.Ellipse
                    {
                        Width = 10, Height = 10,
                        Fill = (System.Windows.Media.Brush)FindResource("DeeperAccentBrush"),
                        Stroke = System.Windows.Media.Brushes.White,
                        StrokeThickness = 1.2,
                        Cursor = Cursors.SizeNS,
                        Tag = i
                    };
                    Canvas.SetLeft(dot, pt.X - 5);
                    Canvas.SetTop(dot, pt.Y - 5);
                    dot.MouseLeftButtonDown += (s, ev) =>
                    {
                        if (s is System.Windows.Shapes.Ellipse el && el.Tag is int idx)
                        {
                            draggingIdx = idx;
                            el.CaptureMouse();
                            ev.Handled = true;
                        }
                    };
                    dot.MouseMove += (s, ev) =>
                    {
                        if (draggingIdx < 0 || target.CustomPattern == null) return;
                        var hh = canvas.ActualHeight;
                        if (hh <= 0) return;
                        var pos = ev.GetPosition(canvas);
                        var v = Math.Clamp(1.0 - (pos.Y / hh), 0.0, 1.0);
                        target.CustomPattern[draggingIdx][1] = v;
                        MarkDirty();
                        Render();
                        ScheduleValidation();
                    };
                    dot.MouseLeftButtonUp += (s, ev) =>
                    {
                        if (s is System.Windows.Shapes.Ellipse el) el.ReleaseMouseCapture();
                        draggingIdx = -1;
                    };
                    canvas.Children.Add(dot);
                }
            }

            canvas.SizeChanged += (_, _) => Render();
            Render();

            var resetBtn = new Button
            {
                Content = Loc.Get("deeper_editor_haptic_curve_reset"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(8, 4, 8, 4),
                Cursor = Cursors.Hand,
                FontSize = 11,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("DeeperAccentTransparent40Brush")
            };
            resetBtn.Click += (_, _) =>
            {
                target.CustomPattern = StockHapticPatterns.SeedCustomFrom(null);
                MarkDirty();
                Render();
                ScheduleValidation();
            };
            host.Children.Add(resetBtn);
        }

        // -- Tiny field helpers ------------------------------------------------

        private void AddIntField(Panel host, string label, int value, Action<int> setter)
        {
            host.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("EditorLabel") });
            var tb = new TextBox
            {
                Style = (Style)FindResource("EditorTextBox"),
                Text = value.ToString(CultureInfo.InvariantCulture)
            };
            tb.TextChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                if (int.TryParse(tb.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                {
                    setter(v); MarkDirty(); ScheduleValidation();
                }
            };
            host.Children.Add(tb);
        }

        private void AddDoubleField(Panel host, string label, double value, Action<double> setter)
        {
            host.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("EditorLabel") });
            var tb = new TextBox
            {
                Style = (Style)FindResource("EditorTextBox"),
                Text = value.ToString("0.###", CultureInfo.InvariantCulture)
            };
            tb.TextChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                if (double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    setter(v); MarkDirty(); ScheduleValidation();
                }
            };
            host.Children.Add(tb);
        }

        private void AddTextField(Panel host, string label, string value, Action<string> setter)
        {
            host.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("EditorLabel") });
            var tb = new TextBox
            {
                Style = (Style)FindResource("EditorTextBox"),
                Text = value ?? ""
            };
            tb.TextChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                setter(tb.Text ?? "");
                MarkDirty();
                ScheduleValidation();
            };
            host.Children.Add(tb);
        }

        private void AddBoolField(Panel host, string label, bool value, Action<bool> setter)
        {
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = value,
                Foreground = (System.Windows.Media.Brush)FindResource("TextLightBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            cb.Click += (_, _) =>
            {
                if (_suppressRuleSync) return;
                setter(cb.IsChecked == true);
                MarkDirty();
                ScheduleValidation();
            };
            host.Children.Add(cb);
        }

        private void AddInfoText(Panel host, string text)
        {
            host.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
                FontSize = 11, FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        private void AddRegionPicker(Panel host, string currentId, Action<string> setter, bool allowNone = false)
        {
            host.Children.Add(new TextBlock
            {
                Text = Loc.Get("deeper_editor_rule_region_id"),
                Style = (Style)FindResource("EditorLabel")
            });
            var combo = new ComboBox
            {
                Style = (Style)FindResource("EditorComboBox"),
                ItemContainerStyle = (Style)FindResource("EditorComboBoxItem")
            };
            int selected = -1;
            if (allowNone) combo.Items.Add(Loc.Get("deeper_editor_rule_region_current"));
            for (int i = 0; i < _enhancement.Regions.Count; i++)
            {
                var r = _enhancement.Regions[i];
                combo.Items.Add(string.IsNullOrEmpty(r.Label) ? r.Id : $"{r.Id} — {r.Label}");
                if (r.Id == currentId) selected = i + (allowNone ? 1 : 0);
            }
            if (selected < 0) selected = combo.Items.Count > 0 ? 0 : -1;
            combo.SelectedIndex = selected;

            combo.SelectionChanged += (_, _) =>
            {
                if (_suppressRuleSync) return;
                var idx = combo.SelectedIndex;
                if (allowNone && idx == 0) { setter(""); }
                else if (idx >= 0)
                {
                    var regionIdx = allowNone ? idx - 1 : idx;
                    if (regionIdx >= 0 && regionIdx < _enhancement.Regions.Count)
                        setter(_enhancement.Regions[regionIdx].Id);
                }
                MarkDirty();
                ScheduleValidation();
            };
            host.Children.Add(combo);
        }

        // -- Gaze rect picker --------------------------------------------------
        // Picker lives in a separate transparent Window because LibVLC's
        // VideoView renders in a native child HWND that wins WPF airspace; an
        // inline overlay would render behind the video and be invisible.

        private void BeginGazePick(Func<double[]> rectGetter)
        {
            EndGazePick(commit: false);

            var current = rectGetter();
            if (current == null || current.Length < 4)
                current = new[] { 0.25, 0.25, 0.5, 0.5 };

            // Position the picker exactly over the editor's preview host so
            // its normalized rect maps 1:1 to the video display rect.
            try
            {
                var origin = PreviewHost.PointToScreen(new Point(0, 0));
                var farCorner = PreviewHost.PointToScreen(new Point(PreviewHost.ActualWidth, PreviewHost.ActualHeight));
                var dpi = VisualTreeHelper.GetDpi(this);

                _gazePickerWindow = new GazePickerWindow(current)
                {
                    Owner = this,
                    Left = origin.X / dpi.DpiScaleX,
                    Top = origin.Y / dpi.DpiScaleY,
                    Width = (farCorner.X - origin.X) / dpi.DpiScaleX,
                    Height = (farCorner.Y - origin.Y) / dpi.DpiScaleY
                };
                _gazePickerWindow.Closed += (_, _) =>
                {
                    var w = _gazePickerWindow;
                    _gazePickerWindow = null;
                    if (w != null && w.Committed)
                    {
                        var result = w.ResultRect;
                        // Write back into the trigger's rect array via the same
                        // reference the caller handed us. Both arrays may be
                        // distinct (Window cloned the input) so copy elementwise.
                        var tgt = rectGetter();
                        if (tgt != null && tgt.Length >= 4 && result.Length >= 4)
                        {
                            tgt[0] = result[0];
                            tgt[1] = result[1];
                            tgt[2] = result[2];
                            tgt[3] = result[3];
                        }
                        MarkDirty();
                        if (_selectedRule != null) BuildTriggerFields();
                        ScheduleValidation();
                    }
                };
                _gazePickerWindow.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BeginGazePick failed: {Error}", ex.Message);
            }
        }

        private void EndGazePick(bool commit)
        {
            // Force-close any open picker. The Closed handler reads Committed
            // (set by the picker's own Done/Cancel/Esc/Enter handlers); calling
            // Close here without committing leaves Committed=false → no apply.
            if (_gazePickerWindow == null) return;
            try { _gazePickerWindow.Close(); }
            catch { }
            _gazePickerWindow = null;
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
            else if (e.Key == Key.H && !inTextBox && _totalSeconds > 0)
            {
                CreateHapticEventAtPlayhead();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && !inTextBox && _selectedRegion != null)
            {
                BtnDeleteRegion_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && !inTextBox && _selectedHaptic != null)
            {
                BtnDeleteHaptic_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && !inTextBox && (_selectedRegion != null || _selectedHaptic != null))
            {
                SelectNothing();
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

            EndGazePick(commit: false);
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
