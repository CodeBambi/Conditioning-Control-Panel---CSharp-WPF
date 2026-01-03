using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Microsoft.Win32;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Session editor window with timeline and drag-drop features
    /// </summary>
    public partial class SessionEditorWindow : Window
    {
        private readonly TimelineSession _session;
        private readonly SessionFileService _fileService;
        private readonly Dictionary<string, FeatureIconState> _iconStates = new();

        // Drag-drop state
        private bool _isDragging;

        /// <summary>
        /// Result session after save (null if cancelled)
        /// </summary>
        public Session? ResultSession { get; private set; }

        public SessionEditorWindow() : this(null) { }

        public SessionEditorWindow(Session? existingSession)
        {
            InitializeComponent();

            _fileService = new SessionFileService();

            if (existingSession != null)
            {
                _session = TimelineSession.FromSession(existingSession);
                TxtSessionName.Text = _session.Name;
                TxtDescription.Text = _session.Description;
                SliderDuration.Value = _session.DurationMinutes;
            }
            else
            {
                _session = new TimelineSession();
            }

            InitializeFeatureIcons();
            RefreshTimeline();
            RefreshStats();
        }

        #region Window Chrome

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close popup if clicking outside of it
            if (SettingsPopup.IsOpen)
            {
                // Check if click is inside the popup
                var popupContent = FeatureSettings;
                if (popupContent != null)
                {
                    var mousePos = e.GetPosition(popupContent);
                    var bounds = new Rect(0, 0, popupContent.ActualWidth, popupContent.ActualHeight);

                    if (!bounds.Contains(mousePos))
                    {
                        SettingsPopup.IsOpen = false;
                    }
                }
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            TutorialOverlay.Visibility = Visibility.Visible;
        }

        private void TutorialOverlay_Close(object sender, RoutedEventArgs e)
        {
            TutorialOverlay.Visibility = Visibility.Collapsed;
        }

        private void TutorialOverlay_Close(object sender, MouseButtonEventArgs e)
        {
            TutorialOverlay.Visibility = Visibility.Collapsed;
        }

        private void TutorialContent_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Prevent closing when clicking on tutorial content
            e.Handled = true;
        }

        #endregion

        #region Feature Icons

        private void InitializeFeatureIcons()
        {
            var features = FeatureDefinition.GetAllFeatures();

            foreach (var feature in features)
            {
                var panel = GetCategoryPanel(feature.Category);
                if (panel == null) continue;

                var icon = CreateFeatureIcon(feature);
                panel.Children.Add(icon);

                // Initialize icon state
                _iconStates[feature.Id] = new FeatureIconState
                {
                    FeatureId = feature.Id,
                    IsStartMode = true,
                    PendingStartEventId = null
                };
            }

            // Update icon states based on existing session events
            UpdateIconStatesFromSession();
        }

        private Panel? GetCategoryPanel(FeatureCategory category)
        {
            return category switch
            {
                FeatureCategory.Audio => AudioFeatures,
                FeatureCategory.Video => VideoFeatures,
                FeatureCategory.Overlays => OverlayFeatures,
                FeatureCategory.Interactive => InteractiveFeatures,
                FeatureCategory.Extras => ExtrasFeatures,
                _ => null
            };
        }

        private Border CreateFeatureIcon(FeatureDefinition feature)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 66)), // #252542
                CornerRadius = new CornerRadius(10),
                Width = 75,
                Height = 75,
                Margin = new Thickness(6),
                Cursor = Cursors.Hand,
                Tag = feature.Id,
                ToolTip = $"{feature.Name}\n{GetFeatureDescription(feature)}\n\nDrag to timeline to add"
            };

            var grid = new Grid();

            // Icon emoji
            var iconText = new TextBlock
            {
                Text = feature.Icon,
                FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(iconText);

            // Start/Stop indicator (small colored dot)
            var indicator = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)), // Green for start
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 5, 5),
                Tag = "indicator"
            };
            grid.Children.Add(indicator);

            border.Child = grid;

            // Drag events
            border.MouseLeftButtonDown += FeatureIcon_MouseLeftButtonDown;
            border.MouseMove += FeatureIcon_MouseMove;

            return border;
        }

        private string GetFeatureDescription(FeatureDefinition feature)
        {
            return feature.Id switch
            {
                "audio_whispers" => "Plays audio whispers throughout the session",
                "mind_wipe" => "Powerful audio effect for deep immersion",
                "flash" => "Flashes images on screen periodically",
                "mandatory_videos" => "Plays mandatory video clips",
                "subliminal" => "Shows subliminal text messages",
                "bouncing_text" => "Displays bouncing text across the screen",
                "pink_filter" => "Applies a pink color filter overlay",
                "spiral" => "Shows a hypnotic spiral overlay",
                "brain_drain" => "Intense visual distortion effect",
                "bubbles" => "Floating interactive bubbles",
                "lock_cards" => "Interactive lock card challenges",
                "bubble_count" => "Bubble counting mini-game",
                "corner_gif" => "Displays a GIF in the corner",
                _ => "An effect for your session"
            };
        }

        private void UpdateIconStatesFromSession()
        {
            // Find unpaired start events and set icon states accordingly
            foreach (var evt in _session.Events.Where(e => e.EventType == TimelineEventType.Start))
            {
                if (string.IsNullOrEmpty(evt.PairedEventId))
                {
                    // This start event has no stop - icon should be in "stop" mode
                    if (_iconStates.TryGetValue(evt.FeatureId, out var state))
                    {
                        state.IsStartMode = false;
                        state.PendingStartEventId = evt.Id;
                    }
                }
            }

            RefreshIconIndicators();
        }

        private void RefreshIconIndicators()
        {
            foreach (var panel in new Panel[] { AudioFeatures, VideoFeatures, OverlayFeatures, InteractiveFeatures, ExtrasFeatures })
            {
                foreach (var child in panel.Children)
                {
                    if (child is not Border icon) continue;

                    var featureId = icon.Tag as string;
                    if (featureId == null || !_iconStates.TryGetValue(featureId, out var state))
                        continue;

                    var grid = icon.Child as Grid;
                    var indicator = grid?.Children.OfType<Ellipse>().FirstOrDefault(e => e.Tag as string == "indicator");
                    if (indicator != null)
                    {
                        indicator.Fill = state.IsStartMode
                            ? new SolidColorBrush(Color.FromRgb(76, 175, 80))   // Green
                            : new SolidColorBrush(Color.FromRgb(244, 67, 54));  // Red
                    }
                }
            }
        }

        #endregion

        #region Drag and Drop

        private void FeatureIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
        }

        private void FeatureIcon_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
            {
                _isDragging = false;
                return;
            }

            var border = sender as Border;
            var featureId = border?.Tag as string;
            if (featureId == null) return;

            if (!_iconStates.TryGetValue(featureId, out var state))
                return;

            var data = new DataObject();
            data.SetData("FeatureId", featureId);
            data.SetData("IsStart", state.IsStartMode);
            data.SetData("PendingStartEventId", state.PendingStartEventId ?? "");

            DragDrop.DoDragDrop(border, data, DragDropEffects.Copy);
            _isDragging = false;
        }

        private void Timeline_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FeatureId"))
            {
                e.Effects = DragDropEffects.Copy;
                // Show visual feedback
                var border = sender as Border;
                if (border != null)
                {
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 105, 180)); // Pink
                    border.BorderThickness = new Thickness(2);
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Timeline_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("FeatureId"))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Timeline_DragLeave(object sender, DragEventArgs e)
        {
            var border = sender as Border;
            if (border != null)
            {
                border.BorderBrush = null;
                border.BorderThickness = new Thickness(0);
            }
        }

        private void Timeline_Drop(object sender, DragEventArgs e)
        {
            // Reset visual
            var border = sender as Border;
            if (border != null)
            {
                border.BorderBrush = null;
                border.BorderThickness = new Thickness(0);
            }

            if (!e.Data.GetDataPresent("FeatureId"))
                return;

            var featureId = e.Data.GetData("FeatureId") as string;
            var isStart = (bool)e.Data.GetData("IsStart");
            var pendingStartId = e.Data.GetData("PendingStartEventId") as string;

            if (featureId == null) return;

            // Calculate minute from drop position
            var position = e.GetPosition(CanvasTimeline);
            var minute = PositionToMinute(position.X);

            if (isStart)
            {
                // Add start event
                var evt = _session.AddStartEvent(featureId, minute);

                // Update icon state
                if (_iconStates.TryGetValue(featureId, out var state))
                {
                    state.IsStartMode = false;
                    state.PendingStartEventId = evt.Id;
                }
            }
            else
            {
                // Add stop event paired to the pending start
                if (!string.IsNullOrEmpty(pendingStartId))
                {
                    var startEvent = _session.Events.FirstOrDefault(ev => ev.Id == pendingStartId);
                    if (startEvent != null)
                    {
                        // Ensure stop is after start
                        if (minute <= startEvent.Minute)
                        {
                            minute = Math.Min(startEvent.Minute + 1, _session.DurationMinutes);
                        }

                        _session.AddStopEvent(startEvent, minute);
                    }
                }

                // Reset icon state
                if (_iconStates.TryGetValue(featureId, out var state))
                {
                    state.IsStartMode = true;
                    state.PendingStartEventId = null;
                }
            }

            RefreshIconIndicators();
            RefreshTimeline();
            RefreshStats();
        }

        #endregion

        #region Timeline Rendering

        private void RefreshTimeline()
        {
            RenderMarkers();
            RenderEvents();

            // Hide hint if there are events
            TxtTimelineHint.Visibility = _session.Events.Any() ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RenderMarkers()
        {
            CanvasMarkers.Children.Clear();

            var duration = _session.DurationMinutes;
            var width = CanvasMarkers.ActualWidth > 0 ? CanvasMarkers.ActualWidth : 800;

            // Calculate interval (aim for 5-10 markers, more for longer durations)
            int interval = duration <= 30 ? 5 : (duration <= 60 ? 10 : (duration <= 120 ? 15 : 30));

            for (int min = 0; min <= duration; min += interval)
            {
                var x = MinuteToPosition(min, width);

                // Marker line
                var line = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 15,
                    Y2 = 20,
                    Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                    StrokeThickness = 1
                };
                CanvasMarkers.Children.Add(line);

                // Marker text - show hours:minutes for durations over 60 min
                string markerText;
                if (duration > 60)
                {
                    int hours = min / 60;
                    int mins = min % 60;
                    markerText = hours > 0 ? $"{hours}:{mins:D2}" : mins.ToString();
                }
                else
                {
                    markerText = min.ToString();
                }

                var text = new TextBlock
                {
                    Text = markerText,
                    Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136)),
                    FontSize = 10
                };
                Canvas.SetLeft(text, x - 8);
                Canvas.SetTop(text, 0);
                CanvasMarkers.Children.Add(text);
            }
        }

        private void RenderEvents()
        {
            CanvasTimeline.Children.Clear();
            CanvasTimeline.Children.Add(TxtTimelineHint);

            var width = CanvasTimeline.ActualWidth > 0 ? CanvasTimeline.ActualWidth : 800;

            // Track icon positions to detect overlaps
            var iconPositions = new List<(double x, double y, int minute)>();

            // Group events by feature for vertical positioning
            var featureRows = new Dictionary<string, int>();
            int rowIndex = 0;

            foreach (var evt in _session.Events.Where(e => e.EventType == TimelineEventType.Start).OrderBy(e => e.Minute))
            {
                if (!featureRows.ContainsKey(evt.FeatureId))
                {
                    featureRows[evt.FeatureId] = rowIndex++;
                }

                var feature = FeatureDefinition.GetById(evt.FeatureId);
                if (feature == null) continue;

                var row = featureRows[evt.FeatureId];
                var baseY = 10 + (row % 4) * 20; // Max 4 rows visible

                var startX = MinuteToPosition(evt.Minute, width);
                var stopEvt = _session.GetPairedStopEvent(evt);
                var endX = stopEvt != null
                    ? MinuteToPosition(stopEvt.Minute, width)
                    : MinuteToPosition(_session.DurationMinutes, width);

                // Calculate vertical offset for overlapping start icons
                var startY = baseY;
                int startOverlapCount = iconPositions.Count(p => Math.Abs(p.x - startX) < 22 && Math.Abs(p.y - baseY) < 22);
                if (startOverlapCount > 0)
                {
                    startY = baseY + (startOverlapCount * 22);
                }
                iconPositions.Add((startX, startY, evt.Minute));

                // Calculate vertical offset for overlapping stop icons
                var stopY = baseY;
                if (stopEvt != null)
                {
                    int stopOverlapCount = iconPositions.Count(p => Math.Abs(p.x - endX) < 22 && Math.Abs(p.y - baseY) < 22);
                    if (stopOverlapCount > 0)
                    {
                        stopY = baseY + (stopOverlapCount * 22);
                    }
                    iconPositions.Add((endX, stopY, stopEvt.Minute));
                }

                // Draw connection bar at the lower of the two positions
                var barY = Math.Max(startY, stopY);
                var bar = new Rectangle
                {
                    Width = Math.Max(endX - startX, 10),
                    Height = 14,
                    Fill = new SolidColorBrush(Color.FromArgb(80, 255, 105, 180)),
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(bar, startX);
                Canvas.SetTop(bar, barY + 3);
                CanvasTimeline.Children.Add(bar);

                // Start icon (green)
                var startIcon = CreateTimelineIcon(evt, feature, true);
                Canvas.SetLeft(startIcon, startX - 10);
                Canvas.SetTop(startIcon, startY);
                CanvasTimeline.Children.Add(startIcon);

                // Stop icon (red) if exists
                if (stopEvt != null)
                {
                    var stopIcon = CreateTimelineIcon(stopEvt, feature, false);
                    Canvas.SetLeft(stopIcon, endX - 10);
                    Canvas.SetTop(stopIcon, stopY);
                    CanvasTimeline.Children.Add(stopIcon);
                }
            }
        }

        private Border CreateTimelineIcon(TimelineEvent evt, FeatureDefinition feature, bool isStart)
        {
            var border = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = isStart
                    ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                    : new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                Cursor = Cursors.Hand,
                Tag = evt.Id,
                ToolTip = $"{feature.Name} - {(isStart ? "Start" : "Stop")} at {evt.Minute} min\nClick to edit settings"
            };

            var text = new TextBlock
            {
                Text = feature.Icon,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            border.Child = text;
            border.MouseLeftButtonDown += TimelineIcon_Click;

            return border;
        }

        private void TimelineIcon_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            var eventId = border?.Tag as string;
            if (eventId == null) return;

            var evt = _session.Events.FirstOrDefault(ev => ev.Id == eventId);
            if (evt == null) return;

            ShowFeatureSettingsPopup(evt);
            e.Handled = true;
        }

        // Padding to prevent icons from being clipped at edges
        private const double TimelinePadding = 15;

        private double MinuteToPosition(int minute, double width)
        {
            // Add padding on both sides so icons at 0 and end aren't clipped
            var usableWidth = width - (TimelinePadding * 2);
            return TimelinePadding + (minute / (double)_session.DurationMinutes) * usableWidth;
        }

        private int PositionToMinute(double x)
        {
            var width = CanvasTimeline.ActualWidth > 0 ? CanvasTimeline.ActualWidth : 800;
            var usableWidth = width - (TimelinePadding * 2);
            var adjustedX = x - TimelinePadding;
            var minute = (int)Math.Round((adjustedX / usableWidth) * _session.DurationMinutes);
            return Math.Max(0, Math.Min(minute, _session.DurationMinutes));
        }

        #endregion

        #region Settings Popup

        private void ShowFeatureSettingsPopup(TimelineEvent evt)
        {
            // Load event into popup with session reference for phrase management
            FeatureSettings.LoadEvent(evt, _session.DurationMinutes, _session);

            // Wire up events
            FeatureSettings.SettingsChanged -= OnSettingsChanged;
            FeatureSettings.SettingsChanged += OnSettingsChanged;

            FeatureSettings.DeleteRequested -= OnDeleteRequested;
            FeatureSettings.DeleteRequested += OnDeleteRequested;

            FeatureSettings.CloseRequested -= OnPopupCloseRequested;
            FeatureSettings.CloseRequested += OnPopupCloseRequested;

            // Show popup
            SettingsPopup.IsOpen = true;
        }

        private void OnSettingsChanged(object? sender, TimelineEvent evt)
        {
            RefreshTimeline();
            RefreshStats();
        }

        private void OnDeleteRequested(object? sender, TimelineEvent evt)
        {
            SettingsPopup.IsOpen = false;

            _session.RemoveEvent(evt);

            // Reset icon state if needed
            var remainingStarts = _session.Events.Count(e => e.FeatureId == evt.FeatureId && e.EventType == TimelineEventType.Start);
            if (remainingStarts == 0 && _iconStates.TryGetValue(evt.FeatureId, out var state))
            {
                state.IsStartMode = true;
                state.PendingStartEventId = null;
            }

            RefreshIconIndicators();
            RefreshTimeline();
            RefreshStats();
        }

        private void OnPopupCloseRequested(object? sender, EventArgs e)
        {
            SettingsPopup.IsOpen = false;
        }

        #endregion

        #region Stats

        private void RefreshStats()
        {
            var xp = _session.CalculateXP();
            var difficulty = _session.CalculateDifficulty();
            var difficultyText = _session.GetDifficultyText();
            var difficultyColor = _session.GetDifficultyColor();

            TxtXP.Text = $"+{xp} XP";
            TxtDifficulty.Text = difficultyText;
            TxtDifficulty.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(difficultyColor));
            TxtDuration.Text = $"{_session.DurationMinutes} min";
        }

        private void SliderDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_session == null) return;

            _session.DurationMinutes = (int)e.NewValue;
            TxtDurationValue.Text = $"{_session.DurationMinutes} min";

            // Clamp any events that exceed the new duration
            foreach (var evt in _session.Events.Where(ev => ev.Minute > _session.DurationMinutes).ToList())
            {
                evt.Minute = _session.DurationMinutes;
            }

            RefreshTimeline();
            RefreshStats();
        }

        #endregion

        #region Buttons

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Session Files (*.session.json)|*.session.json|All Files (*.*)|*.*",
                Title = "Import Session"
            };

            if (dialog.ShowDialog() == true)
            {
                if (!_fileService.ValidateSessionFile(dialog.FileName, out var error))
                {
                    MessageBox.Show($"Invalid session file: {error}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var definition = _fileService.ImportSession(dialog.FileName);
                if (definition == null)
                {
                    MessageBox.Show("Failed to import session", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Load imported session into editor
                var imported = definition.ToSession();
                var timelineSession = TimelineSession.FromSession(imported);

                // Update all fields
                _session.Id = timelineSession.Id;
                _session.Name = timelineSession.Name;
                _session.Icon = timelineSession.Icon;
                _session.Description = timelineSession.Description;
                _session.DurationMinutes = timelineSession.DurationMinutes;
                _session.Events.Clear();
                _session.Events.AddRange(timelineSession.Events);
                _session.SubliminalPhrases = new List<string>(timelineSession.SubliminalPhrases);
                _session.BouncingTextPhrases = new List<string>(timelineSession.BouncingTextPhrases);

                // Update UI
                TxtSessionName.Text = _session.Name;
                TxtDescription.Text = _session.Description;
                SliderDuration.Value = _session.DurationMinutes;

                // Reset icon states
                foreach (var state in _iconStates.Values)
                {
                    state.IsStartMode = true;
                    state.PendingStartEventId = null;
                }
                UpdateIconStatesFromSession();

                RefreshTimeline();
                RefreshStats();

                MessageBox.Show($"Imported: {_session.Name}", "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            // Update session from UI
            _session.Name = TxtSessionName.Text;
            _session.Description = TxtDescription.Text;

            var dialog = new SaveFileDialog
            {
                Filter = "Session Files (*.session.json)|*.session.json",
                Title = "Export Session",
                FileName = SessionFileService.GetExportFileName(_session.ToSession())
            };

            if (dialog.ShowDialog() == true)
            {
                var session = _session.ToSession();
                _fileService.ExportSession(session, dialog.FileName);
                MessageBox.Show($"Session exported to:\n{dialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ResultSession = null;
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // Update session from UI
            _session.Name = TxtSessionName.Text;
            _session.Description = TxtDescription.Text;

            if (string.IsNullOrWhiteSpace(_session.Name))
            {
                MessageBox.Show("Please enter a session name", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultSession = _session.ToSession();
            DialogResult = true;
            Close();
        }

        #endregion
    }

    /// <summary>
    /// Tracks the state of a feature icon (green/red mode)
    /// </summary>
    internal class FeatureIconState
    {
        public string FeatureId { get; set; } = "";
        public bool IsStartMode { get; set; } = true;
        public string? PendingStartEventId { get; set; }
    }
}
